using TextEditor.Core.Folding;

namespace TextEditor.Core.Outline;

/// <summary>
/// Builds a document outline tree from an existing <see cref="FoldingModel"/>.
///
/// <para>
/// No new parsing is performed — the outline is a purely structural
/// projection of the fold regions already detected by the folding model.
/// A region B is a child of region A when B is fully contained within A
/// (<c>A.StartLine &lt; B.StartLine &amp;&amp; B.EndLine ≤ A.EndLine</c>).
/// </para>
///
/// <para>
/// The algorithm runs in O(n) time using a stack that tracks the current
/// ancestor chain.  Regions are already sorted ascending by
/// <c>StartLine</c> by <see cref="FoldingModel.UpdateRegions"/>, so no
/// additional sort is needed.
/// </para>
/// </summary>
public static class OutlineProvider
{
    /// <summary>
    /// Build the outline tree for <paramref name="model"/> and return the
    /// top-level nodes in ascending <c>StartLine</c> order.
    ///
    /// Returns an empty list when the model has no regions.
    /// </summary>
    public static IReadOnlyList<OutlineNode> GetOutline(FoldingModel model)
    {
        var regions = model.Regions;
        if (regions.Count == 0) return [];

        var roots = new List<OutlineNode>();

        // Stack of open ancestors.  Top of stack = innermost open scope.
        var stack = new Stack<OutlineNode>();

        foreach (var region in regions)
        {
            // Pop ancestors that ended before this region starts.
            while (stack.Count > 0 && stack.Peek().EndLine < region.StartLine)
                stack.Pop();

            int depth = stack.Count;
            var node  = new OutlineNode(region.Label, region.StartLine, region.EndLine, depth);

            if (stack.Count == 0)
                roots.Add(node);
            else
                stack.Peek().AddChild(node);

            stack.Push(node);
        }

        return roots;
    }
}
