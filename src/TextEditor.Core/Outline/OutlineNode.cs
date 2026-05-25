namespace TextEditor.Core.Outline;

/// <summary>
/// A single node in a document outline tree.
///
/// Each node represents one <see cref="Folding.FoldRegion"/> from the
/// <see cref="Folding.FoldingModel"/>.  Nodes are nested to reflect
/// containment: if region B is fully contained within region A then B is
/// a child of A in the tree.
/// </summary>
public sealed class OutlineNode
{
    private readonly List<OutlineNode> _children = [];

    internal OutlineNode(string label, int startLine, int endLine, int depth)
    {
        Label     = label;
        StartLine = startLine;
        EndLine   = endLine;
        Depth     = depth;
    }

    /// <summary>Header text (from <see cref="Folding.FoldRegion.Label"/>).</summary>
    public string Label { get; }

    /// <summary>Zero-based document line of the scope header.</summary>
    public int StartLine { get; }

    /// <summary>Zero-based document line of the scope closing boundary.</summary>
    public int EndLine { get; }

    /// <summary>
    /// Nesting depth: 0 for top-level nodes, 1 for their direct children, etc.
    /// </summary>
    public int Depth { get; }

    /// <summary>Direct children of this node, in ascending <see cref="StartLine"/> order.</summary>
    public IReadOnlyList<OutlineNode> Children => _children;

    internal void AddChild(OutlineNode child) => _children.Add(child);

    /// <inheritdoc/>
    public override string ToString() =>
        $"{new string(' ', Depth * 2)}{Label}  [{StartLine}–{EndLine}]";
}
