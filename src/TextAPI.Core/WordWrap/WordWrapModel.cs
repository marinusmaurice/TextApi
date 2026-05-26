using System.Collections.Generic;

namespace TextAPI.Core.WordWrap;

/// <summary>
/// Computes how many display rows each document line occupies given a viewport
/// width (in columns), and maps between document lines and wrapped display rows.
///
/// Uses East Asian Width-aware column counting via
/// <see cref="Language.GraphemeHelper.DisplayWidth"/>.
///
/// The model is lazy: layout is recomputed only when accessed after being
/// invalidated (by a document edit, Resize, or explicit Invalidate call).
/// </summary>
public sealed class WordWrapModel
{
    private readonly TextDocument _doc;
    private          int          _viewportWidth;

    // _rowStarts[i]  = first display row of document line i
    // _rowStarts[LineCount] = total display rows
    private int[]  _rowStarts = [];
    private bool   _isDirty   = true;

    /// <summary>Fired when the wrap layout changes (Resize or Invalidate).</summary>
    public event EventHandler? WrapChanged;

    // ── Construction ──────────────────────────────────────────────────────

    /// <param name="doc">The document to wrap.</param>
    /// <param name="viewportWidth">Viewport width in columns (clamped to ≥ 1).</param>
    public WordWrapModel(TextDocument doc, int viewportWidth)
    {
        _doc           = doc;
        _viewportWidth = Math.Max(1, viewportWidth);
    }

    // ── Configuration ─────────────────────────────────────────────────────

    /// <summary>Current viewport width in columns (always ≥ 1).</summary>
    public int ViewportWidth => _viewportWidth;

    /// <summary>
    /// Change the viewport width.  Invalidates the cached layout and fires
    /// <see cref="WrapChanged"/>.  No-op when <paramref name="newWidth"/> equals
    /// the current width (after clamping).
    /// </summary>
    public void Resize(int newWidth)
    {
        int clamped = Math.Max(1, newWidth);
        if (clamped == _viewportWidth) return;
        _viewportWidth = clamped;
        Invalidate();
    }

    // ── Layout queries ────────────────────────────────────────────────────

    /// <summary>Total number of wrapped display rows across all document lines.</summary>
    public int DisplayRowCount
    {
        get
        {
            EnsureUpToDate();
            return _rowStarts.Length == 0 ? 1 : _rowStarts[^1];
        }
    }

    /// <summary>
    /// Returns the first display row (0-based) occupied by <paramref name="docLine"/>.
    /// Returns 0 for out-of-range lines.
    /// </summary>
    public int ToDisplayRow(int docLine)
    {
        EnsureUpToDate();
        if (docLine < 0 || docLine >= _doc.LineCount) return 0;
        return _rowStarts[docLine];
    }

    /// <summary>
    /// Returns which document line contains <paramref name="displayRow"/>.
    /// Uses binary search on the <c>_rowStarts</c> array.
    /// Returns the last line for out-of-range display rows.
    /// </summary>
    public int ToDocumentLine(int displayRow)
    {
        EnsureUpToDate();
        if (_rowStarts.Length <= 1) return 0;

        int lineCount = _doc.LineCount;
        // Clamp to valid range: last line for anything at or past the end
        if (displayRow >= _rowStarts[lineCount]) return lineCount - 1;
        if (displayRow <= 0) return 0;

        // Binary search: find last i where _rowStarts[i] <= displayRow
        int lo = 0, hi = lineCount - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (_rowStarts[mid] <= displayRow) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    /// <summary>
    /// How many display rows does <paramref name="docLine"/> occupy? Always ≥ 1.
    /// </summary>
    public int WrappedRowCount(int docLine)
    {
        EnsureUpToDate();
        if (docLine < 0 || docLine >= _doc.LineCount) return 1;
        return _rowStarts[docLine + 1] - _rowStarts[docLine];
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="docLine"/> occupies
    /// more than one display row.
    /// </summary>
    public bool IsWrapped(int docLine) => WrappedRowCount(docLine) > 1;

    /// <summary>
    /// Returns the list of (startCharOffset, endCharOffset) pairs for each visual
    /// row of <paramref name="docLine"/>.  The offsets are char positions within
    /// the line string (not document offsets).
    ///
    /// For an empty line, returns a single segment <c>(0, 0)</c>.
    /// The last segment's End equals <c>line.Length</c>.
    /// Consecutive segments are contiguous: <c>segs[i].End == segs[i+1].Start</c>.
    /// </summary>
    public IReadOnlyList<(int Start, int End)> GetWrappedSegments(int docLine)
    {
        string line = _doc.GetLine(docLine);
        if (line.Length == 0) return [(0, 0)];

        var     segments = new List<(int, int)>();
        int     segStart = 0, colUsed = 0, offset = 0;
        var     span     = line.AsSpan();

        while (offset < line.Length)
        {
            int next = Language.GraphemeHelper.NextCluster(span, offset);
            int w    = Language.GraphemeHelper.DisplayWidth(span[offset..next]);

            if (w == 0)
            {
                // Combining marks / ZWJ etc. — attach to current row without advancing column
                offset = next;
                continue;
            }

            if (colUsed + w > _viewportWidth)
            {
                // Start a new visual row
                segments.Add((segStart, offset));
                segStart = offset;
                colUsed  = w;
            }
            else
            {
                colUsed += w;
            }

            offset = next;
        }

        // Final segment
        segments.Add((segStart, line.Length));
        return segments;
    }

    // ── Internal invalidation API (called by TextDocument) ────────────────

    /// <summary>Called by TextDocument.Insert to invalidate the layout.</summary>
    internal void OnInsert(int offset, int insertedNewlines) => Invalidate();

    /// <summary>Called by TextDocument.Delete to invalidate the layout.</summary>
    internal void OnDelete(int offset, int deletedNewlines) => Invalidate();

    /// <summary>
    /// Marks the layout dirty and fires <see cref="WrapChanged"/>.
    /// Called by TextDocument on any structural edit (Replace, Undo, Redo, Load, …).
    /// </summary>
    internal void Invalidate()
    {
        _isDirty = true;
        WrapChanged?.Invoke(this, EventArgs.Empty);
    }

    // ── Lazy recomputation ────────────────────────────────────────────────

    private void EnsureUpToDate()
    {
        if (!_isDirty) return;

        int lineCount = _doc.LineCount;
        _rowStarts    = new int[lineCount + 1];
        _rowStarts[0] = 0;

        for (int i = 0; i < lineCount; i++)
        {
            string line        = _doc.GetLine(i);
            _rowStarts[i + 1]  = _rowStarts[i] + ComputeRowCount(line.AsSpan(), _viewportWidth);
        }

        _isDirty = false;
    }

    /// <summary>
    /// Compute the number of display rows a single line occupies at
    /// <paramref name="viewportWidth"/> columns.  Always returns ≥ 1.
    /// </summary>
    private static int ComputeRowCount(ReadOnlySpan<char> line, int viewportWidth)
    {
        if (line.IsEmpty) return 1;

        int rows = 1, colUsed = 0, offset = 0;

        while (offset < line.Length)
        {
            int next = Language.GraphemeHelper.NextCluster(line, offset);
            int w    = Language.GraphemeHelper.DisplayWidth(line[offset..next]);

            if (w == 0)
            {
                // Combining marks etc. — no column advance
                offset = next;
                continue;
            }

            if (colUsed + w > viewportWidth)
            {
                // Overflow: start a new row.  The cluster that caused the overflow
                // goes into the new row even if its width exceeds the viewport.
                rows++;
                colUsed = w;
            }
            else
            {
                colUsed += w;
            }

            offset = next;
        }

        return rows;
    }
}
