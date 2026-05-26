namespace TextAPI.Core;

/// <summary>
/// Grapheme-aware statistics for a <see cref="TextDocument"/>.
///
/// <list type="table">
///   <listheader><term>Property</term><description>Meaning</description></listheader>
///   <item><term><see cref="GraphemeCount"/></term>
///         <description>User-perceived character count (Unicode grapheme clusters).
///         This is what most editors display in the status bar and what users mean
///         when they say "how many characters". An emoji like 👨‍👩‍👧‍👦 counts as 1.</description></item>
///   <item><term><see cref="CodeUnitCount"/></term>
///         <description>UTF-16 code units — equals <c>doc.Length</c>.
///         Matches <c>string.Length</c> and raw buffer size.</description></item>
///   <item><term><see cref="RuneCount"/></term>
///         <description>Unicode code points (Runes). Surrogate pairs count as 1 code point
///         each. Combining marks that join with a base character still count separately.</description></item>
///   <item><term><see cref="WordCount"/></term>
///         <description>Whitespace-delimited word count, consistent with most editors.</description></item>
///   <item><term><see cref="LineCount"/></term>
///         <description>Number of lines in the document (≥ 1 even for an empty document).</description></item>
///   <item><term><see cref="DisplayColumns"/></term>
///         <description>Sum of East Asian Width display columns across the entire document.
///         ASCII and most Latin characters contribute 1; CJK and most emoji contribute 2;
///         combining marks contribute 0.</description></item>
/// </list>
/// </summary>
public sealed record DocumentStats(
    int GraphemeCount,
    int CodeUnitCount,
    int RuneCount,
    int WordCount,
    int LineCount,
    int DisplayColumns);
