using TextEditor.Core.Folding;

namespace TextEditor.Core.StickyScroll;

/// <summary>
/// One entry in the sticky-scroll context bar — a scope header that has
/// scrolled above the viewport but whose body still contains the first
/// visible line.
/// </summary>
/// <param name="Label">
/// The header text to display (taken from <see cref="FoldRegion.Label"/>).
/// </param>
/// <param name="DocumentLine">
/// Zero-based document line number of the header (= <see cref="FoldRegion.StartLine"/>).
/// </param>
public readonly record struct StickyScrollEntry(string Label, int DocumentLine);

/// <summary>
/// Computes the sticky-scroll context for a given viewport position.
///
/// <para>
/// Works identically to the VS Code "Sticky Scroll" feature: when you
/// scroll down past a class or method header, the header "sticks" to the
/// top of the viewport so you always know which scope you are reading.
/// </para>
///
/// <para>
/// Algorithm: collect every <see cref="FoldRegion"/> whose
/// <c>StartLine &lt; firstVisibleLine</c> (header has scrolled above)
/// <em>and</em> <c>EndLine ≥ firstVisibleLine</c> (body still contains
/// the viewport). Sort ascending by <c>StartLine</c> so the outermost
/// scope is first (index 0).
/// </para>
/// </summary>
public static class StickyScroll
{
    /// <summary>
    /// Returns the chain of scope headers that are "stuck" at the top of the
    /// viewport when the first visible line is <paramref name="firstVisibleLine"/>.
    ///
    /// <list type="bullet">
    ///   <item>When <paramref name="firstVisibleLine"/> is 0 the viewport is at
    ///   the top — result is always empty.</item>
    ///   <item>When <paramref name="firstVisibleLine"/> falls exactly on a region's
    ///   <c>StartLine</c> the header is still visible in the viewport and is
    ///   <b>not</b> included in the context.</item>
    ///   <item>Regions are returned outermost-first (ascending <c>StartLine</c>).</item>
    /// </list>
    /// </summary>
    /// <param name="foldingModel">
    /// The folding model whose regions define scope boundaries.
    /// </param>
    /// <param name="firstVisibleLine">
    /// Zero-based index of the first document line currently visible in the viewport.
    /// </param>
    public static IReadOnlyList<StickyScrollEntry> GetContext(
        FoldingModel foldingModel, int firstVisibleLine)
    {
        if (firstVisibleLine <= 0) return [];

        List<StickyScrollEntry>? result = null;

        foreach (var region in foldingModel.Regions)
        {
            // Header must have scrolled above (StartLine < firstVisibleLine)
            // and the region's body must still contain the viewport
            // (EndLine >= firstVisibleLine).
            if (region.StartLine < firstVisibleLine && region.EndLine >= firstVisibleLine)
            {
                result ??= [];
                result.Add(new StickyScrollEntry(region.Label, region.StartLine));
            }
        }

        if (result is null) return [];

        // Sort ascending by StartLine so outermost scope (lowest line) is first.
        result.Sort((a, b) => a.DocumentLine.CompareTo(b.DocumentLine));
        return result;
    }
}
