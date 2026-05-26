namespace TextAPI.Core.InlayHints;

/// <summary>Classifies what an inlay hint represents.</summary>
public enum InlayHintKind
{
    /// <summary>A parameter name hint, e.g. <c>name:</c> before a method argument.</summary>
    Parameter,

    /// <summary>An inferred type hint, e.g. <c>: int</c> after a var declaration.</summary>
    Type,

    /// <summary>A return value hint shown after an expression.</summary>
    Return,

    /// <summary>Any other kind of inlay hint.</summary>
    Other,
}
