namespace TextAPI.Core.Folding;

/// <summary>
/// A contiguous range of document lines that can be collapsed (folded) in an
/// editor view.
///
/// When folded, only <see cref="StartLine"/> is visible; lines
/// <c>StartLine+1</c> through <see cref="EndLine"/> are hidden and replaced
/// by the <see cref="Label"/> placeholder.
/// </summary>
public sealed class FoldRegion
{
    internal FoldRegion(int startLine, int endLine, string label)
    {
        StartLine = startLine;
        EndLine   = endLine;
        Label     = label;
    }

    /// <summary>First line of the region (always visible; contains the opening <c>{</c>).</summary>
    public int StartLine { get; internal set; }

    /// <summary>Last line of the region (hidden when folded; contains the closing <c>}</c>).</summary>
    public int EndLine   { get; internal set; }

    /// <summary>
    /// Placeholder text shown in the editor gutter when the region is folded
    /// (e.g. the first line of content trimmed, or <c>{ … }</c>).
    /// </summary>
    public string Label  { get; }

    /// <summary>Whether this region is currently collapsed.</summary>
    public bool IsFolded { get; internal set; }

    /// <summary>Total number of lines in the region (inclusive of both endpoints).</summary>
    public int LineCount => EndLine - StartLine + 1;

    /// <summary>
    /// Number of lines hidden when the region is folded
    /// (= <see cref="LineCount"/> − 1, since <see cref="StartLine"/> is
    /// always visible).
    /// </summary>
    public int HiddenLineCount => EndLine - StartLine;

    /// <inheritdoc/>
    public override string ToString() =>
        $"FoldRegion [{StartLine}–{EndLine}] {(IsFolded ? "folded" : "open")} \"{Label}\"";
}
