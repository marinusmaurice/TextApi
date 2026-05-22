namespace TextEditor.Core.Folding;

/// <summary>
/// Detects foldable regions in a document.
///
/// Implementations inspect the document's content (and optionally its syntax
/// tokens) to produce a flat list of <c>(StartLine, EndLine, Label)</c>
/// tuples.  The <see cref="FoldingModel"/> turns these into
/// <see cref="FoldRegion"/> objects, preserving any existing fold state where
/// the start line matches.
///
/// Regions where <c>StartLine >= EndLine</c> are silently ignored — a region
/// must span at least two lines to be meaningful.
/// </summary>
public interface IFoldingStrategy
{
    /// <summary>
    /// Analyse <paramref name="doc"/> and return every foldable range found.
    /// </summary>
    /// <param name="doc">The document to inspect.</param>
    /// <returns>
    /// Sequence of <c>(StartLine, EndLine, Label)</c> tuples in any order.
    /// Single-line entries (<c>StartLine == EndLine</c>) are legal to return
    /// but will be discarded by the <see cref="FoldingModel"/>.
    /// </returns>
    IReadOnlyList<(int StartLine, int EndLine, string Label)> DetectRegions(TextDocument doc);
}
