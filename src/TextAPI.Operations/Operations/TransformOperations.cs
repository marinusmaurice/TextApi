using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextAPI.Core;
using TextAPI.Core.Search;

namespace TextAPI.Operations;

/// <summary>Case conversion mode for ConvertCaseOperation.</summary>
public enum CaseMode { Upper, Lower, TitleCase }

/// <summary>
/// Collapses runs of spaces/tabs on each line to a single space.
/// Uses doc.Load() — this resets the undo stack and change tracker baseline.
/// </summary>
public sealed class NormaliseWhitespaceOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "NORMALISE_WHITESPACE";

    public OperationResult Execute(TextDocument doc)
    {
        string text = doc.GetText();
        // Replace multiple consecutive spaces/tabs within each line, but do not collapse newlines
        var result = Regex.Replace(text, @"[^\S\n]+", " ");
        // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
        doc.Load(result);
        return OperationResult.Ok();
    }
}

/// <summary>
/// Removes trailing spaces and tabs from every line.
/// Uses doc.Load() — this resets the undo stack and change tracker baseline.
/// </summary>
public sealed class TrimTrailingWhitespaceOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "TRIM_TRAILING_WHITESPACE";

    public OperationResult Execute(TextDocument doc)
    {
        string text = doc.GetText();
        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
            lines[i] = lines[i].TrimEnd(' ', '\t');
        // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
        doc.Load(string.Join('\n', lines));
        return OperationResult.Ok();
    }
}

/// <summary>
/// Converts text to upper, lower, or title case, optionally restricted to a range between two anchors.
/// Uses doc.Load() for whole-document conversion (resets undo stack and change tracker baseline).
/// </summary>
public sealed class ConvertCaseOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "CONVERT_CASE";

    public CaseMode Mode { get; init; }
    public string? StartAnchor { get; init; }
    public string? EndAnchor { get; init; }

    private string Convert(string input) => Mode switch
    {
        CaseMode.Upper => input.ToUpper(),
        CaseMode.Lower => input.ToLower(),
        CaseMode.TitleCase => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower()),
        _ => input
    };

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(StartAnchor) || string.IsNullOrEmpty(EndAnchor))
        {
            // Whole-document conversion
            // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
            doc.Load(Convert(doc.GetText()));
            return OperationResult.Ok();
        }

        // Ranged conversion
        var startMatch = AnchorHelper.FindAnchor(doc, StartAnchor, 1, caseSensitive: true);
        if (startMatch is null)
            return OperationResult.Fail($"Start anchor '{StartAnchor}' not found");

        int searchFrom = startMatch.Value.Offset + startMatch.Value.Length;
        var opts = new SearchOptions { CaseSensitive = true };
        SearchMatch? endMatch = null;
        foreach (var m in doc.FindAll(EndAnchor, opts))
        {
            if (m.Offset >= searchFrom)
            {
                endMatch = m;
                break;
            }
        }
        if (endMatch is null)
            return OperationResult.Fail($"End anchor '{EndAnchor}' not found");

        int rangeStart = startMatch.Value.Offset + startMatch.Value.Length;
        int rangeEnd = endMatch.Value.Offset;
        int rangeLength = rangeEnd - rangeStart;

        string section = doc.GetText(rangeStart, rangeLength);
        string converted = Convert(section);
        doc.Replace(rangeStart, rangeLength, converted);
        return OperationResult.Ok(charsInserted: converted.Length, charsDeleted: rangeLength, resolvedOffset: rangeStart);
    }
}

/// <summary>
/// Sorts a range of lines alphabetically.
/// Uses doc.Load() — this resets the undo stack and change tracker baseline.
/// </summary>
public sealed class SortLinesOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "SORT_LINES";

    public int StartLine { get; init; } = 0;
    public int? EndLine { get; init; }
    public bool Descending { get; init; } = false;
    public bool CaseSensitive { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        int endLine = EndLine ?? doc.LineCount - 1;

        if (StartLine < 0 || StartLine >= doc.LineCount)
            return OperationResult.Fail($"StartLine {StartLine} is out of range");
        if (endLine < StartLine || endLine >= doc.LineCount)
            return OperationResult.Fail($"EndLine {endLine} is out of range");

        string text = doc.GetText();
        string[] allLines = text.Split('\n');

        string[] range = allLines[StartLine..(endLine + 1)];
        var comparison = CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        Array.Sort(range, (a, b) =>
        {
            int cmp = string.Compare(a, b, comparison);
            return Descending ? -cmp : cmp;
        });

        for (int i = 0; i < range.Length; i++)
            allLines[StartLine + i] = range[i];

        // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
        doc.Load(string.Join('\n', allLines));
        return OperationResult.Ok();
    }
}

/// <summary>
/// Removes duplicate lines from the document.
/// Uses doc.Load() — this resets the undo stack and change tracker baseline.
/// </summary>
public sealed class DeduplicateLinesOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "DEDUPLICATE_LINES";

    /// <summary>When true, only consecutive duplicate lines are removed.</summary>
    public bool ConsecutiveOnly { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        string text = doc.GetText();
        string[] lines = text.Split('\n');

        List<string> result;
        if (ConsecutiveOnly)
        {
            result = new List<string>(lines.Length);
            string? prev = null;
            foreach (var line in lines)
            {
                if (line != prev)
                    result.Add(line);
                prev = line;
            }
        }
        else
        {
            var seen = new HashSet<string>();
            result = new List<string>(lines.Length);
            foreach (var line in lines)
            {
                if (seen.Add(line))
                    result.Add(line);
            }
        }

        // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
        doc.Load(string.Join('\n', result));
        return OperationResult.Ok();
    }
}

/// <summary>
/// Adds or removes a prefix (indent) from a range of lines.
/// Processes lines from bottom to top to avoid offset drift between individual doc.Replace() calls.
/// </summary>
public sealed class IndentOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "INDENT";

    public int StartLine { get; init; }
    public int? EndLine { get; init; }
    public string Prefix { get; init; } = "    ";
    public bool Dedent { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        int endLine = EndLine ?? doc.LineCount - 1;

        if (StartLine < 0 || StartLine >= doc.LineCount)
            return OperationResult.Fail($"StartLine {StartLine} is out of range");
        if (endLine < StartLine || endLine >= doc.LineCount)
            return OperationResult.Fail($"EndLine {endLine} is out of range");

        int totalInserted = 0;
        int totalDeleted = 0;

        // Process bottom to top to avoid offset drift when making individual Replace calls
        for (int i = endLine; i >= StartLine; i--)
        {
            int offset = doc.PositionToOffset(i, 0);
            string line = doc.GetLine(i);

            if (Dedent)
            {
                if (line.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    doc.Delete(offset, Prefix.Length);
                    totalDeleted += Prefix.Length;
                }
                // If prefix not present, skip silently
            }
            else
            {
                doc.Insert(offset, Prefix);
                totalInserted += Prefix.Length;
            }
        }

        return OperationResult.Ok(charsInserted: totalInserted, charsDeleted: totalDeleted);
    }
}

/// <summary>
/// Hard-wraps long lines at MaxColumns by breaking at word boundaries.
/// Uses doc.Load() — this resets the undo stack and change tracker baseline.
/// </summary>
public sealed class WrapLinesOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "WRAP_LINES";

    public int MaxColumns { get; init; } = 80;
    public int StartLine { get; init; } = 0;
    public int? EndLine { get; init; }

    private static IEnumerable<string> WrapLine(string line, int maxCols)
    {
        if (line.Length <= maxCols)
        {
            yield return line;
            yield break;
        }

        int pos = 0;
        while (pos < line.Length)
        {
            if (line.Length - pos <= maxCols)
            {
                yield return line[pos..];
                break;
            }

            // Find last space at or before maxCols from pos
            int end = pos + maxCols;
            int breakAt = line.LastIndexOf(' ', end - 1, end - pos);
            if (breakAt <= pos)
            {
                // No space found; hard-break at maxCols
                yield return line[pos..end];
                pos = end;
            }
            else
            {
                yield return line[pos..breakAt];
                pos = breakAt + 1; // skip the space
            }
        }
    }

    public OperationResult Execute(TextDocument doc)
    {
        if (MaxColumns <= 0)
            return OperationResult.Fail("MaxColumns must be > 0");

        int endLine = EndLine ?? doc.LineCount - 1;

        if (StartLine < 0 || StartLine >= doc.LineCount)
            return OperationResult.Fail($"StartLine {StartLine} is out of range");
        if (endLine < StartLine || endLine >= doc.LineCount)
            return OperationResult.Fail($"EndLine {endLine} is out of range");

        string text = doc.GetText();
        string[] allLines = text.Split('\n');

        var resultLines = new List<string>();
        for (int i = 0; i < allLines.Length; i++)
        {
            if (i >= StartLine && i <= endLine)
            {
                foreach (var wrapped in WrapLine(allLines[i], MaxColumns))
                    resultLines.Add(wrapped);
            }
            else
            {
                resultLines.Add(allLines[i]);
            }
        }

        // doc.Load() resets undo stack and change tracker baseline — acceptable for whole-doc transforms
        doc.Load(string.Join('\n', resultLines));
        return OperationResult.Ok();
    }
}
