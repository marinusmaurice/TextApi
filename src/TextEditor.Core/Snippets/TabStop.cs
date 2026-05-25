namespace TextEditor.Core.Snippets;

/// <summary>
/// A live tab stop within an active <see cref="SnippetSession"/>.
/// Tracks the current offset and length of its placeholder text in the document.
/// All tab stops with the same <see cref="Index"/> are mirrors of each other.
/// </summary>
public sealed class TabStop
{
    /// <summary>The tab stop number (1, 2, … or 0 for exit).</summary>
    public int Index { get; }

    /// <summary>Current offset in the document where this tab stop starts.</summary>
    public int Offset { get; internal set; }

    /// <summary>Current length of the placeholder text selected at this tab stop.</summary>
    public int Length { get; internal set; }

    internal TabStop(int index, int offset, int length)
    {
        Index  = index;
        Offset = offset;
        Length = length;
    }
}
