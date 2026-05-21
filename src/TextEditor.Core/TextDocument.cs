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
    private readonly DecorationTree  _decorations;
    private          ISyntaxTokeniser _tokeniser;

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
        FilePath   = path;
        IsModified = false;
    }

    // ── Document stats ────────────────────────────────────────────────────

    public int Length    => _buffer.Length;
    public int LineCount => _buffer.LineCount;
    public EolStyle OriginalEolStyle => _buffer.OriginalEolStyle;

    public EolStyle SaveEolStyle
    {
        get => _buffer.SaveEolStyle;
        set => _buffer.SaveEolStyle = value;
    }

    // ── Edit operations (all via command pattern → undo/redo) ─────────────

    /// <summary>Insert text at a zero-based character offset.</summary>
    public void Insert(int offset, string text)
    {
        var cmd = new InsertCommand(_buffer, offset, text);
        _history.Execute(cmd);
        _decorations.OnInsert(offset, text.Length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Delete characters in [offset, offset+length).</summary>
    public void Delete(int offset, int length)
    {
        var cmd = new DeleteCommand(_buffer, offset, length);
        _history.Execute(cmd);
        _decorations.OnDelete(offset, length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Replace characters in [offset, offset+deleteLength) with insertText.</summary>
    public void Replace(int offset, int deleteLength, string insertText)
    {
        var cmd = new ReplaceCommand(_buffer, offset, deleteLength, insertText);
        _history.Execute(cmd);
        _decorations.OnDelete(offset, deleteLength);
        _decorations.OnInsert(offset, insertText.Length);
        IsModified = true;
        PostEditHook();
    }

    /// <summary>Execute multiple edit commands as a single undoable unit.</summary>
    public void ExecuteComposite(string description, IEnumerable<IEditorCommand> commands)
    {
        var composite = new CompositeCommand(description, commands);
        _history.Execute(composite);
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
        IsModified = true;
    }

    public void Redo()
    {
        _history.Redo();
        _decorations.Clear();
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

    /// <summary>Replace the tokeniser (e.g. switch from null to C# tokeniser after detecting language).</summary>
    public void SetTokeniser(ISyntaxTokeniser tokeniser) => _tokeniser = tokeniser;

    /// <summary>Tokenise a single line and return syntax tokens.</summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(int lineIndex)
    {
        var line = GetLine(lineIndex);
        var offset = _buffer.PositionToOffset(lineIndex, 0);
        return _tokeniser.TokeniseLine(line, offset);
    }

    /// <summary>
    /// Tokenise a range of lines and push results into the decoration tree
    /// as SyntaxHighlight decorations. Previous syntax decorations are cleared first.
    /// </summary>
    public void TokeniseLines(int startLine, int endLine)
    {
        _decorations.RemoveAllOfType(DecorationType.SyntaxHighlight);
        for (int i = startLine; i <= Math.Min(endLine, LineCount - 1); i++)
        {
            var tokens = TokeniseLine(i);
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

    // ── Save ─────────────────────────────────────────────────────────────

    public async Task SaveAsync(Stream stream, System.Text.Encoding? encoding = null)
    {
        await _buffer.SaveAsync(stream, encoding);
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
