namespace TextAPI.Core.Language;

/// <summary>A vertical indent guide spanning a range of lines.</summary>
public readonly record struct IndentGuide(int Column, int StartLine, int EndLine);

/// <summary>
/// Computes vertical indent guides for a range of document lines.
///
/// A guide at column C appears as a vertical bar drawn at that column position,
/// spanning all consecutive lines whose leading-whitespace indent exceeds C.
/// Blank (whitespace-only) lines are treated as transparent — they neither break
/// nor start a guide span, but they are included within a span if surrounded by
/// indented lines.
/// </summary>
public static class IndentGuideProvider
{
    /// <summary>
    /// Returns all indent guides for [startLine, endLine] (inclusive).
    /// </summary>
    /// <param name="doc">The document to analyse.</param>
    /// <param name="startLine">First line (zero-based).</param>
    /// <param name="endLine">Last line (zero-based, inclusive).</param>
    /// <param name="tabWidth">Column width of a tab character (default 4).</param>
    public static IReadOnlyList<IndentGuide> GetGuides(
        TextDocument doc, int startLine, int endLine, int tabWidth = 4)
    {
        // clamp
        startLine = Math.Max(0, startLine);
        endLine   = Math.Min(endLine, doc.LineCount - 1);
        if (startLine > endLine) return [];

        int lineCount = endLine - startLine + 1;
        int[] indents = new int[lineCount]; // -1 = blank

        // Compute raw indents
        for (int i = 0; i < lineCount; i++)
        {
            string line = doc.GetLine(startLine + i);
            indents[i] = ComputeIndent(line, tabWidth);
        }

        // Fill blank lines with min of nearest non-blank neighbors
        FillBlankLines(indents);

        // Find max indent
        int maxIndent = indents.Length > 0 ? indents.Max() : 0;
        if (maxIndent <= 0) return [];

        var guides = new List<IndentGuide>();

        // Generate guides for each guide column
        for (int col = 0; col < maxIndent; col += tabWidth)
        {
            // Find spans where effectiveIndent > col
            int? spanStart = null;
            for (int i = 0; i < lineCount; i++)
            {
                bool inside = indents[i] > col;
                if (inside)
                {
                    spanStart ??= i;
                }
                else if (spanStart.HasValue)
                {
                    // End of span at i-1
                    int spanEnd = i - 1;
                    if (spanEnd >= spanStart.Value) // at least 1 line
                        guides.Add(new IndentGuide(col, startLine + spanStart.Value, startLine + spanEnd));
                    spanStart = null;
                }
            }
            // Close any open span
            if (spanStart.HasValue)
            {
                int spanEnd = lineCount - 1;
                if (spanEnd >= spanStart.Value)
                    guides.Add(new IndentGuide(col, startLine + spanStart.Value, startLine + spanEnd));
            }
        }

        guides.Sort((a, b) =>
        {
            int c = a.Column.CompareTo(b.Column);
            return c != 0 ? c : a.StartLine.CompareTo(b.StartLine);
        });

        return guides;
    }

    /// <summary>
    /// Computes the leading-whitespace column count of a line.
    /// Returns -1 for blank/whitespace-only lines.
    /// </summary>
    private static int ComputeIndent(string line, int tabWidth)
    {
        if (string.IsNullOrWhiteSpace(line)) return -1;
        int col = 0;
        foreach (char c in line)
        {
            if (c == ' ')       { col++; }
            else if (c == '\t') { col = ((col / tabWidth) + 1) * tabWidth; }
            else break;
        }
        return col;
    }

    /// <summary>
    /// Replace -1 (blank line) entries with the minimum of the nearest
    /// non-blank indent above and below. This makes blank lines transparent
    /// to guide computation — they don't break a guide span.
    /// </summary>
    private static void FillBlankLines(int[] indents)
    {
        int n = indents.Length;
        // Forward pass: carry last non-blank indent downward
        int[] fromAbove = new int[n];
        int   last      = 0;
        for (int i = 0; i < n; i++)
        {
            if (indents[i] >= 0) last = indents[i];
            fromAbove[i] = last;
        }
        // Backward pass: carry last non-blank indent upward
        int[] fromBelow = new int[n];
        last = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            if (indents[i] >= 0) last = indents[i];
            fromBelow[i] = last;
        }
        // Use min of above and below for blank lines
        for (int i = 0; i < n; i++)
        {
            if (indents[i] < 0)
                indents[i] = Math.Min(fromAbove[i], fromBelow[i]);
        }
    }
}
