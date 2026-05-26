# TextEditor API

A high-performance, feature-complete text-editor engine for .NET 8.
Everything a code editor needs — from the piece-table buffer to syntax highlighting,
multi-cursor editing, code folding, snippet expansion, and a live C# REPL — packaged
as a clean, testable library with no UI dependencies.

---

## Table of Contents

- [Why](#why)
- [Quick Start](#quick-start)
- [Architecture](#architecture)
- [Feature Reference](#feature-reference)
  - [Core Buffer](#1-core-buffer--piece-table)
  - [Cursor & Selection](#2-cursor--selection)
  - [Multi-Cursor](#3-multi-cursor)
  - [Undo / Redo](#4-undo--redo)
  - [Search & Replace](#5-search--replace)
  - [Syntax Highlighting](#6-syntax-highlighting)
  - [Diff Engine](#7-diff-engine)
  - [Bracket Matching & Auto-Indent](#8-bracket-matching--auto-indent)
  - [Code Folding](#9-code-folding)
  - [Encoding Detection](#10-encoding-detection--bom-handling)
  - [Unicode / Grapheme Clusters](#11-unicode--grapheme-clusters)
  - [Word Wrap Layout](#12-word-wrap-layout)
  - [Inlay Hints](#13-inlay-hints)
  - [Snippets](#14-snippet-engine)
  - [Bracket Pair Colorization](#15-bracket-pair-colorization)
  - [TextMate Grammar Tokeniser](#16-textmate-grammar-tokeniser)
  - [Indent Guides](#17-indent-guide-computation)
  - [Multi-line Paste](#18-multi-line-paste)
  - [Read-Only Regions](#19-read-only-regions)
  - [Sticky Scroll](#20-sticky-scroll-context)
  - [Document Outline](#21-document-outline)
  - [Cursor Position History](#22-cursor-position-history)
  - [Bookmarks](#23-bookmarks)
  - [Document Formatting](#24-document-formatting)
  - [Line Comment Toggle](#25-line-comment-toggle)
  - [Document Cleanup](#26-document-cleanup)
  - [Column Selection](#27-column-box-selection)
  - [Change Tracking](#28-change-tracking)
  - [Scripting / Macro Runner](#29-scripting--macro-runner)
- [C# REPL](#c-repl)
- [Plugin System](#plugin-system)
- [Performance](#performance)
- [Building & Testing](#building--testing)
- [Project Structure](#project-structure)

---

## Why

Most editor-engine libraries either wrap a native component (limiting portability and
testability) or implement only a handful of features. This library provides the full
stack — from the O(log n) piece-table buffer to TextMate grammar tokenisation — in
pure managed C# with **1 809 tests** and no runtime dependencies beyond the BCL.

| Benchmark | TextEditor API | VS Code (reference) |
|---|---|---|
| `GetLine` after 10 000 edits | **25 ms** | ~300 ms |
| `ReplaceAll` 128 k occurrences | **< 500 ms** O(n) | ~17 s O(n log n) |

---

## Quick Start

```bash
dotnet add reference path/to/TextEditor.Core.csproj
```

```csharp
using TextEditor.Core;
using TextEditor.Core.Cursor;

var doc = new TextDocument();
doc.Load("Hello, World!\nSecond line.");

// Edit
doc.Insert(7, "Beautiful ");
Console.WriteLine(doc.GetLine(0));   // Hello, Beautiful World!

// Undo
doc.Undo();
Console.WriteLine(doc.GetLine(0));   // Hello, World!

// Search
var match = doc.FindFirst("World");
Console.WriteLine($"Found at offset {match!.Value.Offset}");

// Multi-cursor
var mc = new MultiCursor(doc);
mc.AddColumnSelection(0, 1, 0);     // box-select column 0, lines 0–1
mc.InsertText("> ");                 // inserts at every cursor, one undo step

// Navigate
doc.GoTo(1, 0);                      // jump + push cursor history
doc.GetCursorHistory().Back();       // go back
```

---

## Architecture

```
TextEditor.Core
├── Buffer/          PieceTable + RB-tree  — O(log n) insert/delete/getline
├── Commands/        IEditorCommand, CommandHistory (undo/redo + smart grouping)
├── Cursor/          TextCursor, MultiCursor, WordBoundary
├── Decorations/     DecorationTree  — interval tree for highlights & squiggles
├── Diff/            Myers O(ND) diff  — line-level + char-level
├── EOL/             EolRegistry, EolStyle  — LF / CRLF / CR detection & conversion
├── Encoding/        EncodingDetector, BomWriter  — UTF-8/16/32 + heuristics
├── Folding/         FoldingModel, BraceFoldingStrategy
├── Formatting/      IDocumentFormatter
├── InlayHints/      InlayHintModel
├── Language/        Tokenisers, BracketMatcher, AutoIndent, IndentGuideProvider,
│                    BracketPairColorizer, LineCommentToggle, DocumentCleanup,
│                    GraphemeHelper
├── Navigation/      CursorHistory, BookmarkModel
├── Outline/         OutlineProvider, OutlineNode
├── ReadOnly/        ReadOnlyRegionModel, ReadOnlyViolationException
├── Scripting/       ScriptRunner (line-oriented macro language)
├── Search/          TextSearcher  — Boyer-Moore-Horspool + regex
├── Snippets/        SnippetEngine, SnippetSession
├── StickyScroll/    StickyScroll
├── WordWrap/        WordWrapModel
└── TextDocument.cs  ← single entry point; wires everything together

TextEditor.Repl
├── CSharpScriptHost    Roslyn-backed stateful C# REPL
├── ScriptGlobals       doc / mc / Print() injected into every script
├── PluginRegistry      indexes + executes .csx plugin scripts
└── plugins/            sample .csx plugin files
```

`TextDocument` is the only class consumers need to `new` up. All sub-systems are
created lazily on first access via `GetXxxModel()` factory methods.

---

## Feature Reference

### 1. Core Buffer — Piece Table

Internally the document is stored as a **piece table backed by a red-black tree**.
All mutations are O(log n); `GetLine` materialises a single line without touching
the rest of the buffer.

```csharp
var doc = new TextDocument();
doc.Load("line 0\nline 1\nline 2");

doc.Insert(7, "hello ");
doc.Delete(7, 6);
doc.Replace(0, 6, "LINE 0");

string line  = doc.GetLine(0);
string text  = doc.GetText();
int    len   = doc.Length;
int    lines = doc.LineCount;

// Coordinate conversion
var (line, col) = doc.OffsetToPosition(42);
int offset      = doc.PositionToOffset(3, 5);
```

### 2. Cursor & Selection

```csharp
using TextEditor.Core.Cursor;

var cursor = new TextCursor(doc, 0);

cursor.MoveRight();
cursor.MoveWordRight();
cursor.MoveToLineEnd();
cursor.MoveToDocumentEnd();

cursor.SelectAll();
cursor.SelectLine();
cursor.SelectWordAtCaret();

bool hasSelection = cursor.HasSelection;
int  start        = cursor.SelectionStart;
int  end          = cursor.SelectionEnd;
int  caret        = cursor.CaretOffset;
```

### 3. Multi-Cursor

All edit operations across N cursors execute as **one undo unit**.

```csharp
var mc = new MultiCursor(doc);
mc.AddCursor(10);
mc.AddCursor(50);

mc.InsertText("→ ");      // inserts at every cursor
mc.DeleteLeft();
mc.DeleteWordRight();

// Box / column selection
mc.AddColumnSelection(startLine: 0, endLine: 4, column: 0);
mc.InsertText("// ");

// Paste — distributed (N lines → N cursors) or broadcast (1 line → all)
mc.Paste(new[] { "alpha", "beta", "gamma" });
```

### 4. Undo / Redo

```csharp
doc.Insert(0, "a");   // single grapheme → enters coalesce group
doc.Insert(1, "b");   // coalesced with previous
doc.Insert(2, "c");
doc.Undo();           // reverts "abc" as one unit (smart grouping)

doc.Redo();

bool canUndo = doc.CanUndo;
bool canRedo = doc.CanRedo;

// Break the current coalesce group (call before cursor jumps)
doc.FlushUndoGroup();

IEnumerable<string> history = doc.UndoDescriptions;
```

### 5. Search & Replace

```csharp
using TextEditor.Core.Search;

// Literal search (Boyer-Moore-Horspool, O(n), piece-streaming)
var opts = new SearchOptions { CaseSensitive = false, WholeWord = true };
foreach (var m in doc.FindAll("TODO", opts))
    Console.WriteLine($"offset {m.Offset}, length {m.Length}");

doc.FindFirst("pattern");
doc.FindNext("pattern", fromOffset: 20);
doc.FindPrev("pattern", beforeOffset: 80);
doc.CountMatches("pattern");

// Regex search
var rxOpts = new SearchOptions { UseRegex = true };
doc.FindAll(@"\bclass\s+\w+", rxOpts);

// ReplaceAll — O(n) single-pass (not O(n log n))
int n = doc.ReplaceAll("foo", "bar");

// Regex replace with capture groups
doc.ReplaceAll(@"(\w+) (\w+)", "$2, $1",
    new SearchOptions { UseRegex = true });
```

### 6. Syntax Highlighting

Incremental per-line tokenisation — only dirty lines are re-tokenised.

```csharp
using TextEditor.Core.Language;

// Built-in C# tokeniser
doc.SetTokeniser(new CSharpTokeniser());

IReadOnlyList<SyntaxToken> tokens = doc.GetSyntaxTokens(lineIndex: 0);
foreach (var t in tokens)
    Console.WriteLine($"[{t.Start}–{t.End}] {t.Type}");

// Warm up a viewport range (propagates state for future incremental calls)
doc.TokeniseLines(startLine: 0, endLine: 50);

// Invalidate manually after non-incremental changes
doc.InvalidateHighlightCache(fromLine: 10);
```

### 7. Diff Engine

```csharp
using TextEditor.Core.Diff;

// Line-level Myers O(ND) diff
DiffResult diff = TextDiff.Diff("old text", "new text");
Console.WriteLine(diff.ToUnifiedDiff());

// Char-level inline diff (for highlighting changed words)
var charDiff = DiffChars.Diff("hello world", "hello earth");
```

### 8. Bracket Matching & Auto-Indent

Brackets inside string and comment tokens are correctly skipped.

```csharp
int matchOffset = doc.FindMatchingBracket(caretOffset);   // -1 if unmatched

// Auto-indent on Enter
string indent = doc.GetAutoIndent(caretOffset);           // copies current indent
                                                          // +1 level after {

// De-indent when } is typed
string? closingIndent = doc.GetClosingBraceIndent(caretOffset);
```

### 9. Code Folding

```csharp
using TextEditor.Core.Folding;

var folding = doc.GetFoldingModel();
folding.UpdateRegions(new BraceFoldingStrategy());

foreach (var region in folding.Regions)
    Console.WriteLine($"  [{region.StartLine}–{region.EndLine}] {region.Label}");

folding.SetFolded(region, true);

// Display ↔ document line mapping
int displayRow = folding.ToDisplayRow(docLine: 5);
int docLine    = folding.ToDocumentLine(displayRow: 3);

// Events
folding.RegionsChanged    += (s, e) => { };
folding.FoldStateChanged  += (s, e) => { };
```

### 10. Encoding Detection & BOM Handling

```csharp
await doc.LoadFileAsync("source.cs");

var enc = doc.DetectedEncoding;     // DetectedEncoding record
Console.WriteLine(enc?.Encoding);   // e.g. UTF-8
Console.WriteLine(enc?.Confidence); // High / Medium / Low
Console.WriteLine(doc.HasBom);

// Override encoding on next save
doc.SaveEncoding = System.Text.Encoding.UTF8;
await doc.SaveFileAsync();
```

### 11. Unicode / Grapheme Clusters

All cursor movement, word operations and delete are **grapheme-cluster-aware** —
a family emoji 👨‍👩‍👧‍👦 moves the caret by 1, not 11.

```csharp
DocumentStats stats = doc.GetStats();
Console.WriteLine(stats.GraphemeCount);    // user-perceived characters
Console.WriteLine(stats.CodeUnitCount);    // UTF-16 code units (doc.Length)
Console.WriteLine(stats.RuneCount);        // Unicode code points
Console.WriteLine(stats.WordCount);
Console.WriteLine(stats.DisplayColumns);   // East Asian Width-aware
```

### 12. Word Wrap Layout

```csharp
var wrap = doc.GetWordWrapModel(viewportWidth: 80);

int displayRows   = wrap.DisplayRowCount;
bool isWrapped    = wrap.IsWrapped(docLine: 3);
int  firstDisplay = wrap.ToDisplayRow(docLine: 3);
int  docLine      = wrap.ToDocumentLine(displayRow: 7);

var segments = wrap.GetWrappedSegments(docLine: 3);

wrap.Resize(newViewportWidth: 120);
```

### 13. Inlay Hints

```csharp
using TextEditor.Core.InlayHints;

var hints = doc.GetInlayHintModel();
Guid id = hints.AddHint(offset: 42, text: "count:", kind: InlayHintKind.Parameter);
hints.RemoveHint(id);

var visible = hints.GetHintsInRange(start: 0, end: 200);

// Offsets remap automatically as the document is edited
hints.HintsChanged += (s, e) => { };
```

### 14. Snippet Engine

```csharp
using TextEditor.Core.Snippets;

// Syntax: $1 $2 … $0 (exit), ${1:placeholder}, $TM_FILENAME, $CLIPBOARD
Snippet snippet = SnippetEngine.Parse("for (int $1 = 0; $1 < $2; $1++)\n{\n\t$0\n}");

SnippetSession session = doc.BeginSnippet(snippet, insertOffset: caretOffset);
session.NextTabStop();   // move to $2
session.PrevTabStop();
session.Commit();        // finalise
// session.Cancel();     // remove inserted text
```

### 15. Bracket Pair Colorization

Color index cycles 0 → 1 → 2 with nesting depth. `-1` = unmatched.

```csharp
var pairs = doc.GetBracketPairs(startLine: 0, endLine: 50);
foreach (var p in pairs)
    Console.WriteLine($"open={p.OpenOffset} close={p.CloseOffset} color={p.ColorIndex}");
```

### 16. TextMate Grammar Tokeniser

Drop-in replacement for `CSharpTokeniser` — any VS Code `.tmLanguage.json` file works.

```csharp
doc.SetTokeniser(new TmLanguageTokeniser("grammars/csharp.tmLanguage.json"));
// GetSyntaxTokens() and the highlight cache continue to work unchanged
```

### 17. Indent Guide Computation

```csharp
var guides = doc.GetIndentGuides(startLine: 0, endLine: 40, tabWidth: 4);
foreach (var g in guides)
    Console.WriteLine($"col={g.Column} lines {g.StartLine}–{g.EndLine}");
```

### 18. Multi-line Paste

```csharp
// Distributed: 3 lines → 3 cursors (cursor[i] gets lines[i])
mc.AddColumnSelection(0, 2, 0);
mc.Paste(new[] { "alpha", "beta", "gamma" });

// Broadcast: any other count → joined text inserted at every cursor
mc.Paste(new[] { "one", "two" });   // "one\ntwo" at all cursors
```

### 19. Read-Only Regions

```csharp
var ro = doc.GetReadOnlyModel();

Guid id = ro.Protect(start: 0, end: 20);       // [0, 20) is immutable
ro.Unprotect(id);
ro.UnprotectAll();

bool blocked = ro.IsReadOnly(offset: 5);
bool rangeBlocked = ro.IsRangeReadOnly(start: 0, length: 10);

// Throw on violation (default) or silently ignore
doc.EnforceReadOnly = false;

ro.RegionsChanged += (s, e) => { };
```

### 20. Sticky Scroll Context

Returns the enclosing scope headers visible above the current viewport — identical
to VS Code's sticky scroll.

```csharp
using TextEditor.Core.StickyScroll;

var folding = doc.GetFoldingModel();
folding.UpdateRegions(new BraceFoldingStrategy());

var context = StickyScroll.GetContext(folding, firstVisibleLine: 42);
foreach (var entry in context)               // outermost first
    Console.WriteLine($"  {entry.DocumentLine}: {entry.Label}");
```

### 21. Document Outline

```csharp
using TextEditor.Core.Outline;

var folding = doc.GetFoldingModel();
folding.UpdateRegions(new BraceFoldingStrategy());

IReadOnlyList<OutlineNode> roots = OutlineProvider.GetOutline(folding);

void Print(OutlineNode n, int indent = 0)
{
    Console.WriteLine($"{new string(' ', indent * 2)}{n.Label} [{n.StartLine}–{n.EndLine}]");
    foreach (var child in n.Children) Print(child, indent + 1);
}
foreach (var root in roots) Print(root);
```

### 22. Cursor Position History

Back/Forward navigation like IDE Ctrl+Alt+← / →.

```csharp
var history = doc.GetCursorHistory();

history.Push(offset: 0,  filePath: "Program.cs");
history.Push(offset: 200);

var prev = history.Back();      // HistoryEntry? (Offset, FilePath)
var next = history.Forward();

bool canBack    = history.CanGoBack;
bool canForward = history.CanGoForward;
```

### 23. Bookmarks

```csharp
var bm = doc.GetBookmarkModel();

bool added = bm.Toggle(lineIndex: 3);   // true = bookmarked, false = removed
bool isSet  = bm.IsBookmarked(3);

int? next = bm.NextBookmark(fromLine: 3);
int? prev = bm.PrevBookmark(fromLine: 3);

IReadOnlyList<int> all = bm.GetAll();   // ascending order

// Bookmarks remap automatically on insert/delete and clear on Load()
bm.BookmarksChanged += () => { };
```

### 24. Document Formatting

```csharp
using TextEditor.Core.Formatting;

public sealed class PrettierFormatter : IDocumentFormatter
{
    public string Format(string text) => /* call your formatter */ text;
}

doc.Format(new PrettierFormatter());                         // whole document
doc.Format(new PrettierFormatter(), startLine: 5, endLine: 20);  // range
// No-op when the formatter returns the same text — undo stack untouched
```

### 25. Line Comment Toggle

Matches VS Code Ctrl+/ semantics: if every non-empty line is already commented,
uncomment; otherwise comment. Single undo step.

```csharp
using TextEditor.Core.Language;

LineCommentToggle.Toggle(doc, startLine: 0, endLine: 4);         // default "//"
LineCommentToggle.Toggle(doc, startLine: 0, endLine: 4, prefix: "#");   // Python
LineCommentToggle.Toggle(doc, startLine: 0, endLine: 4, prefix: "--");  // SQL
```

### 26. Document Cleanup

```csharp
using TextEditor.Core.Language;

int linesModified = DocumentCleanup.TrimTrailingWhitespace(doc);
// Returns 0 and does NOT touch the undo stack when already clean

DocumentCleanup.NormalizeLineEndings(doc, "\n");    // LF
DocumentCleanup.NormalizeLineEndings(doc, "\r\n");  // CRLF
```

### 27. Column (Box) Selection

```csharp
// Place one cursor on every line at the given column
mc.AddColumnSelection(startLine: 0, endLine: 9, column: 4);
mc.InsertText("| ");

// Column clamped per-line — safe on lines shorter than column
mc.AddColumnSelection(0, 20, 80);
```

### 28. Change Tracking

Tracks which lines are **Added**, **Modified**, or **Deleted** relative to the
last load or save — the coloured gutter you see in VS Code.

```csharp
using TextEditor.Core.ChangeTracking;

var tracker = doc.GetChangeTracker();
tracker.SetBaseline();                   // snapshot current state

doc.Insert(0, "new content\n");

LineStatus status = tracker.GetStatus(lineIndex: 0);   // Added / Modified / Clean
// Deletion-above markers also available

// Baseline resets automatically on doc.Load() and doc.SaveAsync()
tracker.ChangesUpdated += (s, e) => { };
```

### 29. Scripting / Macro Runner

A simple line-oriented macro language — useful for headless batch processing.

```csharp
using TextEditor.Core.Scripting;

var runner = new ScriptRunner(doc);
runner.Run("""
    LOAD The quick brown fox
    GOTO 0 4
    INSERT very
    FIND fox
    REPLACE_ALL fox cat
    UNDO
    REDO
""");
```

**Commands:** `LOAD`, `INSERT`, `INSERT_AT`, `DELETE`, `DELETE_AT`, `DELETE_LINE`,
`GOTO`, `MOVE`, `SELECT`, `FIND`, `FIND_PREV`, `FIND_ALL`, `REPLACE_ALL`,
`UNDO`, `REDO`, `NOP`.

---

## C# REPL

`TextEditor.Repl` provides a Roslyn-backed, stateful C# scripting host.
Variables declared in one submission are available in the next.

```csharp
using TextEditor.Repl;

var host = new CSharpScriptHost(doc, mc);

var r1 = await host.ExecuteAsync("var n = doc.LineCount;");
var r2 = await host.ExecuteAsync("Print(n);");   // n is still in scope

host.Reset();   // clears variables, keeps doc + mc references
```

**Globals available in every submission:**

| Name | Type | Description |
|---|---|---|
| `doc` | `TextDocument` | The active document |
| `mc` | `MultiCursor` | Multi-cursor for the document |
| `Print(x)` / `print(x)` | `void` | Write to the output buffer |

### Interactive REPL demo

```bash
dotnet run --project src/TextEditor.ReplDemo
```

Launches an 11-step guided tour then drops into an interactive `>` prompt.
Pass `--no-tour` to skip straight to the prompt.

**Built-in commands:** `.help` `.reset` `.doc` `.tour` `.exit`

---

## Plugin System

Drop a `.csx` file anywhere with a `// @plugin … // @end` frontmatter block.
The `PluginRegistry` indexes files without executing them; each run gets a fresh
isolated `CSharpScriptHost` — no state leaks between plugins or into the REPL.

### Plugin file format

```csharp
// @plugin
// Name: Sort Lines
// Description: Sorts all lines alphabetically (case-insensitive).
// Tags: sort, lines, utility
// @end

var lines  = Enumerable.Range(0, doc.LineCount).Select(i => doc.GetLine(i));
var sorted = lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase);
doc.Load(string.Join("\n", sorted));
Print($"Sorted {doc.LineCount} lines.");
```

### Using the registry

```csharp
using TextEditor.Repl;

var reg = new PluginRegistry();
reg.ScanDirectory("plugins/");          // load all *.csx in a folder
reg.Register("my-plugin.csx");          // or register one file

var results = reg.Search("sort");       // search name / description / tags
var plugin  = results[0];
Console.WriteLine(plugin.Name);
Console.WriteLine(plugin.Description);
Console.WriteLine(string.Join(", ", plugin.Tags));

var result = await reg.ExecuteAsync(plugin, doc, mc);
if (!result.Success)
    Console.Error.WriteLine(result.ErrorMessage);
else
    Console.WriteLine(result.DisplayText);
```

### Bundled sample plugins

| File | What it does |
|---|---|
| `plugins/sort-lines.csx` | Sorts all lines alphabetically (case-insensitive) |
| `plugins/doc-stats.csx` | Prints line/word/grapheme counts, longest line, and top-5 word frequency |

---

## Performance

The library is tuned for the common editor workload on documents up to **100 MB**.

| Operation | Algorithm | Complexity |
|---|---|---|
| Insert / Delete | Piece table + RB tree | O(log n) |
| GetLine | Single-piece materialise | O(line length) |
| FindAll (literal) | Boyer-Moore-Horspool, piece-streaming | O(n) |
| ReplaceAll | Single-pass rewrite, one tree reset | O(n) |
| Myers Diff | O(ND) | O(n + d²) |
| Syntax tokenise | Incremental per-line cache | O(dirty lines) |
| Undo / Redo | Command stack | O(1) |

---

## Building & Testing

**Prerequisites:** .NET 8 SDK

```bash
# Build everything
dotnet build TextEditorApi.sln

# Run all 1 809 tests
dotnet test src/TextEditor.Tests/TextEditor.Tests.csproj

# Run a specific feature's tests
dotnet test --filter "BookmarkTests"

# Run a demo
dotnet run --project src/TextEditor.UtilityDemo
dotnet run --project src/TextEditor.ReplDemo
dotnet run --project src/TextEditor.ReplDemo -- --no-tour
```

---

## Project Structure

```
TextEditorApi.sln
├── src/
│   ├── TextEditor.Core/            Main library (no dependencies)
│   ├── TextEditor.Repl/            Roslyn C# scripting host + plugin registry
│   ├── TextEditor.Tests/           1 809 xUnit tests
│   │
│   ├── TextEditor.ReplDemo/        Interactive C# REPL with feature tour
│   ├── TextEditor.UtilityDemo/     GoTo, bookmarks, formatting, comments, cleanup, column select
│   ├── TextEditor.SyntaxDemo/      Incremental syntax highlighting
│   ├── TextEditor.DiffDemo/        Myers diff + inline char diff
│   ├── TextEditor.BracketDemo/     Bracket matching + auto-indent
│   ├── TextEditor.FoldingDemo/     Code folding + display/doc line mapping
│   ├── TextEditor.EncodingDemo/    Encoding detection + BOM round-trip
│   ├── TextEditor.UnicodeDemo/     Grapheme cluster editing scenarios
│   ├── TextEditor.UndoGroupingDemo Smart undo coalescing comparison
│   ├── TextEditor.ChangeTrackingDemo Dirty-line gutter markers
│   ├── TextEditor.WrappingDemo/    Word wrap layout with line numbers
│   ├── TextEditor.InlayHintsDemo/  Parameter name hints + offset remapping
│   ├── TextEditor.SnippetDemo/     Tab-stop snippet expansion
│   ├── TextEditor.BracketColorDemo Nesting-depth bracket colourisation
│   ├── TextEditor.TmLanguageDemo/  TextMate grammar tokeniser
│   ├── TextEditor.IndentGuideDemo/ Vertical indent guide rendering
│   ├── TextEditor.MultiPasteDemo/  Distributed vs broadcast paste
│   ├── TextEditor.ReadOnlyDemo/    Protected regions + violation handling
│   ├── TextEditor.StickyScrollDemo Sticky scope headers
│   ├── TextEditor.OutlineDemo/     Document outline tree
│   ├── TextEditor.CursorHistoryDemo Back/Forward navigation
│   └── TextEditor.PerfViewer/      Throughput benchmarks
└── TODO.md
```

---

## License

MIT
