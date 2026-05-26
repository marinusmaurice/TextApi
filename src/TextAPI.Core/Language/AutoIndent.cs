namespace TextAPI.Core.Language;

/// <summary>
/// Computes indentation strings for two common editor key-press scenarios:
///
/// <list type="bullet">
///   <item>
///     <term>Enter key</term>
///     <description>
///       <see cref="GetIndent"/> returns the whitespace prefix for the new
///       line.  It copies the current line's leading whitespace and adds one
///       extra <paramref name="tabText"/> level when the line's meaningful
///       content (trailing comments stripped) ends with <c>{</c>.
///     </description>
///   </item>
///   <item>
///     <term>Closing brace typed</term>
///     <description>
///       <see cref="GetClosingBraceIndent"/> finds the <c>{</c> that matches
///       the <c>}</c> at the given offset and returns the indentation of that
///       opening-brace line — ready to replace the current line's leading
///       whitespace so the brace aligns correctly.
///     </description>
///   </item>
/// </list>
/// </summary>
public static class AutoIndent
{
    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the indentation string to insert at the beginning of the new
    /// line created when the user presses Enter at
    /// <paramref name="caretOffset"/>.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caretOffset">Caret position at the moment Enter is pressed.</param>
    /// <param name="tabText">One indentation level (default: four spaces).</param>
    public static string GetIndent(
        TextDocument doc, int caretOffset, string tabText = "    ")
    {
        var (line, _) = doc.OffsetToPosition(caretOffset);
        string lineText = doc.GetLine(line);

        // Baseline: copy whatever whitespace the current line starts with.
        string baseIndent = LeadingWhitespace(lineText);

        // If the line's meaningful content ends with '{', add one level.
        string meaningful = MeaningfulContent(doc, line).TrimEnd();
        if (meaningful.Length > 0 && meaningful[^1] == '{')
            return baseIndent + tabText;

        return baseIndent;
    }

    /// <summary>
    /// When the user types <c>}</c>, returns the indentation that the closing
    /// brace line should have — i.e. the leading whitespace of the line that
    /// contains the matching <c>{</c>.
    ///
    /// Returns <see langword="null"/> when:
    /// <list type="bullet">
    ///   <item><paramref name="caretOffset"/> is out of range or not a <c>}</c>.</item>
    ///   <item>No matching <c>{</c> exists (unbalanced input).</item>
    /// </list>
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <param name="caretOffset">Offset of the <c>}</c> character.</param>
    /// <param name="tabText">One indentation level (unused here; kept for API symmetry).</param>
    public static string? GetClosingBraceIndent(
        TextDocument doc, int caretOffset, string tabText = "    ")
    {
        if (caretOffset < 0 || caretOffset >= doc.Length) return null;
        if (doc.GetText(caretOffset, 1) != "}") return null;

        int matchOffset = BracketMatcher.FindMatch(doc, caretOffset);
        if (matchOffset < 0) return null;

        var (matchLine, _) = doc.OffsetToPosition(matchOffset);
        return LeadingWhitespace(doc.GetLine(matchLine));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Returns the leading whitespace prefix of a line.</summary>
    private static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && line[i] is ' ' or '\t') i++;
        return line[0..i];
    }

    /// <summary>
    /// Returns the line text with any trailing inline or block comments
    /// stripped, so the caller can examine the last meaningful character.
    /// Uses the document's syntax tokens so that comment markers inside
    /// strings are not misidentified as comments.
    /// </summary>
    private static string MeaningfulContent(TextDocument doc, int lineIndex)
    {
        string lineText   = doc.GetLine(lineIndex);
        int    lineOffset = doc.PositionToOffset(lineIndex, 0);
        var    tokens     = doc.GetSyntaxTokens(lineIndex);

        // Find the earliest comment token that starts on this line, then cut
        // everything from that column onward.
        int cutAt = lineText.Length;
        foreach (var tok in tokens)
        {
            if (tok.Type != "comment") continue;
            int col = tok.Start - lineOffset;
            if (col < cutAt) cutAt = col;
        }

        return lineText[0..cutAt];
    }
}
