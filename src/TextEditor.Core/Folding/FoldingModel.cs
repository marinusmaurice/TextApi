namespace TextEditor.Core.Folding;

/// <summary>
/// Manages foldable regions for a <see cref="TextDocument"/>.
///
/// The model is separate from the text buffer — it holds no text of its own.
/// A UI layer (or test) drives it by:
/// <list type="number">
///   <item>Calling <see cref="UpdateRegions"/> with an <see cref="IFoldingStrategy"/>
///         to (re)detect regions after a document change.</item>
///   <item>Calling <see cref="Fold"/>, <see cref="Unfold"/>, or
///         <see cref="ToggleFold"/> to change fold state.</item>
///   <item>Querying <see cref="IsLineVisible"/>, <see cref="GetVisibleLines"/>,
///         <see cref="ToDisplayLine"/>, and <see cref="ToDocumentLine"/> to
///         drive the visible-line layout.</item>
/// </list>
///
/// <para>
/// <b>Live document tracking</b><br/>
/// <see cref="TextDocument"/> automatically notifies this model on every
/// <c>Insert</c>, <c>Delete</c>, and <c>Replace</c> call so that
/// <see cref="FoldRegion.StartLine"/> / <see cref="FoldRegion.EndLine"/>
/// values stay in sync with the current document without requiring a full
/// <see cref="UpdateRegions"/> pass.  Destructive operations (undo, redo,
/// load, replace-all) call <see cref="Invalidate"/> instead and set
/// <see cref="IsStale"/> to <see langword="true"/>; the UI should call
/// <see cref="UpdateRegions"/> before relying on region data again.
/// </para>
///
/// <para>
/// <b>Events</b><br/>
/// Subscribe to <see cref="RegionsChanged"/> to be notified when the set of
/// regions changes (after <see cref="UpdateRegions"/>).  Subscribe to
/// <see cref="FoldStateChanged"/> to be notified when any fold is opened or
/// closed.
/// </para>
/// </summary>
public sealed class FoldingModel
{
    private readonly TextDocument     _doc;
    private readonly List<FoldRegion> _regions = [];
    private readonly HashSet<int>     _folded  = [];   // set of current StartLines that are folded

    /// <param name="doc">The document this model tracks.</param>
    public FoldingModel(TextDocument doc) => _doc = doc;

    // ── Events ────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised after <see cref="UpdateRegions"/> completes.
    /// Bind this in your UI to refresh gutter fold markers.
    /// </summary>
    public event EventHandler? RegionsChanged;

    /// <summary>
    /// Raised after any fold state change
    /// (<see cref="Fold"/>, <see cref="Unfold"/>, <see cref="ToggleFold"/>,
    /// <see cref="FoldAll"/>, <see cref="UnfoldAll"/>).
    /// Bind this in your UI to refresh the visible-line layout.
    /// </summary>
    public event EventHandler? FoldStateChanged;

    // ── Staleness ─────────────────────────────────────────────────────────

    /// <summary>
    /// <see langword="true"/> after a destructive document operation (undo,
    /// redo, bulk replace, load) that cannot be incrementally remapped.
    /// Call <see cref="UpdateRegions"/> to clear this flag.
    /// </summary>
    public bool IsStale { get; private set; }

    /// <summary>
    /// Mark the model as stale.  Called by <see cref="TextDocument"/> after
    /// undo, redo, load, and replace-all — operations whose line-delta cannot
    /// be determined cheaply.
    /// </summary>
    internal void Invalidate() => IsStale = true;

    // ── Region management ─────────────────────────────────────────────────

    /// <summary>All detected regions, sorted by <see cref="FoldRegion.StartLine"/>.</summary>
    public IReadOnlyList<FoldRegion> Regions => _regions;

    /// <summary>
    /// Re-detect foldable regions using <paramref name="strategy"/>.
    /// Previously-folded regions whose <see cref="FoldRegion.StartLine"/>
    /// matches a newly-detected region will remain folded.
    /// Clears <see cref="IsStale"/> and fires <see cref="RegionsChanged"/>.
    /// </summary>
    public void UpdateRegions(IFoldingStrategy strategy)
    {
        var detected   = strategy.DetectRegions(_doc);
        var prevFolded = new HashSet<int>(_folded);

        _regions.Clear();
        _folded.Clear();

        foreach (var (start, end, label) in detected)
        {
            if (start >= end) continue;
            var region = new FoldRegion(start, end, label);
            _regions.Add(region);

            if (prevFolded.Contains(start))
            {
                region.IsFolded = true;
                _folded.Add(start);
            }
        }

        _regions.Sort((a, b) => a.StartLine.CompareTo(b.StartLine));
        IsStale = false;
        RegionsChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Incremental line-number tracking ──────────────────────────────────

    /// <summary>
    /// Called by <see cref="TextDocument"/> after text is inserted at
    /// <paramref name="offset"/>.
    /// <paramref name="insertedNewlines"/> is the number of newline characters
    /// in the inserted text (0 for same-line edits, which are no-ops here).
    /// </summary>
    internal void OnInsert(int offset, int insertedNewlines)
    {
        if (_regions.Count == 0 || insertedNewlines == 0) return;

        // Which line does the insert start on? (Document already has new content.)
        int atLine = _doc.OffsetToPosition(offset).Line;

        foreach (var r in _regions)
        {
            if (r.StartLine > atLine)
            {
                // Region starts after the insert point → shift both endpoints.
                r.StartLine += insertedNewlines;
                r.EndLine   += insertedNewlines;
            }
            else if (r.EndLine >= atLine)
            {
                // Insert is within this region → the region expands downward.
                r.EndLine += insertedNewlines;
            }
            // else: region ends before the insert point → unchanged.
        }

        RebuildFoldedSet();
    }

    /// <summary>
    /// Called by <see cref="TextDocument"/> after text is deleted at
    /// <paramref name="offset"/>.
    /// <paramref name="deletedNewlines"/> is the number of newline characters
    /// in the deleted text, captured <em>before</em> the delete executed.
    /// </summary>
    internal void OnDelete(int offset, int deletedNewlines)
    {
        if (_regions.Count == 0 || deletedNewlines == 0) return;

        // After the delete, offset is the position where the gap used to start.
        int atLine         = _doc.OffsetToPosition(offset).Line;
        int lastDeletedLine = atLine + deletedNewlines;   // in old line-number space

        var toRemove = new List<FoldRegion>();

        foreach (var r in _regions)
        {
            if (r.EndLine <= atLine)
            {
                // Region ends before the deleted range → unchanged.
                continue;
            }

            if (r.StartLine > lastDeletedLine)
            {
                // Region is entirely after the deleted range → shift up.
                r.StartLine -= deletedNewlines;
                r.EndLine   -= deletedNewlines;
            }
            else
            {
                // Region overlaps the deleted lines — clamp or remove.
                int newStart = r.StartLine <= atLine ? r.StartLine : atLine;
                int newEnd   = r.EndLine > lastDeletedLine
                                 ? r.EndLine - deletedNewlines
                                 : atLine;

                if (newStart >= newEnd)
                    toRemove.Add(r);
                else
                {
                    r.StartLine = newStart;
                    r.EndLine   = newEnd;
                }
            }
        }

        foreach (var r in toRemove) _regions.Remove(r);
        RebuildFoldedSet();
    }

    // ── Fold / unfold ─────────────────────────────────────────────────────

    /// <summary>
    /// Fold the region whose <see cref="FoldRegion.StartLine"/> equals
    /// <paramref name="startLine"/>.
    /// Returns <see langword="true"/> on success, <see langword="false"/> if
    /// the region does not exist or is already folded.
    /// </summary>
    public bool Fold(int startLine)
    {
        var region = FindRegion(startLine);
        if (region == null || region.IsFolded) return false;
        region.IsFolded = true;
        _folded.Add(startLine);
        FoldStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Unfold the region at <paramref name="startLine"/>.
    /// Returns <see langword="true"/> on success, <see langword="false"/> if
    /// the region does not exist or is already open.
    /// </summary>
    public bool Unfold(int startLine)
    {
        var region = FindRegion(startLine);
        if (region == null || !region.IsFolded) return false;
        region.IsFolded = false;
        _folded.Remove(startLine);
        FoldStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Toggle the fold state of the region at <paramref name="startLine"/>.</summary>
    public void ToggleFold(int startLine)
    {
        if (!Unfold(startLine)) Fold(startLine);
    }

    /// <summary>Fold every region.</summary>
    public void FoldAll()
    {
        foreach (var r in _regions)
        {
            r.IsFolded = true;
            _folded.Add(r.StartLine);
        }
        FoldStateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Unfold every region.</summary>
    public void UnfoldAll()
    {
        foreach (var r in _regions) r.IsFolded = false;
        _folded.Clear();
        FoldStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Visibility queries ────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="line"/> is not
    /// hidden by any folded region.
    /// </summary>
    public bool IsLineVisible(int line)
    {
        foreach (var r in _regions)
        {
            if (!r.IsFolded) continue;
            if (line > r.StartLine && line <= r.EndLine) return false;
        }
        return true;
    }

    /// <summary>Returns <see langword="true"/> when the line is hidden by a fold.</summary>
    public bool IsLineFolded(int line) => !IsLineVisible(line);

    /// <summary>
    /// Returns an array of all document line indices that are currently
    /// visible, in ascending order.
    /// </summary>
    public int[] GetVisibleLines()
    {
        var result = new List<int>(_doc.LineCount);
        for (int i = 0; i < _doc.LineCount; i++)
            if (IsLineVisible(i)) result.Add(i);
        return result.ToArray();
    }

    /// <summary>
    /// The number of lines visible in the current fold state.
    /// Correctly handles overlapping or nested folded regions.
    /// </summary>
    public int VisibleLineCount
    {
        get
        {
            int hidden   = 0;
            int coverEnd = -1;

            foreach (var r in _regions.Where(r => r.IsFolded)
                                      .OrderBy(r => r.StartLine))
            {
                int from = r.StartLine + 1;
                int to   = r.EndLine;
                if (from > to) continue;

                if (from > coverEnd + 1)
                {
                    hidden  += to - from + 1;
                    coverEnd = to;
                }
                else if (to > coverEnd)
                {
                    hidden  += to - coverEnd;
                    coverEnd = to;
                }
            }

            return _doc.LineCount - hidden;
        }
    }

    // ── Display-line mapping ──────────────────────────────────────────────

    /// <summary>
    /// Maps a document line index to its display (visible) row index (0-based).
    /// Returns <c>-1</c> when the line is hidden by a folded region.
    /// </summary>
    public int ToDisplayLine(int documentLine)
    {
        if (!IsLineVisible(documentLine)) return -1;

        int hiddenBefore = 0;
        int coverEnd     = -1;

        foreach (var r in _regions)
        {
            if (!r.IsFolded) continue;
            if (r.StartLine >= documentLine) break;

            int from = r.StartLine + 1;
            int to   = Math.Min(r.EndLine, documentLine - 1);
            if (from > to) continue;

            if (from > coverEnd + 1)
            {
                hiddenBefore += to - from + 1;
                coverEnd      = to;
            }
            else if (to > coverEnd)
            {
                hiddenBefore += to - coverEnd;
                coverEnd      = to;
            }
        }

        return documentLine - hiddenBefore;
    }

    /// <summary>
    /// Maps a display row index to the document line it represents.
    /// Returns <c>-1</c> when out of range.
    /// </summary>
    public int ToDocumentLine(int displayLine)
    {
        if (displayLine < 0) return -1;

        int display = 0;
        for (int i = 0; i < _doc.LineCount; i++)
        {
            if (!IsLineVisible(i)) continue;
            if (display == displayLine) return i;
            display++;
        }

        return -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private FoldRegion? FindRegion(int startLine)
    {
        int lo = 0, hi = _regions.Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int cmp = _regions[mid].StartLine.CompareTo(startLine);
            if (cmp == 0) return _regions[mid];
            if (cmp < 0) lo = mid + 1; else hi = mid - 1;
        }
        return null;
    }

    /// <summary>
    /// Rebuild <see cref="_folded"/> from the current region list.
    /// Called after <see cref="OnInsert"/> and <see cref="OnDelete"/> change
    /// <see cref="FoldRegion.StartLine"/> values in-place.
    /// </summary>
    private void RebuildFoldedSet()
    {
        _folded.Clear();
        foreach (var r in _regions)
            if (r.IsFolded) _folded.Add(r.StartLine);
    }
}
