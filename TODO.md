Outstanding features — priority order

Items 1–9 are COMPLETE.

────────────────────────────────────────────────────────────────────────
COMPLETED
────────────────────────────────────────────────────────────────────────

1  ✅  Cursor + selection model
       TextCursor with anchor/active, MoveWordLeft/Right, SelectAll, SelectLine.

2  ✅  Word boundary operations
       WordBoundary static class, GetWordAt/Left/Right, WordSpan record struct.

3  ✅  Multi-cursor + multi-selection
       MultiCursor managing N TextCursors, all edits as one undo unit.

4  ✅  Scripting shell / macro runner
       ScriptRunner/ScriptParser/ScriptCommand. Commands: MOVE GOTO INSERT
       INSERT_AT DELETE DELETE_AT DELETE_LINE SELECT REPLACE_ALL FIND
       FIND_PREV FIND_ALL UNDO REDO NOP.

5  ✅  Incremental syntax highlighting
       LineHighlightCache with per-line state tracking.
       CSharpTokeniser implements IStatefulSyntaxTokeniser.
       SyntaxDemo CLI app.

6  ✅  Diff engine
       Myers O(ND) algorithm; line-level DiffResult with ToUnifiedDiff;
       DiffChars for inline highlighting. DiffDemo CLI app.

7  ✅  Bracket matching + auto-indent
       BracketMatcher (forward/backward scan skipping string/comment tokens);
       AutoIndent (copy-indent + +1 level after {, closing-brace de-indent).
       BracketDemo CLI app.

8  ✅  Code folding model
       FoldRegion/FoldingModel/IFoldingStrategy/BraceFoldingStrategy.
       Display-line ↔ doc-line mapping with nested-fold overlap fix.
       Auto-update via OnInsert/OnDelete; IsStale/Invalidate for destructive
       ops; RegionsChanged/FoldStateChanged events. FoldingDemo CLI app.

9  ✅  Encoding detection + BOM handling
       EncodingDetector: BOM table (UTF-8/16/32 LE/BE) + heuristic path
       (valid UTF-8, Windows-1252, Latin-1 fallback).
       BomWriter. DetectedEncoding record + EncodingConfidence enum.
       Wired into PieceTable.LoadAsync/SaveAsync.
       TextDocument.DetectedEncoding / HasBom / SaveEncoding.
       EncodingDemo CLI app.

────────────────────────────────────────────────────────────────────────
REMAINING — ordered most-impactful to least
────────────────────────────────────────────────────────────────────────

10  Smart undo grouping / coalescing                           (~100 lines)
    Sequential same-direction character inserts (and deletes) are batched
    into a single undo unit, flushed on cursor jump, paste, or idle timeout.
    The difference between "undo a word" and "undo a letter."
    Touches CommandHistory only.
    Tests: coalesce insert run, coalesce delete run, flush on paste,
           flush on cursor jump, max-group-size limit, undo/redo symmetry.
    Demo: UndoGroupingDemo — type sentence, undo word-by-word, compare
          with grouping off.

11  Change tracking / dirty-line markers                       (~180 lines)
    ChangeTracker records which lines are Added / Modified / Deleted
    relative to a saved baseline (captured at Load/Save).
    Incrementally updated on every Insert/Delete (same hook pattern as
    FoldingModel). Uses the existing Myers diff engine to compute the
    initial per-line delta from the baseline string.
    TextDocument.GetChangeTracker() lazy factory.
    Events: ChangesUpdated.
    Tests: fresh load all-clean, single-line insert marks Added,
           edit existing line marks Modified, delete marks Deleted,
           save clears all marks, round-trip after undo restores marks.
    Demo: ChangeTrackingDemo — loads sample, makes edits, prints gutter
          with ✚ / ~ / ✖ per line, saves and shows all-clean.

12  Word wrap layout model                                     (~220 lines)
    WordWrapModel computes how many display rows each document line
    occupies given a viewport width (columns). Maps document lines ↔
    wrapped display rows. Same structural pattern as FoldingModel.
    TextDocument.GetWordWrapModel(int viewportWidth).
    Methods: ToDisplayRows(docLine), ToDocumentLine(displayRow),
             DisplayRowCount, IsWrapped(docLine), GetWrappedSegments(docLine).
    Invalidated on every Insert/Delete; no incremental remap (recompute
    is O(changed lines), not O(n)).
    Tests: no-wrap passthrough, single long line wraps to N rows,
           display↔doc round-trip, insert inside wrapped line updates
           row count, viewport resize recomputes.
    Demo: WordWrapDemo — renders a long prose document in a narrow
          (40-col) viewport, shows line numbers vs display rows.

13  Inlay hints model                                          (~150 lines)
    InlayHintModel stores InlayHint(offset, text, kind) annotations
    displayed inline without modifying the document (parameter names,
    inferred types, return values).
    Remaps offsets on Insert/Delete; fires HintsChanged.
    TextDocument.GetInlayHintModel() lazy factory.
    Kinds: Parameter, Type, Return (extensible).
    Tests: add hint, insert before shifts offset, delete covering hint
           removes it, GetHintsInRange(), kind filter, event firing.
    Demo: InlayHintsDemo — loads a C# method call, adds synthetic
          parameter-name hints, inserts text and shows offsets shift.

14  Snippet engine                                             (~250 lines)
    SnippetEngine expands tab-stop bodies into a document insertion plus
    a live SnippetSession tracking tab-stop offsets.
    Syntax: $1 $2 … $0 (exit), ${1:placeholder}, $TM_FILENAME,
            $CLIPBOARD, mirror fields (same $n appears twice → typed in
            sync via MultiCursor).
    API: SnippetEngine.Parse(body) → Snippet;
         doc.BeginSnippet(Snippet, insertOffset) → SnippetSession;
         session.NextTabStop() / PrevTabStop() / Commit() / Cancel().
    Tests: parse simple snippet, tab-stop ordering, placeholder text,
           mirror field sync, variable substitution, nested placeholders,
           commit writes final text, cancel removes inserted text.
    Demo: SnippetDemo — expands a "for loop" snippet, shows tab-stop
          navigation.

15  Bracket pair colorization                                  (~80 lines)
    BracketPairColorizer walks the document using BracketMatcher logic
    and returns IReadOnlyList<BracketPair(OpenOffset, CloseOffset,
    ColorIndex)>. Color index cycles 0→1→2 with nesting depth.
    Unmatched brackets get ColorIndex = -1.
    API: TextDocument.GetBracketPairs(int startLine, int endLine).
    Tests: flat pairs all color-0, nested increments depth, triple-nested
           resets to 0, unmatched open = -1, string/comment brackets
           excluded.
    Demo: BracketColorDemo — renders source with [ ], { }, ( ) each
          tinted by depth using ANSI color codes.

16  TextMate grammar tokeniser                                 (~400 lines)
    TmLanguageTokeniser implements IStatefulSyntaxTokeniser by loading
    a .tmLanguage JSON file and evaluating its scope-stack rule engine.
    Replaces the hand-rolled CSharpTokeniser with a universal solution —
    any VS Code grammar file works automatically.
    API: new TmLanguageTokeniser(string jsonPath);
         doc.SetTokeniser(new TmLanguageTokeniser("csharp.tmLanguage.json")).
    Scope names are mapped to SyntaxToken.Type strings so the existing
    highlight cache and decoration tree need no changes.
    Tests: load minimal grammar JSON, tokenise known string, scope-stack
           push/pop, end pattern terminates scope, include rules,
           fallback to prior tokeniser on parse error.
    Demo: TmLanguageDemo — loads the bundled minimal JSON grammar for
          C# and Python, tokenises sample files, prints colored output.

17  Indent guide computation                                   (~90 lines)
    IndentGuideProvider.GetGuides(TextDocument, int startLine, int endLine)
    returns IReadOnlyList<IndentGuide(column, startLine, endLine)>.
    Uses leading-whitespace scan; collapses blank lines into surrounding
    guide spans. Respects tab-width parameter.
    Tests: single indent level, nested levels, blank line inside block,
           mixed tabs/spaces, empty document.
    Demo: integrated into WordWrapDemo or a standalone IndentGuideDemo
          that renders vertical │ characters at guide columns.

18  ✅  Multi-line paste across multi-cursors
    MultiCursor.Paste(IReadOnlyList<string> lines) — distributed when
    lines.Count == cursor count, broadcast otherwise. 16 tests, one
    undo step. MultiPasteDemo CLI app.

19  ✅  Read-only regions
    ReadOnlyRegionModel: Protect/Unprotect/UnprotectAll/IsReadOnly/
    IsRangeReadOnly/GetRegions. ReadOnlyViolationException with region
    info. EnforceReadOnly (throw vs silent). OnInsert/OnDelete remap.
    Load clears all protections. 40 tests, ReadOnlyDemo CLI app.

20  ✅  Sticky scroll context provider
    StickyScroll.GetContext(FoldingModel, firstVisibleLine) →
    IReadOnlyList<StickyScrollEntry(Label, DocumentLine)>.
    Regions where StartLine < first and EndLine >= first, sorted
    outermost-first. 20 tests, StickyScrollDemo CLI app.

21  ✅  Document outline
    OutlineProvider.GetOutline(FoldingModel) → IReadOnlyList<OutlineNode>.
    OutlineNode(Label, StartLine, EndLine, Depth, Children). Stack-based
    O(n) tree build from sorted regions. 19 tests, OutlineDemo CLI app.

22  ✅  Cursor position history (Back / Forward)
    CursorHistory ring buffer (cap 100): Push/Back/Forward/Clear.
    HistoryEntry(Offset, FilePath?). Truncates forward on new Push.
    Evicts oldest at capacity. TextDocument.GetCursorHistory() lazy
    factory; cleared on Load. 36 tests, CursorHistoryDemo CLI app.

── Utility API ──────────────────────────────────────────────────────────
✅  GoTo(line, col) on TextDocument — jumps and pushes CursorHistory; clamps
✅  BookmarkModel — Toggle/IsBookmarked/NextBookmark/PrevBookmark/GetAll;
    OnInsert/OnDelete remap; Load clears; integrated into TextDocument; 19 tests
✅  Regex capture group ReplaceAll — $1/$2 when UseRegex=true + '$' in replacement;
    Regex.Replace fast path; 8 tests
✅  IDocumentFormatter — pluggable Format(text) interface; TextDocument.Format(
    formatter, startLine?, endLine?); no-op when text unchanged; 7 tests
✅  LineCommentToggle — Toggle(doc, startLine, endLine, prefix="//"); single undo
    step; handles indentation, empty lines, custom prefix; 10 tests
✅  DocumentCleanup — TrimTrailingWhitespace (returns modified line count,
    single undo); NormalizeLineEndings (SaveEolStyle + strips stray \r); 10 tests
✅  Column selection — MultiCursor.AddColumnSelection(startLine, endLine, col);
    col clamped per line; replaces existing cursors; 8 tests
✅  Plugin system — PluginRegistry in TextEditor.Repl; .csx frontmatter
    (// @plugin / Name / Description / Tags / // @end); Search by name/tag/desc
    case-insensitive; Execute via isolated CSharpScriptHost; ScanDirectory; 11 tests
✅  TextEditor.UtilityDemo — CLI demo app showing all 7 utility features

// 23  LSP client + Diagnostics model                            (large)
//     DiagnosticsModel: stores Diagnostic(range, severity, message, code)
//     entries, remaps on OnInsert/OnDelete, fires DiagnosticsChanged.
//     TextDocument.GetDiagnosticsModel() lazy factory.
//     LspClient: separate process, async JSON-RPC 2.0 (Content-Length framing).
//     Lifecycle: initialize → initialized → shutdown/exit.
//     Core capabilities wired to TextDocument:
//       textDocument/didOpen, didChange, didClose (keeps server in sync),
//       textDocument/completion, hover, definition,
//       textDocument/publishDiagnostics → DiagnosticsModel.
//     API: LspClient.StartAsync(serverPath, args);
//          doc.AttachLspClient(client);
//          doc.RequestCompletionsAsync(offset),
//          doc.RequestHoverAsync(offset),
//          doc.RequestDefinitionAsync(offset).
//     Tests: add diagnostic, insert before/after shifts range, delete removes
//            covered diagnostics, severity filter, GetDiagnosticsInRange(),
//            event firing; JSON-RPC framing round-trip, initialize handshake
//            mock, didChange batching, completion response parsing,
//            diagnostics pushed into DiagnosticsModel.
//     Demo: LspDemo — starts omnisharp or clangd, opens a file, requests
//           completions at a known position, prints results; also shows
//           synthetic diagnostics shifting on edit.

────────────────────────────────────────────────────────────────────────
NOTES
────────────────────────────────────────────────────────────────────────

Where this API beats VS Code today:
  GetLine after many edits  25 ms vs ~300 ms (flat-buffer materialise once)
  ReplaceAll                73 ms O(n) single-pass vs O(n log n) sequential

Every new feature must:
  • Include full xUnit test coverage in TextEditor.Tests
  • Include a CLI demo app (or extend an existing one) in src/
  • Follow the existing lazy-factory pattern (GetXxxModel()) for models
  • Wire OnInsert/OnDelete into TextDocument for live remapping
  • Fire events the UI layer can bind to
