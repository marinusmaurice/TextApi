namespace TextEditor.Core.Buffer;

/// <summary>
/// Augmented Red-Black tree that stores Pieces and supports O(log n)
/// seek-by-character-offset and seek-by-line-number via subtree metadata.
/// Rotations update SubtreeCharCount and SubtreeLineFeedCount automatically.
/// </summary>
internal sealed class PieceRbTree
{
    private readonly RbTreeNode _nil  = RbTreeNode.CreateNil();
    private          RbTreeNode _root;

    internal RbTreeNode Nil  => _nil;
    internal RbTreeNode Root => _root;

    internal PieceRbTree()
    {
        _root = _nil;
    }

    /// <summary>Reset the tree to empty (used by Compact).</summary>
    internal void Reset() { _root = _nil; }

    // ── Public query API ──────────────────────────────────────────────────

    /// <summary>Total character count of the whole document.</summary>
    internal int TotalCharCount => _root.IsNil ? 0 : _root.SubtreeCharCount;

    /// <summary>Total line count (number of \n chars + 1).</summary>
    internal int TotalLineCount => _root.IsNil ? 1 : _root.SubtreeLineFeedCount + 1;

    /// <summary>
    /// Find the node that contains the given zero-based character offset.
    /// Returns the node and the offset's position within that node's piece.
    /// </summary>
    internal (RbTreeNode Node, int OffsetInPiece) FindNodeByCharOffset(int charOffset)
    {
        var node = _root;
        while (!node.IsNil)
        {
            int leftCount = node.Left.IsNil ? 0 : node.Left.SubtreeCharCount;
            if (charOffset < leftCount)
            {
                node = node.Left!;
            }
            else
            {
                charOffset -= leftCount;
                if (charOffset < node.Length)
                    return (node, charOffset);
                charOffset -= node.Length;
                node = node.Right!;
            }
        }
        // Offset is at or past end of document — return last node
        return (GetMaxNode(), 0);
    }

    /// <summary>
    /// Find the node that contains the start of the given zero-based line number.
    /// Returns the node and the character offset of line start within that node.
    /// </summary>
    internal (RbTreeNode Node, int LineStartOffsetInPiece) FindNodeByLine(int lineIndex)
    {
        if (lineIndex <= 0) return (GetMinNode(), 0);

        var node = _root;
        int remaining = lineIndex;

        while (!node.IsNil)
        {
            int leftLines = node.Left.IsNil ? 0 : node.Left.SubtreeLineFeedCount;
            if (remaining <= leftLines)
            {
                node = node.Left!;
            }
            else
            {
                remaining -= leftLines;
                if (remaining <= node.LineFeedCount)
                {
                    // The line start is inside this node — find the char offset
                    return (node, FindLineStartInPiece(node, remaining));
                }
                remaining -= node.LineFeedCount;
                node = node.Right!;
            }
        }
        return (GetMaxNode(), 0);
    }

    // ── Insert ────────────────────────────────────────────────────────────

    /// <summary>Insert a new piece node after the given node (or as root if tree is empty).</summary>
    internal RbTreeNode InsertAfter(RbTreeNode? after, RbTreeNode newNode)
    {
        newNode.Left   = _nil;
        newNode.Right  = _nil;
        newNode.Colour = NodeColour.Red;

        if (_root.IsNil)
        {
            _root          = newNode;
            newNode.Parent = _nil;
            SetPieceLinks(null, newNode, null);
        }
        else if (after == null || after.IsNil)
        {
            // Insert before the minimum
            var min = GetMinNode();
            min.Left       = newNode;
            newNode.Parent = min;
            SetPieceLinks(null, newNode, min);
        }
        else
        {
            if (after.Right != null && !after.Right.IsNil)
            {
                var successor = GetMinNode(after.Right);
                successor.Left  = newNode;
                newNode.Parent  = successor;
                SetPieceLinks(after, newNode, successor);
            }
            else
            {
                after.Right    = newNode;
                newNode.Parent = after;
                SetPieceLinks(after, newNode, after.NextPiece);
            }
        }

        InsertFixup(newNode);
        UpdateAncestors(newNode);
        return newNode;
    }

    /// <summary>Delete a node from the tree.</summary>
    /// <summary>
    /// Delete a node from the RB tree (structural operation only).
    /// Boundary-tag links are NOT maintained here — PieceTable.RebuildPieceLinks()
    /// rebuilds them from InOrder after every Delete batch.
    /// </summary>
    internal void Delete(RbTreeNode node)
    {
        RbTreeNode fixupNode;
        RbTreeNode fixupParent;
        NodeColour originalColour = node.Colour;

        if (node.Left == null || node.Left.IsNil)
        {
            fixupNode   = node.Right ?? _nil;
            fixupParent = node.Parent ?? _nil;
            Transplant(node, fixupNode);
        }
        else if (node.Right == null || node.Right.IsNil)
        {
            fixupNode   = node.Left;
            fixupParent = node.Parent ?? _nil;
            Transplant(node, fixupNode);
        }
        else
        {
            var successor  = GetMinNode(node.Right);
            originalColour = successor.Colour;
            fixupNode      = successor.Right ?? _nil;

            if (successor.Parent == node)
            {
                fixupParent      = successor;
                fixupNode.Parent = successor;
            }
            else
            {
                fixupParent = successor.Parent ?? _nil;
                Transplant(successor, fixupNode);
                successor.Right = node.Right;
                if (successor.Right != null && !successor.Right.IsNil)
                    successor.Right.Parent = successor;
            }

            Transplant(node, successor);
            successor.Left = node.Left;
            if (successor.Left != null && !successor.Left.IsNil)
                successor.Left.Parent = successor;
            successor.Colour = node.Colour;
            successor.UpdateMetadata(_nil);
        }

        if (fixupNode.IsNil) fixupNode.Parent = fixupParent;
        if (originalColour == NodeColour.Black) DeleteFixup(fixupNode);
        UpdateAncestors(fixupNode.IsNil ? fixupParent : fixupNode);
    }

    private void Transplant(RbTreeNode u, RbTreeNode v)
    {
        if (u.Parent == null || u.Parent.IsNil)
            _root = v;
        else if (u == u.Parent.Left)
            u.Parent.Left = v;
        else
            u.Parent.Right = v;
        v.Parent = u.Parent;
    }

    // ── In-order iteration ────────────────────────────────────────────────

    internal IEnumerable<RbTreeNode> InOrder()
    {
        var stack = new Stack<RbTreeNode>();
        var cur   = _root;
        while (!cur.IsNil || stack.Count > 0)
        {
            while (!cur.IsNil) { stack.Push(cur); cur = cur.Left!; }
            cur = stack.Pop();
            yield return cur;
            cur = cur.Right!;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private RbTreeNode GetMinNode() => GetMinNode(_root);
    private RbTreeNode GetMinNode(RbTreeNode node)
    {
        while (!node.Left.IsNil) node = node.Left!;
        return node;
    }

    private RbTreeNode GetMaxNode()
    {
        var node = _root;
        while (!node.Right.IsNil) node = node.Right!;
        return node;
    }

    private static void SetPieceLinks(RbTreeNode? prev, RbTreeNode node, RbTreeNode? next)
    {
        node.PrevPiece = prev;
        node.NextPiece = next;
        if (prev  != null && !prev.IsNil)  prev.NextPiece  = node;
        if (next  != null && !next.IsNil)  next.PrevPiece  = node;
    }

    private void UpdateAncestors(RbTreeNode node)
    {
        while (!node.IsNil)
        {
            node.UpdateMetadata(_nil);
            node = node.Parent!;
        }
    }

    private static int FindLineStartInPiece(RbTreeNode node, int nthFeed)
    {
        // Not used for actual buffer reads here; returns the piece-internal
        // char offset just after the nth \n — the caller maps to buffer position.
        // Actual text read happens in PieceTable via buffer access.
        return nthFeed; // placeholder — PieceTable resolves against buffer
    }

    // ── RB fixups ─────────────────────────────────────────────────────────

    private void InsertFixup(RbTreeNode z)
    {
        while (z.Parent?.Colour == NodeColour.Red)
        {
            if (z.Parent == z.Parent.Parent?.Left)
            {
                var y = z.Parent.Parent!.Right!;
                if (y.Colour == NodeColour.Red)
                {
                    z.Parent.Colour        = NodeColour.Black;
                    y.Colour               = NodeColour.Black;
                    z.Parent.Parent.Colour = NodeColour.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Right)
                    {
                        z = z.Parent;
                        RotateLeft(z);
                    }
                    z.Parent!.Colour        = NodeColour.Black;
                    z.Parent.Parent!.Colour = NodeColour.Red;
                    RotateRight(z.Parent.Parent);
                }
            }
            else
            {
                var y = z.Parent!.Parent?.Left!;
                if (y?.Colour == NodeColour.Red)
                {
                    z.Parent.Colour        = NodeColour.Black;
                    y.Colour               = NodeColour.Black;
                    z.Parent.Parent!.Colour = NodeColour.Red;
                    z = z.Parent.Parent;
                }
                else
                {
                    if (z == z.Parent.Left)
                    {
                        z = z.Parent;
                        RotateRight(z);
                    }
                    z.Parent!.Colour        = NodeColour.Black;
                    z.Parent.Parent!.Colour = NodeColour.Red;
                    RotateLeft(z.Parent.Parent!);
                }
            }
        }
        _root.Colour = NodeColour.Black;
    }

    private void DeleteFixup(RbTreeNode x)
    {
        while (x != _root && x.Colour == NodeColour.Black)
        {
            if (x.Parent == null || x.Parent.IsNil) break;

            if (x == x.Parent.Left)
            {
                var w = x.Parent.Right ?? _nil;
                if (w.IsNil) { x = x.Parent; continue; }

                if (w.Colour == NodeColour.Red)
                {
                    w.Colour        = NodeColour.Black;
                    x.Parent.Colour = NodeColour.Red;
                    RotateLeft(x.Parent);
                    w = x.Parent.Right ?? _nil;
                }
                var wLeft  = w.Left  ?? _nil;
                var wRight = w.Right ?? _nil;
                if (wLeft.Colour == NodeColour.Black && wRight.Colour == NodeColour.Black)
                {
                    w.Colour = NodeColour.Red;
                    x = x.Parent;
                }
                else
                {
                    if (wRight.Colour == NodeColour.Black)
                    {
                        if (!wLeft.IsNil) wLeft.Colour = NodeColour.Black;
                        w.Colour = NodeColour.Red;
                        RotateRight(w);
                        w = x.Parent!.Right ?? _nil;
                    }
                    w.Colour           = x.Parent!.Colour;
                    x.Parent.Colour    = NodeColour.Black;
                    if (w.Right != null && !w.Right.IsNil) w.Right.Colour = NodeColour.Black;
                    RotateLeft(x.Parent);
                    x = _root;
                }
            }
            else
            {
                var w = x.Parent.Left ?? _nil;
                if (w.IsNil) { x = x.Parent; continue; }

                if (w.Colour == NodeColour.Red)
                {
                    w.Colour        = NodeColour.Black;
                    x.Parent.Colour = NodeColour.Red;
                    RotateRight(x.Parent);
                    w = x.Parent.Left ?? _nil;
                }
                var wRight = w.Right ?? _nil;
                var wLeft  = w.Left  ?? _nil;
                if (wRight.Colour == NodeColour.Black && wLeft.Colour == NodeColour.Black)
                {
                    w.Colour = NodeColour.Red;
                    x = x.Parent;
                }
                else
                {
                    if (wLeft.Colour == NodeColour.Black)
                    {
                        if (!wRight.IsNil) wRight.Colour = NodeColour.Black;
                        w.Colour = NodeColour.Red;
                        RotateLeft(w);
                        w = x.Parent!.Left ?? _nil;
                    }
                    w.Colour           = x.Parent!.Colour;
                    x.Parent.Colour    = NodeColour.Black;
                    if (w.Left != null && !w.Left.IsNil) w.Left.Colour = NodeColour.Black;
                    RotateRight(x.Parent);
                    x = _root;
                }
            }
        }
        x.Colour = NodeColour.Black;
    }

    private void RotateLeft(RbTreeNode x)
    {
        var y = x.Right ?? _nil;
        if (y.IsNil) return;   // nothing to rotate to
        x.Right = y.Left ?? _nil;
        if (!(y.Left ?? _nil).IsNil) y.Left!.Parent = x;
        y.Parent = x.Parent;
        if (x.Parent == null || x.Parent.IsNil) _root = y;
        else if (x == x.Parent.Left) x.Parent.Left  = y;
        else                         x.Parent.Right = y;
        y.Left   = x;
        x.Parent = y;
        x.UpdateMetadata(_nil);
        y.UpdateMetadata(_nil);
    }

    private void RotateRight(RbTreeNode y)
    {
        var x = y.Left ?? _nil;
        if (x.IsNil) return;
        y.Left = x.Right ?? _nil;
        if (!(x.Right ?? _nil).IsNil) x.Right!.Parent = y;
        x.Parent = y.Parent;
        if (y.Parent == null || y.Parent.IsNil) _root = x;
        else if (y == y.Parent.Left) y.Parent.Left  = x;
        else                         y.Parent.Right = x;
        x.Right  = y;
        y.Parent = x;
        y.UpdateMetadata(_nil);
        x.UpdateMetadata(_nil);
    }
}
