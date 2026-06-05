# TextDocument

The central document model. Backed by a piece table for O(log n) mutations with minimal allocation. Every edit is tracked for undo/redo.

**Navigation:** [Overview](index.md) | [API Reference](operations.md) | [Examples](examples-textdocument.md) | [Cursors](cursor.md)

## Construction


```csharp
// Default constructor — empty document, plain-text tokenizer
var doc = new TextDocument();

// With a custom syntax tokenizer
var doc = new TextDocument(myTokeniser);
```

## Loading Content

| Method | Description |
|---|---|
| `Load(string content, string? filePath = null)` | Load from a string. Resets undo stack and change tracker. Throws `ArgumentNullException` if content is null. |
| `async Task LoadFileAsync(string path, Encoding? enc = null)` | Load from a file. Auto-detects encoding (BOM, UTF-8, Latin-1) and EOL style (`LF`, `CRLF`, `CR`) unless overridden. |

```csharp
doc.Load("Hello\nWorld");
await doc.LoadFileAsync("/path/to/file.cs");
```

## Properties

| Property | Type | Description |
|---|---|---|
| `Length` | `int` | Total character count (not bytes). |
| `LineCount` | `int` | Number of lines. |
| `IsModified` | `bool` | True if the document has unsaved changes. |
| `FilePath` | `string?` | Path set during `LoadFileAsync` or passed to `Load`. |
| `LanguageId` | `string` | Language identifier from the active tokenizer (e.g. `"csharp"`). |
| `OriginalEolStyle` | `EolStyle` | EOL style detected on load. |
| `SaveEolStyle` | `EolStyle` | EOL style used when saving. Defaults to `OriginalEolStyle`. |
| `DetectedEncoding` | `DetectedEncoding?` | Encoding detected on load (null for string loads). |
| `HasBom` | `bool` | True if the loaded file had a BOM. |
| `SaveEncoding` | `Encoding?` | Override the save encoding. Null = preserve original. |
| `EnforceReadOnly` | `bool` | If true, mutations inside read-only regions throw `ReadOnlyViolationException`. Default: true. |

## Reading Text

| Method | Description |
|---|---|
| `GetText()` | Full document text (normalised LF). |
| `GetText(int offset, int length)` | Substring of the document. |
| `GetTextWithEol()` | Full text using the document's original/save EOL style. |
| `GetLine(int lineIndex)` | Text of line `lineIndex` (0-based), without the newline character. |

## Position Conversion

```csharp
// offset → (line, column) — both zero-based
var (line, col) = doc.OffsetToPosition(42);

// (line, column) → offset
int offset = doc.PositionToOffset(2, 5);
```

## Mutations

All mutations are recorded for undo/redo. Single-grapheme edits are coalesced into groups automatically (like a keyboard-driven editor). Multi-character programmatic edits create their own undo step.

| Method | Description |
|---|---|
| `Insert(int offset, string text)` | Insert text at a byte offset. Throws `ArgumentNullException` if text is null. |
| `Delete(int offset, int length)` | Delete `length` characters starting at `offset`. |
| `Replace(int offset, int deleteLength, string insertText)` | Delete then insert in a single undo unit. Throws `ArgumentNullException` if insertText is null. |
| `ReplaceAll(string pattern, string replacement, SearchOptions? opts)` | Global find-and-replace. Returns replacement count. O(n) via Boyer-Moore-Horspool. |
| `ExecuteComposite(string description, IEnumerable<IEditorCommand> commands)` | Run multiple editor commands as a single named undo unit. |

## Undo / Redo

```csharp
if (doc.CanUndo) doc.Undo();
if (doc.CanRedo) doc.Redo();

// Inspect available undo/redo steps
foreach (var desc in doc.UndoDescriptions)
	Console.WriteLine(desc);

doc.FlushUndoGroup(); // close the current coalescing group
```

See [Undo / Redo](undo-redo.md) for full details.

## Search

```csharp
var opts = new SearchOptions { CaseSensitive = false, UseRegex = true };

// Find all matches
foreach (var m in doc.FindAll(@"\bTODO\b", opts))
	Console.WriteLine($"  at offset {m.Offset}, length {m.Length}");

// Find next / prev from a position
var next = doc.FindNext("hello", fromOffset: 0);
var prev = doc.FindPrev("hello", beforeOffset: doc.Length);

// Count without returning matches
int count = doc.CountMatches("TODO");
```

See [Search](search.md) for the full API.

## Saving

```csharp
// Save to stream (preserves detected encoding + EOL by default)
await doc.SaveAsync(stream);

// Save to file path (uses FilePath if path is null)
await doc.SaveFileAsync("/output/file.txt");

// Override encoding for this save only
doc.SaveEncoding = System.Text.Encoding.UTF8;
await doc.SaveFileAsync();
```

## Decorations

```csharp
using TextAPI.Core.Decorations;

Guid id = doc.AddDecoration(start: 0, end: 5, DecorationType.ErrorSquiggle, tag: "CS0101");
doc.RemoveDecoration(id);

foreach (var d in doc.GetDecorationsInRange(0, 100))
	Console.WriteLine($"{d.Type} [{d.Start},{d.End})");
```

See [Decorations](decorations.md).

## Statistics

```csharp
var stats = doc.GetStats();
// stats.CharCount, WordCount, LineCount, ParagraphCount, ...
```

## Performance Notes

- **Piece table**: insert/delete/get-text are all O(log n) in piece count, not document length.
- **ReplaceAll**: uses Boyer-Moore-Horspool with a single pass and a bulk replace command — O(n) regardless of match count.
- **Memory**: a 100 MB file uses ~210 MB steady-state (one UTF-16 char array, no extra copies).
- Call `doc.Compact()` after heavy edits to consolidate pieces and reduce tree depth.
