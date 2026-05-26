namespace TextAPI.Core.Language;

/// <summary>
/// Extends <see cref="ISyntaxTokeniser"/> with inter-line state so the
/// <see cref="LineHighlightCache"/> can re-tokenise only the lines whose state
/// actually changed after an edit.
///
/// State is an opaque <see langword="int"/>.  Implementations define the
/// meaning; the only contract is that <see cref="InitialState"/> is the value
/// used at the very start of a document (line 0).
///
/// Example states for C#:
///   0 = normal
///   1 = inside a block comment (/* … */)
/// </summary>
public interface IStatefulSyntaxTokeniser : ISyntaxTokeniser
{
    /// <summary>
    /// The state value that applies before the first line of a document.
    /// Typically 0 ("normal / outside every multi-line construct").
    /// </summary>
    int InitialState { get; }

    /// <summary>
    /// Tokenise <paramref name="lineText"/> given the inter-line state
    /// <paramref name="stateIn"/> inherited from the end of the previous line.
    /// </summary>
    /// <param name="lineText">Line content without the trailing newline.</param>
    /// <param name="lineOffset">
    ///   Document character offset of the first character of this line.
    ///   Token <c>Start</c> positions are absolute (offset-based), not
    ///   column-relative.
    /// </param>
    /// <param name="stateIn">State from the end of the previous line.</param>
    /// <param name="stateOut">State to pass to the beginning of the next line.</param>
    IReadOnlyList<SyntaxToken> TokeniseLine(
        string lineText, int lineOffset, int stateIn, out int stateOut);
}
