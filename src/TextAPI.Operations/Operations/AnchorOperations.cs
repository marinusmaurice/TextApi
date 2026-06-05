using System.Text.Json.Serialization;
using TextAPI.Core;
using TextAPI.Core.Search;

namespace TextAPI.Operations;

/// <summary>
/// Shared helper used by all anchor-based operations.
/// Returns the Nth match (1-based) of the anchor string in the document.
/// </summary>
internal static class AnchorHelper
{
    internal static SearchMatch? FindAnchor(TextDocument doc, string anchor, int occurrence, bool caseSensitive)
    {
        var opts = new SearchOptions { CaseSensitive = caseSensitive };
        int count = 0;
        foreach (var match in doc.FindAll(anchor, opts))
        {
            count++;
            if (count == occurrence)
                return match;
        }
        return null;
    }
}

/// <summary>
/// Inserts text immediately after the Nth occurrence of an anchor string.
/// </summary>
public sealed class InsertAfterOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "INSERT_AFTER";

    public string Anchor { get; init; } = "";
    public string Text { get; init; } = "";
    public int Occurrence { get; init; } = 1;
    public bool CaseSensitive { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        var match = AnchorHelper.FindAnchor(doc, Anchor, Occurrence, CaseSensitive);
        if (match is null)
            return OperationResult.Fail($"Anchor '{Anchor}' not found");

        int insertAt = match.Value.Offset + match.Value.Length;
        doc.Insert(insertAt, Text);
        return OperationResult.Ok(charsInserted: Text.Length, resolvedOffset: insertAt);
    }
}

/// <summary>
/// Inserts text immediately before the Nth occurrence of an anchor string.
/// </summary>
public sealed class InsertBeforeOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "INSERT_BEFORE";

    public string Anchor { get; init; } = "";
    public string Text { get; init; } = "";
    public int Occurrence { get; init; } = 1;
    public bool CaseSensitive { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        var match = AnchorHelper.FindAnchor(doc, Anchor, Occurrence, CaseSensitive);
        if (match is null)
            return OperationResult.Fail($"Anchor '{Anchor}' not found");

        doc.Insert(match.Value.Offset, Text);
        return OperationResult.Ok(charsInserted: Text.Length, resolvedOffset: match.Value.Offset);
    }
}

/// <summary>
/// Appends text at the end of the document.
/// </summary>
public sealed class AppendOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "APPEND";

    public string Text { get; init; } = "";

    public OperationResult Execute(TextDocument doc)
    {
        int insertAt = doc.Length;
        doc.Insert(insertAt, Text);
        return OperationResult.Ok(charsInserted: Text.Length, resolvedOffset: insertAt);
    }
}

/// <summary>
/// Prepends text at the beginning of the document.
/// </summary>
public sealed class PrependOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "PREPEND";

    public string Text { get; init; } = "";

    public OperationResult Execute(TextDocument doc)
    {
        doc.Insert(0, Text);
        return OperationResult.Ok(charsInserted: Text.Length, resolvedOffset: 0);
    }
}

/// <summary>
/// Replaces the text between (or including) two anchor strings.
/// </summary>
public sealed class ReplaceSectionOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "REPLACE_SECTION";

    public string StartAnchor { get; init; } = "";
    public string EndAnchor { get; init; } = "";
    public string Text { get; init; } = "";
    public int StartOccurrence { get; init; } = 1;
    public int EndOccurrence { get; init; } = 1;
    public bool IncludeAnchors { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        var startMatch = AnchorHelper.FindAnchor(doc, StartAnchor, StartOccurrence, caseSensitive: true);
        if (startMatch is null)
            return OperationResult.Fail($"Start anchor '{StartAnchor}' not found");

        // Search for end anchor starting from the end of the start anchor
        int searchFrom = startMatch.Value.Offset + startMatch.Value.Length;
        var opts = new SearchOptions { CaseSensitive = true };
        SearchMatch? endMatch = null;
        int count = 0;
        foreach (var m in doc.FindAll(EndAnchor, opts))
        {
            if (m.Offset < searchFrom)
                continue;
            count++;
            if (count == EndOccurrence)
            {
                endMatch = m;
                break;
            }
        }

        if (endMatch is null)
            return OperationResult.Fail($"End anchor '{EndAnchor}' not found");

        int replaceStart = IncludeAnchors ? startMatch.Value.Offset : startMatch.Value.Offset + startMatch.Value.Length;
        int replaceEnd = IncludeAnchors ? endMatch.Value.Offset + endMatch.Value.Length : endMatch.Value.Offset;
        int deleteLength = replaceEnd - replaceStart;

        doc.Replace(replaceStart, deleteLength, Text);
        return OperationResult.Ok(charsInserted: Text.Length, charsDeleted: deleteLength, resolvedOffset: replaceStart);
    }
}

/// <summary>
/// Deletes the text between (or including) two anchor strings.
/// </summary>
public sealed class DeleteSectionOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "DELETE_SECTION";

    public string StartAnchor { get; init; } = "";
    public string EndAnchor { get; init; } = "";
    public int StartOccurrence { get; init; } = 1;
    public int EndOccurrence { get; init; } = 1;
    public bool IncludeAnchors { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        // Delegate to ReplaceSectionOperation with empty replacement text
        var inner = new ReplaceSectionOperation
        {
            StartAnchor = StartAnchor,
            EndAnchor = EndAnchor,
            Text = "",
            StartOccurrence = StartOccurrence,
            EndOccurrence = EndOccurrence,
            IncludeAnchors = IncludeAnchors
        };
        return inner.Execute(doc);
    }
}
