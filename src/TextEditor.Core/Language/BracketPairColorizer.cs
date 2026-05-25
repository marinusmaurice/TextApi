using System;
using System.Collections.Generic;

namespace TextEditor.Core.Language;

/// <summary>
/// A matched bracket pair with its nesting-depth color index.
/// ColorIndex cycles 0 → 1 → 2 → 0 with depth.
/// ColorIndex == -1 means an unmatched bracket.
/// </summary>
public readonly record struct BracketPair(
    int OpenOffset,
    int CloseOffset,
    int ColorIndex);

/// <summary>
/// Computes bracket pair colorization for a range of lines.
///
/// Algorithm
/// ─────────
///   Walk every character in [startLine..endLine]. For each opening bracket
///   not inside a string/comment token, push onto a stack with current depth
///   and increment depth. For each closing bracket, pop from the stack and
///   emit a pair with depth%3. Unmatched brackets get ColorIndex = -1.
///
/// Performance
/// ───────────
///   O(n × m) where n = bracket count and m = average match-search distance.
///   Acceptable for viewport-sized ranges (typically &lt; 200 lines).
/// </summary>
public static class BracketPairColorizer
{
    private static readonly HashSet<char> OpenBrackets  = new() { '(', '[', '{' };
    private static readonly HashSet<char> CloseBrackets = new() { ')', ']', '}' };

    /// <summary>
    /// Returns all matched (and unmatched) bracket pairs whose opening bracket
    /// falls within [startLine, endLine] (inclusive, zero-based).
    /// </summary>
    public static IReadOnlyList<BracketPair> GetBracketPairs(
        TextDocument doc, int startLine, int endLine)
    {
        endLine   = Math.Min(endLine,   doc.LineCount - 1);
        startLine = Math.Max(0,         startLine);

        var pairs = new List<BracketPair>();
        var stack = new Stack<(int offset, int depth)>();
        int depth = 0;

        for (int line = startLine; line <= endLine; line++)
        {
            string lineText  = doc.GetLine(line);
            int    lineStart = doc.PositionToOffset(line, 0);
            bool[] mask      = BuildMask(doc, line, lineText, lineStart);

            for (int col = 0; col < lineText.Length; col++)
            {
                char c = lineText[col];
                if (!OpenBrackets.Contains(c) && !CloseBrackets.Contains(c))
                    continue;

                // Skip brackets inside string/comment tokens
                if (col < mask.Length && mask[col])
                    continue;

                int docOffset = lineStart + col;

                if (OpenBrackets.Contains(c))
                {
                    stack.Push((docOffset, depth));
                    depth++;
                }
                else // closing bracket
                {
                    if (stack.Count > 0)
                    {
                        var (openOffset, openDepth) = stack.Pop();
                        depth = openDepth;
                        pairs.Add(new BracketPair(openOffset, docOffset, openDepth % 3));
                    }
                    else
                    {
                        // Unmatched closing bracket
                        pairs.Add(new BracketPair(-1, docOffset, -1));
                    }
                }
            }
        }

        // Any remaining items on the stack are unmatched opening brackets
        while (stack.Count > 0)
        {
            var (openOffset, _) = stack.Pop();
            pairs.Add(new BracketPair(openOffset, -1, -1));
        }

        // Sort by open offset (or close offset for unmatched close brackets)
        pairs.Sort((a, b) =>
        {
            int ao = a.OpenOffset  >= 0 ? a.OpenOffset  : a.CloseOffset;
            int bo = b.OpenOffset  >= 0 ? b.OpenOffset  : b.CloseOffset;
            return ao.CompareTo(bo);
        });

        return pairs;
    }

    /// <summary>
    /// Builds a boolean mask (same approach as BracketMatcher.BuildMask) where
    /// <c>true</c> means the column is inside a string or comment token.
    /// Token Start/End are document offsets, so we subtract lineStart to get
    /// line-relative column indices.
    /// </summary>
    private static bool[] BuildMask(
        TextDocument doc, int lineIndex, string lineText, int lineStart)
    {
        if (lineText.Length == 0) return Array.Empty<bool>();

        var tokens = doc.GetSyntaxTokens(lineIndex);
        var mask   = new bool[lineText.Length];

        foreach (var tok in tokens)
        {
            if (tok.Type is not ("string" or "comment")) continue;
            int from = tok.Start - lineStart;
            int to   = tok.End   - lineStart;
            for (int i = Math.Max(0, from); i < to && i < mask.Length; i++)
                mask[i] = true;
        }

        return mask;
    }
}
