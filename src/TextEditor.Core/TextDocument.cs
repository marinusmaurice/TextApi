using TextEditor.Core.Buffer;
using TextEditor.Core.Commands;
using TextEditor.Core.Decorations;
using TextEditor.Core.EOL;
using TextEditor.Core.Language;

namespace TextEditor.Core;

/// <summary>
/// The top-level editor document API.
///
/// This is the single entry point consumers use. It wires together:
///   • PieceTable       — the text buffer (piece table + RB tree)
///   • CommandHistory   — undo/redo stack (all mutations go through here)
///   • DecorationTree   — syntax highlights, squiggles, selections
///   • ISyntaxTokeniser — pluggable tokeniser (regex or Tree-sitter)
///
/// Usage pattern:
///   var doc = new TextDocument();
///   doc.Load("Hello\nWorld");
///   doc.Insert(5, " Beautiful");
///   doc.Undo();
///   string line0 = doc.GetLine(0);
/// </summary>
public sealed class TextDocument
{
    private readonly PieceTable      _buffer;
    private readonly CommandHistory  _history;

    /// <summary>Direct buffer access for multi-cursor command batching (same assembly only).</summary>
    internal PieceTable InternalBuffer => _buffer;
    private readonly DecorationTree  _decorations;
    private          ISyntaxTokeniser _tokeniser;
    private          LineHighlightCache _highlightCache;

    public TextDocument(
        ISyntaxTokeniser? tokeniser           = null,
        int               undoHistoryLimit    = 1000,
        int               lineCacheCapacity   = 512,
        int               compactionThreshold = 5000)
    {
        _buffer      = new PieceTable(lineCacheCapacity, compactionThreshold);
        _history     = new CommandHistory(undoHistoryLimit);
        _decorations = new DecorationTree();
        _tokeniser   = tokeniser ?? new NullTokeniser();

        // _highlightCache is initialised after _buffer so LineCount is available.
        _highlightCache = new LineHighlightCache(this, _tokeniser);

        // When auto-compaction fires, set a flag so we clear the history
        // AFTER the current command finishes executing (not mid-Execute).
        _buffer.AutoCompacted += () => _pendingHistoryClear = true;
    }

    private bool _pendingHistoryClear;

    private void PostEditHook()
    {
        if (_pendingHistoryClear)
        {
            _pendingHistoryClear = false;
            _history.Clear();
            _decorations.Clear();
        }
    }

    // ── Identity ──────────────────────────────────────────────────────────

    public string? FilePath { get; private set; }
    public string  LanguageId => _tokeniser.LanguageId;
    public bool    IsModified { get; private set; }

    // ── Load ──────────────────────────────────────────────────────────────

    /// <summary>Load text content directly.</summary>
    public void Load(string content, string? filePath = null)
    {
        _buffer.Load(content);
        _decorations.Clear();
        _history.Clear();
        _highlightCache.InvalidateAll();
        _foldingModel?.Invalidate();
        _changeTracker?.SetBaseline();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        _readOnlyModel?.UnprotectAll();
        _cursorHistory?.Clear();
        FilePath   = filePath;
        IsModified = false;
    }

    /// <summary>Load from a file path.</summary>
    public async Task LoadFileAsync(string path, System.Text.Encoding? encoding = null)
    {
        await using var stream = File.OpenRead(path);
        await _buffer.LoadAsync(stream, encoding);
        _decorations.Clear();
        _history.Clear();
        _highlightCache.InvalidateAll();
        _foldingModel?.Invalidate();
        _changeTracker?.SetBaseline();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        _readOnlyModel?.UnprotectAll();
        _cursorHistory?.Clear();
        FilePath   = path;
        IsModified = false;
    }

    // ── Document stats ────────────────────────────────────────────────────

    public int Length    => _buffer.Length;
    public int LineCount => _buffer.LineCount;
    public EolStyle OriginalEolStyle => _buffer.OriginalEolStyle;

    /// <summary>
    /// Returns grapheme-aware statistics for the current document content.
    ///
    /// <list type="bullet">
    ///   <item><see cref="DocumentStats.GraphemeCount"/> — user-perceived character count
    ///   (Unicode grapheme clusters; an emoji like 👨‍👩‍👧‍👦 counts as 1).</item>
    ///   <item><see cref="DocumentStats.CodeUnitCount"/> — <c>doc.Length</c> (UTF-16 code units).</item>
    ///   <item><see cref="DocumentStats.RuneCount"/> — Unicode code points.</item>
    ///   <item><see cref="DocumentStats.WordCount"/> — whitespace-delimited words.</item>
    ///   <item><see cref="DocumentStats.LineCount"/> — line count.</item>
    ///   <item><see cref="DocumentStats.DisplayColumns"/> — East Asian Width–aware total columns.</item>
    /// </list>
    /// </summary>
    public DocumentStats GetStats()
    {
        string text = _buffer.GetText();
        var span    = text.AsSpan();

        int graphemes = Language.GraphemeHelper.ClusterCount(span);
        int codeUnits = text.Length;

        int runes = 0;
        foreach (var _ in text.EnumerateRunes()) runes++;

        int words   = CountWords(span);
        int lines   = _buffer.LineCount;
        int dispCols = Language.GraphemeHelper.TotalDisplayWidth(span);

        return new DocumentStats(graphemes, codeUnits, runes, words, lines, dispCols);
    }

    /// <summary>
    /// Flush any pending coalesced undo group to the undo stack.
    /// Call this before cursor navigation so that movement breaks the typing group,
    /// matching VS Code behaviour (Left arrow ends the current undo unit).
    /// </summary>
    public void FlushUndoGroup() => _history.FlushGroup();

    private static int CountWords(ReadOnlySpan<char> text)
    {
        bool inWord = false;
        int  count  = 0;
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                if (!inWord) { count++; inWord = true; }
            }
            else
            {
                inWord = false;
            }
        }
        return count;
    }

    public EolStyle SaveEolStyle
    {
        get => _buffer.SaveEolStyle;
        set => _buffer.SaveEolStyle = value;
    }

    // ── Encoding ──────────────────────────────────────────────────────────

    /// <summary>
    /// The encoding detected (or defaulted) when the file was last loaded
    /// via <see cref="LoadFileAsync"/>.
    /// <see langword="null"/> when the document was loaded from a string.
    /// </summary>
    public Encoding.DetectedEncoding? DetectedEncoding => _buffer.DetectedEncoding;

    /// <summary>
    /// <see langword="true"/> when the file that was loaded had a byte-order mark.
    /// </summary>
    public bool HasBom => _buffer.DetectedEncoding?.HasBom ?? false;

    /// <summary>
    /// Override the encoding used the next time the document is saved.
    /// <see langword="null"/> (the default) means use the detected encoding,
    /// preserving the original file's encoding and BOM.
    /// </summary>
    public System.Text.Encoding? SaveEncoding { get; set; }

    // ── Edit operations (all via command pattern → undo/redo) ─────────────

    /// <summary>Insert text at a zero-based character offset.</summary>
    public void Insert(int offset, string text)
    {
        if (_readOnlyModel != null)
        {
            var (blocked, rs, re) = _readOnlyModel.WouldBlockInsertInfo(offset);
            if (blocked)
            {
                if (EnforceReadOnly) throw new ReadOnly.ReadOnlyViolationException(rs, re, offset);
                return;
            }
        }

        var cmd = new InsertCommand(_buffer, offset, text);
        // Single grapheme cluster = user typed one character → coalesce into group.
        // Multi-cluster = paste or programmatic insert → own undo unit.
        if (Language.GraphemeHelper.ClusterCount(text.AsSpan()) == 1)
            _history.ExecuteGrouped(cmd, GroupKind.Insert,
                insertOffset: offset, insertLength: text.Length);
        else
            _history.Execute(cmd);

        _decorations.OnInsert(offset, text.Length);
        _highlightCache.OnInsert(offset, text.Length);
        _foldingModel?.OnInsert(offset, text.Count(c => c == '\n'));
        _changeTracker?.Invalidate();
        _wordWrapModel?.OnInsert(offset, text.Count(c => c == '\n'));
        _inlayHintModel?.OnInsert(offset, text.Length);
        _readOnlyModel?.OnInsert(offset, text.Length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Delete characters in [offset, offset+length).</summary>
    public void Delete(int offset, int length)
    {
        if (_readOnlyModel != null)
        {
            var (blocked, rs, re) = _readOnlyModel.WouldBlockDelete(offset, length);
            if (blocked)
            {
                if (EnforceReadOnly) throw new ReadOnly.ReadOnlyViolationException(rs, re, offset);
                return;
            }
        }

        // Capture both newline count and cluster count before the delete executes.
        string deletedText    = _buffer.GetText(offset, length);
        int deletedNewlines   = deletedText.Count(c => c == '\n');
        bool singleCluster    = Language.GraphemeHelper.ClusterCount(deletedText.AsSpan()) == 1;

        var cmd = new DeleteCommand(_buffer, offset, length);
        if (singleCluster)
            _history.ExecuteGrouped(cmd, GroupKind.Delete,
                deleteOffset: offset, deleteLength: length);
        else
            _history.Execute(cmd);

        _decorations.OnDelete(offset, length);
        _highlightCache.OnDelete(offset, length);
        _foldingModel?.OnDelete(offset, deletedNewlines);
        _changeTracker?.Invalidate();
        _wordWrapModel?.OnDelete(offset, deletedNewlines);
        _inlayHintModel?.OnDelete(offset, length);
        _readOnlyModel?.OnDelete(offset, length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Replace characters in [offset, offset+deleteLength) with insertText.</summary>
    public void Replace(int offset, int deleteLength, string insertText)
    {
        // Optimise: zero-delete replace is just an insert (participates in undo grouping).
        if (deleteLength == 0) { Insert(offset, insertText); return; }
        // Optimise: zero-insert replace is just a delete (participates in undo grouping).
        if (insertText.Length == 0) { Delete(offset, deleteLength); return; }

        // True replace (selection clear + insert): always its own undo unit.
        if (_readOnlyModel != null)
        {
            var (blocked, rs, re) = _readOnlyModel.WouldBlockDelete(offset, deleteLength);
            if (blocked)
            {
                if (EnforceReadOnly) throw new ReadOnly.ReadOnlyViolationException(rs, re, offset);
                return;
            }
            // Also check that the replacement insertion point is not strictly inside a region.
            (blocked, rs, re) = _readOnlyModel.WouldBlockInsertInfo(offset);
            if (blocked)
            {
                if (EnforceReadOnly) throw new ReadOnly.ReadOnlyViolationException(rs, re, offset);
                return;
            }
        }

        int deletedNewlines = _foldingModel != null
            ? _buffer.GetText(offset, deleteLength).Count(c => c == '\n') : 0;
        var cmd = new ReplaceCommand(_buffer, offset, deleteLength, insertText);
        _history.Execute(cmd);
        _decorations.OnDelete(offset, deleteLength);
        _decorations.OnInsert(offset, insertText.Length);
        _highlightCache.OnDelete(offset, deleteLength);
        _highlightCache.OnInsert(offset, insertText.Length);
        _foldingModel?.OnDelete(offset, deletedNewlines);
        _foldingModel?.OnInsert(offset, insertText.Count(c => c == '\n'));
        _changeTracker?.Invalidate();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.OnDelete(offset, deleteLength);
        _inlayHintModel?.OnInsert(offset, insertText.Length);
        _readOnlyModel?.OnDelete(offset, deleteLength);
        _readOnlyModel?.OnInsert(offset, insertText.Length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Execute multiple edit commands as a single undoable unit.</summary>
    public void ExecuteComposite(string description, IEnumerable<IEditorCommand> commands)
    {
        var composite = new CompositeCommand(description, commands);
        _history.Execute(composite);
        _foldingModel?.Invalidate();
        _changeTracker?.Invalidate();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        IsModified = true;
        PostEditHook();
    }

    // ── Undo / Redo ───────────────────────────────────────────────────────

    public bool CanUndo => _history.CanUndo;
    public bool CanRedo => _history.CanRedo;

    public void Undo()
    {
        _history.Undo();
        _decorations.Clear();  // simplest safe approach; production = fine-grained decoration undo
        _highlightCache.InvalidateAll();
        _foldingModel?.Invalidate();
        _changeTracker?.Invalidate();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        IsModified = true;
    }

    public void Redo()
    {
        _history.Redo();
        _decorations.Clear();
        _highlightCache.InvalidateAll();
        _foldingModel?.Invalidate();
        _changeTracker?.Invalidate();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        IsModified = true;
    }

    public IEnumerable<string> UndoDescriptions => _history.UndoDescriptions;
    public IEnumerable<string> RedoDescriptions => _history.RedoDescriptions;

    // ── Read ──────────────────────────────────────────────────────────────

    /// <summary>Return the full document text (LF-normalised, internal form).</summary>
    public string GetText() => _buffer.GetText();

    /// <summary>Return the full document text with the saved EOL style restored.</summary>
    public string GetTextWithEol() => _buffer.GetTextWithEol();

    /// <summary>Return text in [offset, offset+length).</summary>
    public string GetText(int offset, int length) => _buffer.GetText(offset, length);

    /// <summary>Return the content of line <paramref name="lineIndex"/> (zero-based, no EOL char).</summary>
    public string GetLine(int lineIndex) => _buffer.GetLine(lineIndex);

    /// <summary>Convert a character offset to (line, column).</summary>
    public (int Line, int Column) OffsetToPosition(int offset) => _buffer.OffsetToPosition(offset);

    /// <summary>Convert (line, column) to a character offset.</summary>
    public int PositionToOffset(int line, int column) => _buffer.PositionToOffset(line, column);

    // ── Syntax / Tokenisation ─────────────────────────────────────────────

    /// <summary>
    /// Replace the tokeniser (e.g. switch from null to C# tokeniser after
    /// detecting language).  Fully invalidates the highlight cache.
    /// </summary>
    public void SetTokeniser(ISyntaxTokeniser tokeniser)
    {
        _tokeniser = tokeniser;
        _highlightCache.SetTokeniser(tokeniser);
    }

    /// <summary>
    /// Get syntax tokens for a single line, using the incremental cache.
    /// The line is re-tokenised only when its content or incoming state has
    /// changed since the last call.  O(1) for cached lines.
    /// </summary>
    public IReadOnlyList<SyntaxToken> GetSyntaxTokens(int lineIndex)
        => _highlightCache.GetTokens(lineIndex);

    /// <summary>
    /// Tokenise a single line and return syntax tokens.
    /// Uses the incremental cache; equivalent to <see cref="GetSyntaxTokens"/>.
    /// </summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(int lineIndex)
        => _highlightCache.GetTokens(lineIndex);

    /// <summary>
    /// Tokenise a range of lines, propagate state past <paramref name="endLine"/>
    /// until it stabilises, then push results into the decoration tree as
    /// SyntaxHighlight decorations.  Previous syntax decorations are cleared first.
    /// </summary>
    public void TokeniseLines(int startLine, int endLine)
    {
        _decorations.RemoveAllOfType(DecorationType.SyntaxHighlight);
        // WarmUp propagates state beyond endLine for future incremental calls.
        _highlightCache.WarmUp(startLine, endLine);
        for (int i = startLine; i <= Math.Min(endLine, LineCount - 1); i++)
        {
            var tokens = _highlightCache.GetTokens(i);
            foreach (var t in tokens)
            {
                _decorations.AddDecoration(new Decoration
                {
                    Start = t.Start,
                    Type  = DecorationType.SyntaxHighlight,
                    Tag   = t.Type,
                    Data  = t
                }.SetEnd(t.End));
            }
        }
    }

    /// <summary>
    /// Force the highlight cache to re-tokenise from <paramref name="fromLine"/>
    /// onwards on the next <see cref="GetSyntaxTokens"/> or
    /// <see cref="TokeniseLines"/> call.
    /// </summary>
    public void InvalidateHighlightCache(int fromLine = 0)
    {
        if (fromLine <= 0)
            _highlightCache.InvalidateAll();
        else
            _highlightCache.InvalidateFrom(fromLine);
    }

    // ── Bracket matching + auto-indent ────────────────────────────────────

    /// <summary>
    /// Given an offset that points at an opening or closing bracket
    /// (<c>(</c> <c>)</c> <c>[</c> <c>]</c> <c>{</c> <c>}</c>), returns the
    /// offset of its matching counterpart.
    /// Brackets inside string and comment tokens are correctly ignored.
    /// Returns <c>-1</c> when the character is not a bracket or no match
    /// exists (unbalanced source).
    /// </summary>
    public int FindMatchingBracket(int offset)
        => Language.BracketMatcher.FindMatch(this, offset);

    /// <summary>
    /// Returns the indentation string to place at the start of the new line
    /// created when the user presses Enter at <paramref name="caretOffset"/>.
    /// Adds one extra <paramref name="tabText"/> level when the current line's
    /// meaningful content ends with <c>{</c>; otherwise copies the current
    /// line's leading whitespace.
    /// </summary>
    public string GetAutoIndent(int caretOffset, string tabText = "    ")
        => Language.AutoIndent.GetIndent(this, caretOffset, tabText);

    /// <summary>
    /// When a <c>}</c> is typed at <paramref name="caretOffset"/>, returns
    /// the leading whitespace of the line that contains the matching <c>{</c>,
    /// ready to replace the current line's indentation.
    /// Returns <see langword="null"/> when the offset is not a <c>}</c> or
    /// has no matching <c>{</c>.
    /// </summary>
    public string? GetClosingBraceIndent(int caretOffset, string tabText = "    ")
        => Language.AutoIndent.GetClosingBraceIndent(this, caretOffset, tabText);

    /// <summary>
    /// Returns all bracket pairs (with nesting-depth color index) whose opening
    /// bracket falls within [<paramref name="startLine"/>, <paramref name="endLine"/>].
    /// <list type="bullet">
    ///   <item><see cref="Language.BracketPair.ColorIndex"/> cycles 0→1→2 with nesting depth.</item>
    ///   <item><see cref="Language.BracketPair.ColorIndex"/> == -1 for unmatched brackets.</item>
    /// </list>
    /// Brackets inside string and comment tokens are excluded.
    /// </summary>
    public IReadOnlyList<Language.BracketPair> GetBracketPairs(int startLine, int endLine)
        => Language.BracketPairColorizer.GetBracketPairs(this, startLine, endLine);

    /// <summary>
    /// Returns indent guides for [<paramref name="startLine"/>, <paramref name="endLine"/>].
    /// Each <see cref="Language.IndentGuide"/> represents a vertical bar at a given column
    /// spanning the lines whose indentation exceeds that column.
    /// </summary>
    public IReadOnlyList<Language.IndentGuide> GetIndentGuides(
        int startLine, int endLine, int tabWidth = 4)
        => Language.IndentGuideProvider.GetGuides(this, startLine, endLine, tabWidth);

    // ── Decorations ───────────────────────────────────────────────────────

    public Guid AddDecoration(int start, int end, DecorationType type, string? tag = null, object? data = null)
    {
        var d = new Decoration { Start = start, Type = type, Tag = tag, Data = data }.SetEnd(end);
        _decorations.AddDecoration(d);
        return d.Id;
    }

    public bool RemoveDecoration(Guid id) => _decorations.RemoveDecoration(id);

    public IEnumerable<Decoration> GetDecorationsInRange(int start, int end) =>
        _decorations.GetDecorationsInRange(start, end);

    // ── Cursor position history ───────────────────────────────────────────

    private Navigation.CursorHistory? _cursorHistory;

    /// <summary>
    /// Returns the <see cref="Navigation.CursorHistory"/> for this document,
    /// creating it on first access with the default capacity (100 entries).
    ///
    /// Call <see cref="Navigation.CursorHistory.Push"/> whenever the caret
    /// makes a jump-type move (Find Next, Go to Line, Go to Definition).
    /// Normal arrow-key movement should NOT push.
    /// The history is cleared automatically on <see cref="Load"/> /
    /// <see cref="LoadFileAsync"/>.
    /// </summary>
    public Navigation.CursorHistory GetCursorHistory()
        => _cursorHistory ??= new Navigation.CursorHistory();

    // ── Word wrap ─────────────────────────────────────────────────────────

    private WordWrap.WordWrapModel? _wordWrapModel;

    /// <summary>
    /// Returns the <see cref="WordWrap.WordWrapModel"/> for this document,
    /// creating it on first access with the specified viewport width.
    /// Use <see cref="WordWrap.WordWrapModel.Resize"/> to change the viewport width later.
    /// </summary>
    public WordWrap.WordWrapModel GetWordWrapModel(int viewportWidth = 80)
        => _wordWrapModel ??= new WordWrap.WordWrapModel(this, viewportWidth);

    // ── Change tracking ───────────────────────────────────────────────────

    private ChangeTracking.ChangeTracker? _changeTracker;

    /// <summary>
    /// Returns the <see cref="ChangeTracking.ChangeTracker"/> for this document,
    /// creating it on first access.
    ///
    /// The tracker compares the current document against the baseline captured at
    /// the last <c>Load</c>, <c>Save</c>, or explicit
    /// <see cref="ChangeTracking.ChangeTracker.SetBaseline"/> call, and returns
    /// per-line <see cref="ChangeTracking.LineStatus"/> values (Clean / Added / Modified)
    /// plus deletion-above markers.
    /// </summary>
    public ChangeTracking.ChangeTracker GetChangeTracker()
        => _changeTracker ??= new ChangeTracking.ChangeTracker(this);

    // ── Read-only regions ─────────────────────────────────────────────────

    private ReadOnly.ReadOnlyRegionModel? _readOnlyModel;

    /// <summary>
    /// When <see langword="true"/> (the default), editing a protected offset throws
    /// <see cref="ReadOnly.ReadOnlyViolationException"/>.
    /// When <see langword="false"/>, the edit is silently ignored instead.
    /// </summary>
    public bool EnforceReadOnly { get; set; } = true;

    /// <summary>
    /// Returns the <see cref="ReadOnly.ReadOnlyRegionModel"/> for this document,
    /// creating it on first access.
    ///
    /// Use <see cref="ReadOnly.ReadOnlyRegionModel.Protect"/> to mark ranges as
    /// immutable.  Protected ranges are automatically remapped when edits occur
    /// elsewhere in the document.  All protections are cleared on
    /// <see cref="Load"/> / <see cref="LoadFileAsync"/>.
    /// </summary>
    public ReadOnly.ReadOnlyRegionModel GetReadOnlyModel()
        => _readOnlyModel ??= new ReadOnly.ReadOnlyRegionModel();

    // ── Inlay hints ───────────────────────────────────────────────────────

    private InlayHints.InlayHintModel? _inlayHintModel;

    /// <summary>
    /// Returns the <see cref="InlayHints.InlayHintModel"/> for this document,
    /// creating it on first access.
    /// Add hints via <see cref="InlayHints.InlayHintModel.AddHint"/> or
    /// <see cref="InlayHints.InlayHintModel.SetHints"/>.
    /// </summary>
    public InlayHints.InlayHintModel GetInlayHintModel()
        => _inlayHintModel ??= new InlayHints.InlayHintModel(this);

    // ── Code folding ─────────────────────────────────────────────────────

    private Folding.FoldingModel? _foldingModel;

    /// <summary>
    /// Returns the <see cref="Folding.FoldingModel"/> for this document,
    /// creating it on first access.
    ///
    /// Call <see cref="Folding.FoldingModel.UpdateRegions"/> with a strategy
    /// (e.g. <see cref="Folding.BraceFoldingStrategy"/>) to detect foldable
    /// regions.  After document edits, call <c>UpdateRegions</c> again to
    /// refresh the region list; existing fold state is preserved for any
    /// region whose start line is unchanged.
    /// </summary>
    public Folding.FoldingModel GetFoldingModel()
        => _foldingModel ??= new Folding.FoldingModel(this);

    // ── Save ─────────────────────────────────────────────────────────────

    public async Task SaveAsync(Stream stream, System.Text.Encoding? encoding = null)
    {
        // Explicit parameter beats SaveEncoding property; both beat auto-detected encoding.
        await _buffer.SaveAsync(stream, encoding ?? SaveEncoding);
        _changeTracker?.SetBaseline();
        IsModified = false;
    }

    public async Task SaveFileAsync(string? path = null, System.Text.Encoding? encoding = null)
    {
        path ??= FilePath ?? throw new InvalidOperationException("No file path set.");
        await using var stream = File.Create(path);
        await SaveAsync(stream, encoding);
        FilePath = path;
    }

    // ── Search ────────────────────────────────────────────────────────────

    private Search.TextSearcher? _searcher;
    private Search.TextSearcher  Searcher => _searcher ??= new Search.TextSearcher(_buffer);

    /// <summary>Find all matches. Streams results — stop early for incremental UI.</summary>
    public IEnumerable<Search.SearchMatch> FindAll(string pattern, Search.SearchOptions? opts = null)
        => Searcher.FindAll(pattern, opts);

    /// <summary>Find first match.</summary>
    public Search.SearchMatch? FindFirst(string pattern, Search.SearchOptions? opts = null)
        => Searcher.FindFirst(pattern, opts);

    /// <summary>Find next match at or after fromOffset.</summary>
    public Search.SearchMatch? FindNext(string pattern, int fromOffset, Search.SearchOptions? opts = null)
        => Searcher.FindNext(pattern, fromOffset, opts);

    /// <summary>Find previous match before beforeOffset.</summary>
    public Search.SearchMatch? FindPrev(string pattern, int beforeOffset, Search.SearchOptions? opts = null)
        => Searcher.FindPrev(pattern, beforeOffset, opts);

    /// <summary>Count matches without building a result list.</summary>
    public int CountMatches(string pattern, Search.SearchOptions? opts = null)
        => Searcher.Count(pattern, opts);

    /// <summary>
    /// Replace all occurrences of <paramref name="pattern"/> with <paramref name="replacement"/>.
    /// Returns the number of replacements made.
    ///
    /// PERFORMANCE: O(n) single-pass rewrite — not O(n log n).
    ///
    /// Algorithm:
    ///   1. Collect all match offsets via BMH span-streaming search — O(n).
    ///   2. Materialise the current document into a pooled char[] — O(n), one alloc.
    ///   3. Single forward pass: copy gap chars, write replacement, advance. — O(n).
    ///   4. Hand the result to BulkReplaceCommand which calls PieceTable.LoadRaw()
    ///      — O(1) tree work: reset + one root node. No per-match tree mutations.
    ///   5. The whole operation is a single undo step (snapshot original, swap new).
    ///
    /// Previous approach: N × ReplaceCommand inside CompositeCommand = O(N log N).
    ///   128k replacements: ~17 seconds.
    /// This approach: O(n) regardless of match count.
    ///   128k replacements: target < 500ms.
    /// </summary>
    public int ReplaceAll(string pattern, string replacement, Search.SearchOptions? opts = null)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;

        // ── 1. Collect matches ────────────────────────────────────────────
        var matches = Searcher.FindAll(pattern, opts).ToList();
        if (matches.Count == 0) return 0;

        // ── 2. Materialise current document into a flat char[] ────────────
        int srcLen   = _buffer.Length;
        char[] src   = System.Buffers.ArrayPool<char>.Shared.Rent(srcLen);
        _buffer.GetTextInto(0, srcLen, src);

        // ── 3. Compute output size and allocate ───────────────────────────
        // For literal search every match has the same length.
        // For regex, matches can have varying lengths — sum them individually.
        int totalMatchedChars = matches.Sum(m => m.Length);
        int outLen = srcLen - totalMatchedChars + (matches.Count * replacement.Length);

        char[] dst     = new char[outLen];
        char[] replBuf = replacement.ToCharArray();

        // ── 4. Single-pass forward copy ───────────────────────────────────
        int r = 0, w = 0;
        foreach (var m in matches)
        {
            // Copy gap between previous match end and this match start
            int gapLen = m.Offset - r;
            if (gapLen > 0)
            {
                src.AsSpan(r, gapLen).CopyTo(dst.AsSpan(w, gapLen));
                w += gapLen;
            }
            // Write replacement
            replBuf.AsSpan().CopyTo(dst.AsSpan(w, replacement.Length));
            w += replacement.Length;
            r  = m.Offset + m.Length;
        }
        // Copy remaining tail
        int tailLen = srcLen - r;
        if (tailLen > 0)
            src.AsSpan(r, tailLen).CopyTo(dst.AsSpan(w, tailLen));

        System.Buffers.ArrayPool<char>.Shared.Return(src);

        // ── 5. Issue as a single bulk command (one undo step) ─────────────
        var cmd = new Commands.BulkReplaceCommand(
            _buffer, dst, outLen, matches.Count, pattern, replacement);
        _history.Execute(cmd);

        // Decorations are invalidated — simplest safe approach
        _decorations.Clear();
        _highlightCache.InvalidateAll();
        _foldingModel?.Invalidate();
        _changeTracker?.Invalidate();
        _wordWrapModel?.Invalidate();
        _inlayHintModel?.ClearHints();
        _searcher = null;  // invalidate cached searcher (buffer changed)
        IsModified = true;

        return matches.Count;
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    /// <summary>Number of pieces in the piece table (useful for perf diagnostics).</summary>
    public int PieceCount => _buffer.PieceCount;

    /// <summary>Force compaction of the piece table.</summary>
    public void Compact() => _buffer.Compact();
}

// ── Extension helper (keeps Decoration immutable-ish init but settable End) ──
internal static class DecorationExt
{
    internal static Decoration SetEnd(this Decoration d, int end) { d.End = end; return d; }
}
