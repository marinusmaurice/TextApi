namespace TextEditor.Core.Cursor;

/// <summary>
/// The text and position of a word (or adjacent non-word group) returned by
/// <see cref="WordBoundary.GetWordAt"/>.
/// </summary>
public readonly record struct WordSpan(int Start, int End, string Text)
{
    /// <summary>Number of characters in the span.</summary>
    public int Length => End - Start;

    /// <summary>True when Start == End (empty match).</summary>
    public bool IsEmpty => Start == End;

    /// <summary>Canonical empty span returned for empty documents or out-of-range queries.</summary>
    public static readonly WordSpan Empty = new(0, 0, string.Empty);
}
