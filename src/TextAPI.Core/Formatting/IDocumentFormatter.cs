namespace TextAPI.Core.Formatting;

/// <summary>
/// Pluggable document formatter.  Implement this to provide language-specific
/// formatting (indentation, line wrapping, style normalisation, etc.).
///
/// <para>
/// Usage:
/// <code>
///   doc.Format(new MyFormatter());
///   doc.Format(new MyFormatter(), startLine: 2, endLine: 5);
/// </code>
/// </para>
/// </summary>
public interface IDocumentFormatter
{
    /// <summary>
    /// Format <paramref name="text"/> and return the formatted result.
    /// The input is LF-normalised raw content of the target range.
    /// The returned string must also be LF-normalised.
    /// Return the same string (reference-equal or value-equal) to signal no change.
    /// </summary>
    string Format(string text);
}
