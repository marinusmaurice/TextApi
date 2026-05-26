namespace TextAPI.Core.Decorations;

/// <summary>The kind of a decoration.</summary>
public enum DecorationType
{
    SyntaxHighlight,
    ErrorSquiggle,
    WarningSquiggle,
    InfoSquiggle,
    Selection,
    SearchMatch,
    Bookmark,
    Custom
}

/// <summary>
/// A single decoration over a character range [Start, End).
/// Stored in the DecorationTree — a separate interval RB-tree,
/// completely independent of the text model.
/// </summary>
public sealed class Decoration
{
    public Guid   Id   { get; init; } = Guid.NewGuid();
    public int    Start { get; set; }
    public int    End   { get; set; }
    public DecorationType Type { get; init; }
    public string? Tag  { get; init; }   // e.g. token type "keyword", "string"
    public object? Data { get; init; }   // caller-supplied payload (colour, message, etc.)

    public int Length => End - Start;
}

/// <summary>
/// Interval tree (simplified sorted list — for production use a proper
/// augmented RB interval tree for O(log n) stab queries).
///
/// Keeps decorations in a list sorted by Start. Provides:
///   • AddDecoration  — O(log n) insertion point, O(n) shift (acceptable for typical decoration counts)
///   • RemoveDecoration — by id
///   • GetDecorationsInRange — returns all decorations that overlap [start, end)
///   • ShiftDecorations — called by the text model after insert/delete to keep ranges valid
/// </summary>
public sealed class DecorationTree
{
    private readonly List<Decoration> _decorations = [];

    // ── Add / Remove ──────────────────────────────────────────────────────

    public void AddDecoration(Decoration d)
    {
        int i = FindInsertIndex(d.Start);
        _decorations.Insert(i, d);
    }

    public bool RemoveDecoration(Guid id)
    {
        int i = _decorations.FindIndex(d => d.Id == id);
        if (i < 0) return false;
        _decorations.RemoveAt(i);
        return true;
    }

    public void RemoveAllOfType(DecorationType type) =>
        _decorations.RemoveAll(d => d.Type == type);

    public void Clear() => _decorations.Clear();

    // ── Query ─────────────────────────────────────────────────────────────

    /// <summary>All decorations whose range overlaps [start, end).</summary>
    public IEnumerable<Decoration> GetDecorationsInRange(int start, int end)
    {
        foreach (var d in _decorations)
        {
            if (d.Start >= end) break;           // sorted — nothing further overlaps
            if (d.End   >  start) yield return d;
        }
    }

    /// <summary>All decorations on a specific line (resolved via a line-to-offset mapper).</summary>
    public IEnumerable<Decoration> GetDecorationsOnLine(int lineStart, int lineEnd) =>
        GetDecorationsInRange(lineStart, lineEnd);

    // ── Shift (called after insert/delete) ───────────────────────────────

    /// <summary>
    /// After inserting <paramref name="length"/> characters at <paramref name="offset"/>,
    /// shift all decoration ranges that start at or after the insertion point.
    /// Decorations that span the insertion point are extended.
    /// </summary>
    public void OnInsert(int offset, int length)
    {
        foreach (var d in _decorations)
        {
            if (d.Start >= offset)
            {
                d.Start += length;
                d.End   += length;
            }
            else if (d.End > offset)
            {
                d.End += length;   // decoration spans insertion — extend it
            }
        }
    }

    /// <summary>
    /// After deleting <paramref name="length"/> characters starting at <paramref name="offset"/>,
    /// adjust all affected decoration ranges.
    /// Decorations fully inside the deleted range are removed.
    /// </summary>
    public void OnDelete(int offset, int length)
    {
        int end = offset + length;
        _decorations.RemoveAll(d => d.Start >= offset && d.End <= end);

        foreach (var d in _decorations)
        {
            if (d.Start >= end)
            {
                d.Start -= length;
                d.End   -= length;
            }
            else if (d.Start > offset)
            {
                d.End   = offset;
                d.Start = offset;
            }
            else if (d.End > offset)
            {
                d.End = d.End >= end
                    ? d.End - length
                    : offset;
            }
        }
    }

    public int Count => _decorations.Count;

    // ── Private ───────────────────────────────────────────────────────────

    private int FindInsertIndex(int start)
    {
        int lo = 0, hi = _decorations.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_decorations[mid].Start <= start) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
