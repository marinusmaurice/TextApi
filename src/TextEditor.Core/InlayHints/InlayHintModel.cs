namespace TextEditor.Core.InlayHints;

/// <summary>
/// Stores InlayHint annotations and keeps their offsets in sync with document edits.
///
/// Design
/// ──────
///   Hints are stored sorted by Offset for efficient range queries.
///   On Insert: hints at or after the insertion point shift right by insertedLength.
///   On Delete: hints within the deleted range are removed; hints after shift left.
///   HintsChanged fires after every structural change.
///
/// Consumers (LSP client, linters) call SetHints() to replace all hints at once,
/// or AddHint() / RemoveHint() for individual management.
/// </summary>
public sealed class InlayHintModel
{
    private readonly TextDocument _doc;
    // Hints kept sorted by Offset at all times.
    private readonly List<InlayHint> _hints = [];

    /// <summary>Fired when hints are added, removed, or their offsets change.</summary>
    public event EventHandler? HintsChanged;

    internal InlayHintModel(TextDocument doc)
    {
        _doc = doc;
    }

    // ── Hint management ───────────────────────────────────────────────────

    /// <summary>Add a single hint. Returns the hint's assigned Id.</summary>
    public Guid AddHint(InlayHint hint)
    {
        int idx = FindInsertIndex(hint.Offset);
        _hints.Insert(idx, hint);
        HintsChanged?.Invoke(this, EventArgs.Empty);
        return hint.Id;
    }

    /// <summary>Replace all current hints with the given collection (e.g. fresh LSP response).</summary>
    public void SetHints(IEnumerable<InlayHint> hints)
    {
        _hints.Clear();
        _hints.AddRange(hints);
        _hints.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        HintsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Remove the hint with the given Id. Returns true if found.</summary>
    public bool RemoveHint(Guid id)
    {
        int idx = _hints.FindIndex(h => h.Id == id);
        if (idx < 0) return false;
        _hints.RemoveAt(idx);
        HintsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Remove all hints.</summary>
    public void ClearHints()
    {
        if (_hints.Count == 0) return;
        _hints.Clear();
        HintsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Queries ───────────────────────────────────────────────────────────

    /// <summary>All current hints, in offset order.</summary>
    public IReadOnlyList<InlayHint> AllHints => _hints;

    /// <summary>Returns hints whose offset falls within [startOffset, endOffset).</summary>
    public IReadOnlyList<InlayHint> GetHintsInRange(int startOffset, int endOffset)
    {
        // Binary search for start
        int lo = LowerBound(startOffset);
        var result = new List<InlayHint>();
        for (int i = lo; i < _hints.Count && _hints[i].Offset < endOffset; i++)
            result.Add(_hints[i]);
        return result;
    }

    /// <summary>Returns hints filtered by kind.</summary>
    public IReadOnlyList<InlayHint> GetHintsByKind(InlayHintKind kind)
        => _hints.Where(h => h.Kind == kind).ToList();

    /// <summary>Returns the hint at exactly the given offset, or null.</summary>
    public InlayHint? GetHintAt(int offset)
    {
        int lo = LowerBound(offset);
        if (lo < _hints.Count && _hints[lo].Offset == offset)
            return _hints[lo];
        return null;
    }

    // ── OnInsert / OnDelete ───────────────────────────────────────────────

    internal void OnInsert(int offset, int insertedLength)
    {
        if (insertedLength == 0 || _hints.Count == 0) return;
        bool changed = false;
        foreach (var hint in _hints)
        {
            if (hint.Offset >= offset)
            {
                hint.Offset += insertedLength;
                changed = true;
            }
        }
        if (changed) HintsChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void OnDelete(int offset, int deletedLength)
    {
        if (deletedLength == 0 || _hints.Count == 0) return;
        int deleteEnd = offset + deletedLength;
        bool changed = false;

        // Remove hints within the deleted range, shift those after it.
        for (int i = _hints.Count - 1; i >= 0; i--)
        {
            var hint = _hints[i];
            if (hint.Offset >= offset && hint.Offset < deleteEnd)
            {
                _hints.RemoveAt(i);
                changed = true;
            }
            else if (hint.Offset >= deleteEnd)
            {
                hint.Offset -= deletedLength;
                changed = true;
            }
        }
        if (changed) HintsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private int FindInsertIndex(int offset)
    {
        int lo = 0, hi = _hints.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_hints[mid].Offset <= offset) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    private int LowerBound(int offset)
    {
        int lo = 0, hi = _hints.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_hints[mid].Offset < offset) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
