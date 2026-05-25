namespace TextEditor.Core.InlayHints;

/// <summary>
/// An inline annotation displayed at a specific document offset without modifying the text.
/// </summary>
public sealed class InlayHint
{
    /// <summary>Unique identifier for this hint (assigned by <see cref="InlayHintModel"/>).</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Zero-based character offset in the document where the hint is displayed.</summary>
    public int Offset { get; internal set; }

    /// <summary>The text shown inline (e.g. "name:", ": int").</summary>
    public string Text { get; }

    /// <summary>The kind of hint.</summary>
    public InlayHintKind Kind { get; }

    /// <summary>Optional tooltip text shown on hover.</summary>
    public string? Tooltip { get; }

    public InlayHint(int offset, string text, InlayHintKind kind = InlayHintKind.Other, string? tooltip = null)
    {
        Offset  = offset;
        Text    = text;
        Kind    = kind;
        Tooltip = tooltip;
    }
}
