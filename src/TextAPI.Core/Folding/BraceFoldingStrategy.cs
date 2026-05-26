namespace TextAPI.Core.Folding;

/// <summary>
/// Detects foldable regions by matching <c>{</c> and <c>}</c> operator tokens
/// that span more than one line.
///
/// Uses the document's <see cref="TextDocument.GetSyntaxTokens"/> so that
/// braces inside string literals and comments are correctly ignored.
/// Falls back to raw character scanning when no tokeniser is attached (the
/// plain-text case), which still works for most brace-structured formats.
///
/// Algorithm:
/// <list type="number">
///   <item>Walk lines top to bottom.</item>
///   <item>
///     For each line, scan syntax tokens for operator tokens whose text is
///     <c>{</c> or <c>}</c> (multiple braces on one line are processed
///     left-to-right).
///   </item>
///   <item>
///     Push the line index onto a stack when <c>{</c> is encountered; pop
///     and emit a region when <c>}</c> is encountered.
///   </item>
///   <item>
///     Same-line open+close pairs (start == end) are discarded — they do not
///     produce a fold region.
///   </item>
/// </list>
///
/// The <see cref="FoldRegion.Label"/> is the content of
/// <see cref="FoldRegion.StartLine"/> trimmed to 60 characters with a
/// trailing <c> …</c> suffix when truncated.
/// </summary>
public sealed class BraceFoldingStrategy : IFoldingStrategy
{
    private const int MaxLabelLength = 60;

    public IReadOnlyList<(int StartLine, int EndLine, string Label)>
        DetectRegions(TextDocument doc)
    {
        var results = new List<(int, int, string)>();
        var stack   = new Stack<int>();   // startLine of each unmatched '{'

        for (int line = 0; line < doc.LineCount; line++)
        {
            string lineText = doc.GetLine(line);

            // Try syntax tokens first; fall back to character scan.
            var tokens = doc.GetSyntaxTokens(line);
            if (tokens.Count > 0)
                ProcessWithTokens(line, lineText, doc, tokens, stack, results);
            else
                ProcessRaw(line, lineText, stack, results);
        }

        // Unmatched opens have no partner — discard them.
        return results;
    }

    // ── Token-aware processing ────────────────────────────────────────────

    private static void ProcessWithTokens(
        int lineIndex, string lineText, TextDocument doc,
        IReadOnlyList<Language.SyntaxToken> tokens,
        Stack<int> stack,
        List<(int, int, string)> results)
    {
        int lineOffset = doc.PositionToOffset(lineIndex, 0);

        // Collect brace positions from operator tokens, left to right.
        foreach (var tok in tokens.OrderBy(t => t.Start))
        {
            if (tok.Type != "operator") continue;

            int col = tok.Start - lineOffset;
            if (col < 0 || col >= lineText.Length) continue;
            char ch = lineText[col];

            if (ch == '{')
            {
                stack.Push(lineIndex);
            }
            else if (ch == '}' && stack.Count > 0)
            {
                int startLine = stack.Pop();
                if (startLine != lineIndex)               // must span > 1 line
                    results.Add((startLine, lineIndex, MakeLabel(doc, startLine)));
            }
        }
    }

    // ── Raw character scan (NullTokeniser / plain text) ───────────────────

    private static void ProcessRaw(
        int lineIndex, string lineText,
        Stack<int> stack,
        List<(int, int, string)> results)
    {
        foreach (char ch in lineText)
        {
            if (ch == '{')
            {
                stack.Push(lineIndex);
            }
            else if (ch == '}' && stack.Count > 0)
            {
                int startLine = stack.Pop();
                if (startLine != lineIndex)
                    results.Add((startLine, lineIndex, lineText.Trim()));
            }
        }
    }

    // ── Label helper ──────────────────────────────────────────────────────

    private static string MakeLabel(TextDocument doc, int startLine)
    {
        string raw = doc.GetLine(startLine).Trim();

        // When the opening brace is alone on its line (Allman style),
        // use the previous non-empty line for context — it is far more
        // informative than just "{".
        if (raw == "{" && startLine > 0)
        {
            for (int prev = startLine - 1; prev >= 0; prev--)
            {
                string prevRaw = doc.GetLine(prev).Trim();
                if (prevRaw.Length == 0) continue;
                raw = prevRaw + " {";
                break;
            }
        }

        return raw.Length <= MaxLabelLength
            ? raw
            : raw[..MaxLabelLength] + " …";
    }
}
