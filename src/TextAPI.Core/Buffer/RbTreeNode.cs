namespace TextAPI.Core.Buffer;

/// <summary>
/// Red-Black tree node colour.
/// </summary>
internal enum NodeColour { Red, Black }

/// <summary>
/// A single node in the augmented Red-Black tree that backs the piece table.
/// Each node represents one Piece and carries subtree-level metadata so that
/// both character-offset and line-number queries run in O(log n).
///
/// Malloc lesson applied: prev/next piece pointers are embedded directly in
/// the node (boundary-tag style) so neighbour lookup is O(1) without a
/// separate directory or extra tree traversal.
/// </summary>
internal sealed class RbTreeNode
{
    // ── Tree structure ────────────────────────────────────────────────────
    internal RbTreeNode? Left;
    internal RbTreeNode? Right;
    internal RbTreeNode? Parent;
    internal NodeColour  Colour = NodeColour.Red;

    // ── Boundary-tag neighbours (malloc-inspired O(1) adjacency) ─────────
    /// <summary>The logically previous piece in document order.</summary>
    internal RbTreeNode? PrevPiece;
    /// <summary>The logically next piece in document order.</summary>
    internal RbTreeNode? NextPiece;

    // ── Piece payload ─────────────────────────────────────────────────────
    /// <summary>0 = original buffer, 1 = add buffer.</summary>
    internal int    BufferIndex;
    /// <summary>Byte/char start offset within the chosen buffer.</summary>
    internal int    Start;
    /// <summary>Length of this piece in characters.</summary>
    internal int    Length;
    /// <summary>Number of line-feed characters (\n) in this piece.</summary>
    internal int    LineFeedCount;

    // ── Augmented subtree metadata (recomputed on every rotation/insert) ──
    /// <summary>Total character length of the entire subtree rooted here.</summary>
    internal int    SubtreeCharCount;
    /// <summary>Total line-feed count of the entire subtree rooted here.</summary>
    internal int    SubtreeLineFeedCount;

    // ── Sentinel detection ────────────────────────────────────────────────
    internal bool IsNil => Length == -1;   // only the shared Nil sentinel has Length == -1

    internal RbTreeNode() { }

    /// <summary>Creates the shared NIL sentinel node (black, zero everything).</summary>
    internal static RbTreeNode CreateNil() => new()
    {
        Colour               = NodeColour.Black,
        Length               = -1,
        SubtreeCharCount     = 0,
        SubtreeLineFeedCount = 0
    };

    /// <summary>Recompute subtree aggregates from children (must be called bottom-up after any structural change).</summary>
    internal void UpdateMetadata(RbTreeNode nil)
    {
        SubtreeCharCount     = (Left.IsNil  ? 0 : Left!.SubtreeCharCount)
                             + Length
                             + (Right.IsNil ? 0 : Right!.SubtreeCharCount);

        SubtreeLineFeedCount = (Left.IsNil  ? 0 : Left!.SubtreeLineFeedCount)
                             + LineFeedCount
                             + (Right.IsNil ? 0 : Right!.SubtreeLineFeedCount);
    }
}
