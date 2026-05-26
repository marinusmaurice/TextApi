using TextAPI.Core.Diff;

namespace TextAPI.Core.ChangeTracking;

/// <summary>Per-line change status relative to the last saved baseline.</summary>
public enum LineStatus
{
    /// <summary>Line content is identical to the baseline.</summary>
    Clean,

    /// <summary>Line exists in the current document but not in the baseline (newly inserted).</summary>
    Added,

    /// <summary>Line exists in both baseline and current document but content has changed.</summary>
    Modified,
}

/// <summary>
/// Tracks per-line change status (Added / Modified / Clean) relative to a saved baseline,
/// and records where baseline lines have been deleted.
///
/// Works like the gutter change bar in VS Code / JetBrains IDEs:
///   ▌ green  = line was added since baseline
///   ▌ yellow = line was modified since baseline
///   ◂ red    = one or more baseline lines were deleted at this position
///
/// Design
/// ──────
///   The baseline is a snapshot of the document's lines at the time of
///   the last Load, Save, or explicit <see cref="SetBaseline"/> call.
///
///   Status is computed lazily: <see cref="GetStatus"/> triggers a Myers
///   line-level diff (<see cref="TextDiff.Diff(string[], string[])"/>)
///   between the baseline and the current document only when the document
///   has been mutated since the last query.
///
///   Consecutive Delete+Insert diff hunks are interpreted as "Modified"
///   lines (content replaced), matching VS Code's colour convention.
///
/// <see cref="TextDocument"/> integration
/// ───────────────────────────────────────
///   <see cref="TextDocument.GetChangeTracker()"/> returns the lazy-init
///   instance.  <see cref="TextDocument"/> calls <see cref="Invalidate"/>
///   after every edit and after Undo/Redo, and calls
///   <see cref="SetBaseline"/> after Load and Save.
/// </summary>
public sealed class ChangeTracker
{
    private readonly TextDocument _doc;

    // Baseline snapshot (empty = no baseline set yet)
    private string[] _baselineLines = [];

    // Lazily-computed diff results
    private LineStatus[] _statuses     = [];
    private bool[]       _deletedAbove = []; // [i] = true → baseline lines deleted just before current line i
    private bool         _isDirty      = true;

    /// <summary>Fired after the internal change map is recomputed (on any mutation).</summary>
    public event EventHandler? ChangesUpdated;

    internal ChangeTracker(TextDocument doc)
    {
        _doc = doc;
        // Capture the document's current state as the baseline immediately,
        // so the tracker is usable even when accessed after Load() has already run.
        SetBaseline();
    }

    // ── Baseline management ───────────────────────────────────────────────

    /// <summary>
    /// Capture the current document content as the new baseline.
    /// All lines immediately become <see cref="LineStatus.Clean"/> and
    /// <see cref="HasDeletionAbove"/> returns <see langword="false"/> for every line.
    /// Call this after <c>Load</c> or <c>Save</c> (TextDocument does this automatically).
    /// </summary>
    public void SetBaseline()
    {
        int n = _doc.LineCount;
        _baselineLines = new string[n];
        for (int i = 0; i < n; i++)
            _baselineLines[i] = _doc.GetLine(i);

        // After a fresh baseline every line is clean — skip the diff.
        _statuses     = new LineStatus[n];   // all LineStatus.Clean (value 0)
        _deletedAbove = new bool[n + 1];     // all false
        _isDirty      = false;

        ChangesUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── Status queries ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the change status of <paramref name="lineIndex"/> relative to the baseline.
    /// Triggers a lazy diff recompute if the document has changed since the last call.
    /// </summary>
    public LineStatus GetStatus(int lineIndex)
    {
        if (_isDirty) Recompute();
        return (uint)lineIndex < (uint)_statuses.Length
            ? _statuses[lineIndex]
            : LineStatus.Clean;
    }

    /// <summary>
    /// Returns <see langword="true"/> when one or more baseline lines were deleted
    /// immediately above (before) <paramref name="lineIndex"/> in the current document.
    ///
    /// A UI renderer can show a small red triangle / deletion chevron at this position.
    /// <paramref name="lineIndex"/> == 0 means deletions occurred at the very top of the document.
    /// <paramref name="lineIndex"/> == <see cref="TextDocument.LineCount"/> means deletions at the bottom.
    /// </summary>
    public bool HasDeletionAbove(int lineIndex)
    {
        if (_isDirty) Recompute();
        return (uint)lineIndex < (uint)_deletedAbove.Length && _deletedAbove[lineIndex];
    }

    /// <summary>
    /// Enumerates all current line indices whose status is not <see cref="LineStatus.Clean"/>.
    /// </summary>
    public IEnumerable<int> ChangedLines()
    {
        if (_isDirty) Recompute();
        for (int i = 0; i < _statuses.Length; i++)
            if (_statuses[i] != LineStatus.Clean)
                yield return i;
    }

    /// <summary>
    /// Enumerates all positions (0-based current-line index) where deleted baseline lines exist.
    /// Index 0 means something was deleted before the first current line.
    /// Index equal to <see cref="TextDocument.LineCount"/> means deletions at the end.
    /// </summary>
    public IEnumerable<int> DeletionPoints()
    {
        if (_isDirty) Recompute();
        for (int i = 0; i < _deletedAbove.Length; i++)
            if (_deletedAbove[i])
                yield return i;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the document differs from the baseline in any way
    /// (any Added, Modified, or deleted lines).
    /// </summary>
    public bool HasAnyChanges
    {
        get
        {
            if (_isDirty) Recompute();
            if (_statuses.Any(s => s != LineStatus.Clean)) return true;
            if (_deletedAbove.Any(d => d)) return true;
            return false;
        }
    }

    // ── Invalidation (called by TextDocument) ─────────────────────────────

    /// <summary>
    /// Mark the cached diff as stale.  The next query will trigger a recompute.
    /// Called by <see cref="TextDocument"/> after every Insert, Delete, Replace,
    /// Undo, Redo, or ExecuteComposite.
    /// </summary>
    internal void Invalidate()
    {
        _isDirty = true;
        ChangesUpdated?.Invoke(this, EventArgs.Empty);
    }

    // ── Core diff-based recompute ─────────────────────────────────────────

    private void Recompute()
    {
        int currentCount = _doc.LineCount;
        string[] current = new string[currentCount];
        for (int i = 0; i < currentCount; i++)
            current[i] = _doc.GetLine(i);

        _statuses     = new LineStatus[currentCount]; // all Clean by default
        _deletedAbove = new bool[currentCount + 1];   // all false by default

        // No baseline → treat everything as clean (doc loaded without explicit baseline)
        if (_baselineLines.Length == 0)
        {
            _isDirty = false;
            return;
        }

        var diffResult = TextDiff.Diff(_baselineLines, current);
        var hunks      = diffResult.Hunks;

        int h = 0;
        while (h < hunks.Count)
        {
            var hunk = hunks[h];

            switch (hunk.Kind)
            {
                case DiffKind.Equal:
                    // Lines identical to baseline → remain Clean.
                    h++;
                    break;

                case DiffKind.Delete:
                {
                    // Check whether the next hunk is an Insert (Delete+Insert = Modified region).
                    if (h + 1 < hunks.Count && hunks[h + 1].Kind == DiffKind.Insert)
                    {
                        var ins      = hunks[h + 1];
                        int modCount = Math.Min(hunk.OldCount, ins.NewCount);

                        // First modCount inserted lines replace deleted lines → Modified.
                        for (int i = 0; i < modCount; i++)
                            _statuses[ins.NewStart + i] = LineStatus.Modified;

                        // Extra inserted lines (more new than old) → Added.
                        for (int i = modCount; i < ins.NewCount; i++)
                            _statuses[ins.NewStart + i] = LineStatus.Added;

                        // Extra deleted lines (more old than new) → deletion marker
                        // placed after the last inserted line.
                        if (hunk.OldCount > ins.NewCount)
                        {
                            int marker = ins.NewStart + ins.NewCount;
                            if ((uint)marker < (uint)_deletedAbove.Length)
                                _deletedAbove[marker] = true;
                        }

                        h += 2; // consumed Delete + Insert
                    }
                    else
                    {
                        // Pure deletion: mark deletion point at the insertion position
                        // in the current document (hunk.NewStart = where deleted lines "were").
                        int marker = hunk.NewStart;
                        if ((uint)marker < (uint)_deletedAbove.Length)
                            _deletedAbove[marker] = true;
                        h++;
                    }
                    break;
                }

                case DiffKind.Insert:
                {
                    // Pure insertion (not preceded by a Delete in this pass) → Added.
                    for (int i = 0; i < hunk.NewCount; i++)
                        _statuses[hunk.NewStart + i] = LineStatus.Added;
                    h++;
                    break;
                }

                default:
                    h++;
                    break;
            }
        }

        _isDirty = false;
    }
}
