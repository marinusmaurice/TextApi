namespace TextAPI.Core.ReadOnly;

/// <summary>
/// Marks offset ranges as immutable.
///
/// <para>
/// When a region <c>[start, end)</c> is protected, edits that overlap it are
/// either rejected (throwing <see cref="ReadOnlyViolationException"/>) or silently
/// ignored depending on <see cref="TextDocument.EnforceReadOnly"/>.
/// </para>
///
/// <para>
/// Regions are automatically remapped when allowed edits occur elsewhere in
/// the document (same hook pattern as <c>InlayHintModel</c>).
/// </para>
///
/// <para>
/// Boundaries:
/// <list type="bullet">
///   <item>Insert at exactly <c>Start</c>: <b>allowed</b> — text goes before the region, region shifts right.</item>
///   <item>Insert at exactly <c>End</c>: <b>allowed</b> — text goes after the region, no shift.</item>
///   <item>Insert strictly inside <c>(Start, End)</c>: <b>blocked</b>.</item>
///   <item>Delete overlapping <c>[Start, End)</c>: <b>blocked</b>.</item>
/// </list>
/// </para>
/// </summary>
public sealed class ReadOnlyRegionModel
{
    private readonly record struct Region(Guid Id, int Start, int End);
    private readonly List<Region> _regions = [];

    /// <summary>Fired when regions are added, removed, or cleared.</summary>
    public event EventHandler? RegionsChanged;

    // ── Protection management ─────────────────────────────────────────────

    /// <summary>
    /// Mark the half-open range <c>[start, end)</c> as read-only.
    /// Returns a <see cref="Guid"/> that can be used to remove the protection later.
    /// </summary>
    /// <param name="start">First protected character offset (inclusive).</param>
    /// <param name="end">First unprotected character offset after the region (exclusive).</param>
    public Guid Protect(int start, int end)
    {
        if (start < 0)    throw new ArgumentOutOfRangeException(nameof(start), "start must be ≥ 0.");
        if (end < start)  throw new ArgumentOutOfRangeException(nameof(end),   "end must be ≥ start.");

        var id = Guid.NewGuid();
        _regions.Add(new Region(id, start, end));
        RegionsChanged?.Invoke(this, EventArgs.Empty);
        return id;
    }

    /// <summary>
    /// Remove the protection with the given <paramref name="id"/>.
    /// Returns <see langword="false"/> if the id is not found.
    /// </summary>
    public bool Unprotect(Guid id)
    {
        int idx = _regions.FindIndex(r => r.Id == id);
        if (idx < 0) return false;
        _regions.RemoveAt(idx);
        RegionsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Remove all protected regions.</summary>
    public void UnprotectAll()
    {
        if (_regions.Count == 0) return;
        _regions.Clear();
        RegionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Query ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="offset"/> falls within
    /// any protected region (i.e. <c>region.Start ≤ offset &lt; region.End</c>).
    /// </summary>
    public bool IsReadOnly(int offset)
    {
        foreach (var r in _regions)
            if (r.Start <= offset && offset < r.End) return true;
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the half-open range
    /// <c>[start, start+length)</c> overlaps any protected region.
    /// </summary>
    public bool IsRangeReadOnly(int start, int length)
    {
        int end = start + length;
        foreach (var r in _regions)
            if (start < r.End && end > r.Start) return true;
        return false;
    }

    /// <summary>
    /// All currently protected regions as <c>(Id, Start, End)</c> tuples.
    /// </summary>
    public IReadOnlyList<(Guid Id, int Start, int End)> GetRegions()
    {
        var result = new (Guid, int, int)[_regions.Count];
        for (int i = 0; i < _regions.Count; i++)
            result[i] = (_regions[i].Id, _regions[i].Start, _regions[i].End);
        return result;
    }

    // ── Edit-gate helpers (used by TextDocument) ──────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when inserting at <paramref name="offset"/>
    /// is blocked — i.e. the offset is strictly inside a protected region
    /// (<c>region.Start &lt; offset &lt; region.End</c>).
    /// </summary>
    internal bool WouldBlockInsert(int offset)
    {
        foreach (var r in _regions)
            if (r.Start < offset && offset < r.End) return true;
        return false;
    }

    /// <summary>
    /// Returns (is-blocked, violating-region-start, violating-region-end).
    /// Blocked when the half-open delete range <c>[offset, offset+length)</c>
    /// overlaps any protected region.
    /// </summary>
    internal (bool Blocked, int RegionStart, int RegionEnd) WouldBlockDelete(int offset, int length)
    {
        int end = offset + length;
        foreach (var r in _regions)
        {
            if (offset < r.End && end > r.Start)
                return (true, r.Start, r.End);
        }
        return (false, 0, 0);
    }

    /// <summary>
    /// Returns (is-blocked, violating-region-start, violating-region-end).
    /// Blocked when the insert offset is strictly inside a protected region.
    /// </summary>
    internal (bool Blocked, int RegionStart, int RegionEnd) WouldBlockInsertInfo(int offset)
    {
        foreach (var r in _regions)
        {
            if (r.Start < offset && offset < r.End)
                return (true, r.Start, r.End);
        }
        return (false, 0, 0);
    }

    // ── Offset remapping (called by TextDocument after allowed edits) ─────

    /// <summary>
    /// Shift region offsets to account for an insertion of <paramref name="length"/>
    /// characters at <paramref name="offset"/>.
    /// Insert at exactly <c>region.Start</c> shifts the region right.
    /// Insert at or after <c>region.End</c> leaves the region unchanged.
    /// </summary>
    internal void OnInsert(int offset, int length)
    {
        for (int i = 0; i < _regions.Count; i++)
        {
            var r = _regions[i];
            if (offset <= r.Start)
                _regions[i] = r with { Start = r.Start + length, End = r.End + length };
            // offset strictly inside (blocked case — shouldn't reach here)
            // offset >= r.End → no change
        }
    }

    /// <summary>
    /// Shift region offsets to account for a deletion of <paramref name="length"/>
    /// characters starting at <paramref name="offset"/>.
    /// Delete entirely before a region shifts it left.
    /// Delete overlapping a region is blocked and should never reach here.
    /// </summary>
    internal void OnDelete(int offset, int length)
    {
        int delEnd = offset + length;
        for (int i = 0; i < _regions.Count; i++)
        {
            var r = _regions[i];
            if (delEnd <= r.Start)
                _regions[i] = r with { Start = r.Start - length, End = r.End - length };
            // overlap → blocked, skip (defensive)
            // delete entirely after region → no change
        }
    }
}
