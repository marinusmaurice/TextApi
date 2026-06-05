using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextAPI.Core;

namespace TextAPI.Operations;

/// <summary>
/// Inserts a new line at the given 0-based line index (pushing existing lines down).
/// </summary>
public sealed class InsertLineOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "INSERT_LINE";

    /// <summary>0-based line index. 0 = before first line; LineCount = after last line.</summary>
    public int LineIndex { get; init; }
    public string Text { get; init; } = "";

    public OperationResult Execute(TextDocument doc)
    {
        if (LineIndex < 0 || LineIndex > doc.LineCount)
            return OperationResult.Fail($"LineIndex {LineIndex} is out of range (0..{doc.LineCount})");

        if (LineIndex == doc.LineCount)
        {
            // Append after the last line
            string toInsert = "\n" + Text;
            doc.Insert(doc.Length, toInsert);
            return OperationResult.Ok(charsInserted: toInsert.Length, resolvedOffset: doc.Length - toInsert.Length);
        }
        else
        {
            int offset = doc.PositionToOffset(LineIndex, 0);
            string toInsert = Text + "\n";
            doc.Insert(offset, toInsert);
            return OperationResult.Ok(charsInserted: toInsert.Length, resolvedOffset: offset);
        }
    }
}

/// <summary>
/// Deletes the line at the given 0-based line index, including its trailing newline.
/// </summary>
public sealed class DeleteLineOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "DELETE_LINE";

    public int LineIndex { get; init; }

    public OperationResult Execute(TextDocument doc)
    {
        if (LineIndex < 0 || LineIndex >= doc.LineCount)
            return OperationResult.Fail($"LineIndex {LineIndex} is out of range (0..{doc.LineCount - 1})");

        int offset = doc.PositionToOffset(LineIndex, 0);
        string lineText = doc.GetLine(LineIndex);
        bool isLastLine = LineIndex == doc.LineCount - 1;

        int deleteLength;
        if (isLastLine)
        {
            // No trailing newline; also remove the preceding newline if there is one
            if (LineIndex > 0 && offset > 0)
            {
                // Include the preceding '\n' so we don't leave a dangling newline
                offset--;
                deleteLength = lineText.Length + 1;
            }
            else
            {
                deleteLength = lineText.Length;
            }
        }
        else
        {
            // Include the trailing newline
            deleteLength = lineText.Length + 1;
        }

        if (offset + deleteLength > doc.Length)
            deleteLength = doc.Length - offset;

        doc.Delete(offset, deleteLength);
        return OperationResult.Ok(charsDeleted: deleteLength, resolvedOffset: offset);
    }
}

/// <summary>
/// Replaces the content of a line (not its newline character) with new text.
/// </summary>
public sealed class ReplaceLineOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "REPLACE_LINE";

    public int LineIndex { get; init; }
    public string Text { get; init; } = "";

    public OperationResult Execute(TextDocument doc)
    {
        if (LineIndex < 0 || LineIndex >= doc.LineCount)
            return OperationResult.Fail($"LineIndex {LineIndex} is out of range (0..{doc.LineCount - 1})");

        int offset = doc.PositionToOffset(LineIndex, 0);
        string oldLine = doc.GetLine(LineIndex);

        doc.Replace(offset, oldLine.Length, Text);
        return OperationResult.Ok(charsInserted: Text.Length, charsDeleted: oldLine.Length, resolvedOffset: offset);
    }
}

/// <summary>
/// Finds the Nth line whose content matches a pattern and inserts a new line after it.
/// </summary>
public sealed class InsertAfterLineMatchOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "INSERT_AFTER_LINE_MATCH";

    public string Pattern { get; init; } = "";
    public string LineText { get; init; } = "";
    public bool UseRegex { get; init; } = false;
    public int Occurrence { get; init; } = 1;

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Pattern))
            return OperationResult.Fail("Pattern cannot be empty");

        Regex? regex = null;
        if (UseRegex)
        {
            try { regex = new Regex(Pattern); }
            catch (ArgumentException ex) { return OperationResult.Fail($"Invalid regex: {ex.Message}"); }
        }

        int matchCount = 0;
        for (int i = 0; i < doc.LineCount; i++)
        {
            string line = doc.GetLine(i);
            bool isMatch = UseRegex
                ? regex!.IsMatch(line)
                : line.Contains(Pattern, StringComparison.Ordinal);

            if (!isMatch) continue;

            matchCount++;
            if (matchCount < Occurrence) continue;

            // Found the Nth matching line; insert after it
            bool isLastLine = i == doc.LineCount - 1;
            int lineOffset = doc.PositionToOffset(i, 0);
            int insertAt;
            string toInsert;

            if (isLastLine)
            {
                insertAt = doc.Length;
                toInsert = "\n" + LineText;
            }
            else
            {
                // Insert before the next line's newline position (i.e., end of this line content)
                insertAt = lineOffset + line.Length;
                toInsert = "\n" + LineText;
            }

            doc.Insert(insertAt, toInsert);
            return OperationResult.Ok(charsInserted: toInsert.Length, resolvedOffset: insertAt);
        }

        return OperationResult.Fail($"No line matching '{Pattern}' (occurrence {Occurrence}) was found");
    }
}
