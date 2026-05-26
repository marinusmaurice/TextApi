namespace TextAPI.Core.Language;

/// <summary>
/// Finds matching bracket pairs in a <see cref="TextDocument"/>, correctly
/// skipping brackets that appear inside string or comment tokens.
///
/// Supported pairs: <c>( )</c> — <c>[ ]</c> — <c>{ }</c>
///
/// The matcher is language-agnostic: it relies entirely on the token types
/// "string" and "comment" produced by whatever
/// <see cref="ISyntaxTokeniser"/> is attached to the document.  For plain-
/// text documents (no tokeniser) every bracket is live.
/// </summary>
public static class BracketMatcher
{
    // Open bracket → its expected close
    private static readonly Dictionary<char, char> Closes = new()
        { ['('] = ')', ['['] = ']', ['{'] = '}' };

    // Close bracket → its expected open
    private static readonly Dictionary<char, char> Opens = new()
        { [')'] = '(', [']'] = '[', ['}'] = '{' };

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Given a document <paramref name="offset"/> that points at an opening
    /// or closing bracket character, returns the offset of its matching
    /// counterpart.
    ///
    /// Returns <c>-1</c> when:
    /// <list type="bullet">
    ///   <item><paramref name="offset"/> is out of range.</item>
    ///   <item>The character at <paramref name="offset"/> is not a bracket.</item>
    ///   <item>No matching bracket was found (unbalanced input).</item>
    /// </list>
    /// </summary>
    public static int FindMatch(TextDocument doc, int offset)
    {
        if (offset < 0 || offset >= doc.Length) return -1;

        char ch = doc.GetText(offset, 1)[0];

        if (Closes.TryGetValue(ch, out char close))
            return ScanForward(doc, offset, ch, close);

        if (Opens.TryGetValue(ch, out char open))
            return ScanBackward(doc, offset, open, ch);

        return -1;
    }

    // ── Forward scan (open → close) ───────────────────────────────────────

    private static int ScanForward(
        TextDocument doc, int startOffset, char open, char close)
    {
        var (startLine, startCol) = doc.OffsetToPosition(startOffset);
        int depth = 0;

        for (int line = startLine; line < doc.LineCount; line++)
        {
            string lineText  = doc.GetLine(line);
            bool[] masked    = BuildMask(doc, line);
            int    firstCol  = line == startLine ? startCol : 0;
            int    lineStart = doc.PositionToOffset(line, 0);

            for (int col = firstCol; col < lineText.Length; col++)
            {
                if (col < masked.Length && masked[col]) continue;

                char c = lineText[col];
                if      (c == open)  depth++;
                else if (c == close) { if (--depth == 0) return lineStart + col; }
            }
        }

        return -1;   // unmatched
    }

    // ── Backward scan (close → open) ──────────────────────────────────────

    private static int ScanBackward(
        TextDocument doc, int startOffset, char open, char close)
    {
        var (startLine, startCol) = doc.OffsetToPosition(startOffset);
        int depth = 0;

        for (int line = startLine; line >= 0; line--)
        {
            string lineText  = doc.GetLine(line);
            bool[] masked    = BuildMask(doc, line);
            int    lastCol   = line == startLine ? startCol : lineText.Length - 1;
            int    lineStart = doc.PositionToOffset(line, 0);

            for (int col = lastCol; col >= 0; col--)
            {
                if (col < masked.Length && masked[col]) continue;

                char c = lineText[col];
                if      (c == close) depth++;
                else if (c == open)  { if (--depth == 0) return lineStart + col; }
            }
        }

        return -1;   // unmatched
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Build a boolean mask for <paramref name="lineIndex"/> where <c>true</c>
    /// means the column is inside a <c>string</c> or <c>comment</c> token
    /// and must not be treated as a live bracket.
    /// </summary>
    private static bool[] BuildMask(TextDocument doc, int lineIndex)
    {
        string lineText   = doc.GetLine(lineIndex);
        if (lineText.Length == 0) return [];

        int    lineOffset = doc.PositionToOffset(lineIndex, 0);
        var    tokens     = doc.GetSyntaxTokens(lineIndex);
        var    mask       = new bool[lineText.Length];

        foreach (var tok in tokens)
        {
            if (tok.Type is not ("string" or "comment")) continue;
            int from = tok.Start - lineOffset;
            int to   = tok.End   - lineOffset;
            for (int i = Math.Max(0, from); i < to && i < mask.Length; i++)
                mask[i] = true;
        }

        return mask;
    }
}
