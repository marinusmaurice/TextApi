namespace TextEditor.Core.Language;

/// <summary>
/// Per-line token cache that enables incremental syntax re-highlighting.
///
/// After an edit, only the lines whose tokeniser <em>state</em> actually
/// changed need to be re-scanned.  The cache propagates inter-line state
/// forward from the first dirty line and stops as soon as a line's new
/// <c>StateOut</c> equals its previously-cached <c>StateOut</c> (state has
/// stabilised), meaning all subsequent cached entries remain valid.
///
/// For tokenisers that do not implement <see cref="IStatefulSyntaxTokeniser"/>
/// the cache degrades gracefully: every line is tokenised independently with
/// state 0, just as the previous scratch-based implementation did.
///
/// Thread safety: not thread-safe.  All calls must be made on the same thread
/// as the owning <see cref="TextDocument"/>.
/// </summary>
internal sealed class LineHighlightCache
{
    // ── Per-line entry ─────────────────────────────────────────────────────

    private struct Entry
    {
        public bool Valid;
        public int  StateIn;
        public int  StateOut;
        public uint ContentHash;
        public IReadOnlyList<SyntaxToken>? Tokens;
    }

    // ── State ─────────────────────────────────────────────────────────────

    private Entry[]                    _entries;
    private int                        _trackedLineCount;
    private ISyntaxTokeniser           _tokeniser;
    private IStatefulSyntaxTokeniser?  _stateful;
    private int                        _initialState;
    private readonly TextDocument      _doc;

    // ── Construction ──────────────────────────────────────────────────────

    internal LineHighlightCache(TextDocument doc, ISyntaxTokeniser tokeniser)
    {
        _doc              = doc;
        _entries          = new Entry[Math.Max(64, doc.LineCount + 1)];
        _trackedLineCount = doc.LineCount;
        ApplyTokeniser(tokeniser);
    }

    internal void SetTokeniser(ISyntaxTokeniser tokeniser)
    {
        ApplyTokeniser(tokeniser);
        InvalidateAll();
    }

    private void ApplyTokeniser(ISyntaxTokeniser tokeniser)
    {
        _tokeniser    = tokeniser;
        _stateful     = tokeniser as IStatefulSyntaxTokeniser;
        _initialState = _stateful?.InitialState ?? 0;
    }

    // ── Invalidation ──────────────────────────────────────────────────────

    internal void InvalidateAll()
    {
        for (int i = 0; i < _entries.Length; i++) _entries[i].Valid = false;
    }

    internal void InvalidateFrom(int lineIndex)
    {
        for (int i = lineIndex; i < _entries.Length; i++) _entries[i].Valid = false;
    }

    // ── Document edit hooks ───────────────────────────────────────────────

    /// <summary>
    /// Must be called immediately after an insertion.
    /// The document buffer is already updated when this is invoked.
    /// </summary>
    internal void OnInsert(int offset, int insertedLength)
    {
        if (insertedLength == 0) return;

        int newLineCount = _doc.LineCount;
        int firstLine    = _doc.OffsetToPosition(Math.Min(offset, _doc.Length)).Line;
        int linesAdded   = newLineCount - _trackedLineCount;

        EnsureCapacity(newLineCount);

        if (linesAdded > 0)
        {
            // Shift entries downward to open slots for the new lines.
            int insertAt   = firstLine + 1;
            int shiftCount = Math.Max(0, _trackedLineCount - insertAt);
            if (shiftCount > 0)
                Array.Copy(_entries, insertAt, _entries, insertAt + linesAdded, shiftCount);
            for (int i = insertAt; i < insertAt + linesAdded; i++) _entries[i] = default;
        }

        InvalidateFrom(firstLine);
        _trackedLineCount = newLineCount;
    }

    /// <summary>
    /// Must be called immediately after a deletion.
    /// The document buffer is already updated when this is invoked.
    /// </summary>
    internal void OnDelete(int offset, int deletedLength)
    {
        if (deletedLength == 0) return;

        int newLineCount = _doc.LineCount;
        int firstLine    = _doc.OffsetToPosition(Math.Min(offset, Math.Max(0, _doc.Length - 1))).Line;
        if (_doc.Length == 0) firstLine = 0;
        int linesRemoved = _trackedLineCount - newLineCount;

        if (linesRemoved > 0)
        {
            int removeAt   = firstLine + 1;
            int shiftFrom  = removeAt + linesRemoved;
            int shiftCount = Math.Max(0, _trackedLineCount - shiftFrom);
            if (shiftCount > 0)
                Array.Copy(_entries, shiftFrom, _entries, removeAt, shiftCount);
            for (int i = newLineCount; i < _trackedLineCount && i < _entries.Length; i++)
                _entries[i] = default;
        }

        InvalidateFrom(firstLine);
        _trackedLineCount = newLineCount;
    }

    // ── Token retrieval ───────────────────────────────────────────────────

    /// <summary>
    /// Get syntax tokens for <paramref name="lineIndex"/>.
    /// Tokenises lazily on demand, propagating state from the last valid line.
    /// O(1) when the line is already cached; O(k) where k is the number of
    /// consecutive invalid lines that must be re-scanned first.
    /// </summary>
    internal IReadOnlyList<SyntaxToken> GetTokens(int lineIndex)
    {
        int lineCount = _doc.LineCount;
        if (lineIndex < 0 || lineIndex >= lineCount) return [];

        EnsureCapacity(lineCount);

        if (_entries[lineIndex].Valid)
            return _entries[lineIndex].Tokens!;

        // Find the first invalid line at or before lineIndex.
        int start = lineIndex;
        while (start > 0 && !_entries[start - 1].Valid) start--;

        int state = start == 0 ? _initialState : _entries[start - 1].StateOut;

        for (int i = start; i <= lineIndex; i++)
            state = TokeniseSingleLine(i, state);

        return _entries[lineIndex].Tokens!;
    }

    /// <summary>
    /// Eagerly tokenise [<paramref name="startLine"/>, <paramref name="endLine"/>]
    /// and then continue forward until the per-line state stabilises.
    ///
    /// Returns the first line index <em>beyond</em> the stabilised range —
    /// i.e. the exclusive upper bound of lines that were actually re-scanned.
    /// Callers can use this to know the true "dirty window" size.
    /// </summary>
    internal int WarmUp(int startLine, int endLine)
    {
        int lineCount = _doc.LineCount;
        endLine = Math.Min(endLine, lineCount - 1);
        if (startLine > endLine || lineCount == 0) return startLine;

        EnsureCapacity(lineCount);

        // Resolve entry state at startLine.
        int start = startLine;
        while (start > 0 && !_entries[start - 1].Valid) start--;
        int state = start == 0 ? _initialState : _entries[start - 1].StateOut;

        int i;
        for (i = start; i < lineCount; i++)
        {
            bool wasValid    = _entries[i].Valid;
            int  oldStateOut = _entries[i].StateOut;

            state = TokeniseSingleLine(i, state);

            // Once the requested range is covered, stop when state stabilises:
            // the new StateOut matches what was cached, so every line below is
            // still correct.
            if (i > endLine && wasValid && _entries[i].StateOut == oldStateOut)
            {
                i++;   // exclusive end
                break;
            }
        }
        return i;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Tokenise line <paramref name="lineIndex"/> using <paramref name="stateIn"/>.
    /// Updates the cache entry and returns the resulting <c>stateOut</c>.
    /// If the line is already valid and nothing changed (same hash, same stateIn),
    /// the cached entry is reused without re-tokenising.
    /// </summary>
    private int TokeniseSingleLine(int lineIndex, int stateIn)
    {
        string lineText   = _doc.GetLine(lineIndex);
        int    lineOffset = _doc.PositionToOffset(lineIndex, 0);
        uint   hash       = FnvHash(lineText);

        ref Entry e = ref _entries[lineIndex];

        // Cache hit: content and incoming state are identical.
        if (e.Valid && e.ContentHash == hash && e.StateIn == stateIn)
            return e.StateOut;

        IReadOnlyList<SyntaxToken> tokens;
        int stateOut;

        if (_stateful != null)
            tokens = _stateful.TokeniseLine(lineText, lineOffset, stateIn, out stateOut);
        else
        {
            tokens   = _tokeniser.TokeniseLine(lineText, lineOffset);
            stateOut = 0;
        }

        e = new Entry
        {
            Valid       = true,
            StateIn     = stateIn,
            StateOut    = stateOut,
            ContentHash = hash,
            Tokens      = tokens,
        };

        return stateOut;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _entries.Length) return;
        Array.Resize(ref _entries, Math.Max(required, _entries.Length * 2));
    }

    // FNV-1a, 32-bit — fast and good enough for line content hashing.
    private static uint FnvHash(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s)
        {
            h ^= c;
            h *= 16777619u;
        }
        return h;
    }
}
