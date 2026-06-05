using System.Text.Json.Serialization;
using TextAPI.Core;
using TextAPI.Core.Search;

namespace TextAPI.Operations;

/// <summary>
/// Returns all matches of a pattern. Does not mutate the document.
/// </summary>
public sealed class FindAllOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "FIND_ALL";

    public string Pattern { get; init; } = "";
    public bool UseRegex { get; init; } = false;
    public bool CaseSensitive { get; init; } = true;
    public bool WholeWord { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Pattern))
            return OperationResult.Fail("Pattern cannot be empty");

        var opts = new SearchOptions
        {
            UseRegex = UseRegex,
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord
        };

        var matches = new List<string>();
        foreach (var m in doc.FindAll(Pattern, opts))
            matches.Add(doc.GetText(m.Offset, m.Length));

        return new OperationResult
        {
            Success = true,
            MatchCount = matches.Count,
            FoundMatches = matches
        };
    }
}

/// <summary>
/// Extracts the text between two anchor strings. Does not mutate the document.
/// </summary>
public sealed class ExtractSectionOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "EXTRACT_SECTION";

    public string StartAnchor { get; init; } = "";
    public string EndAnchor { get; init; } = "";
    public bool IncludeAnchors { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
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

        int extractStart = IncludeAnchors ? startMatch.Value.Offset : startMatch.Value.Offset + startMatch.Value.Length;
        int extractEnd = IncludeAnchors ? endMatch.Value.Offset + endMatch.Value.Length : endMatch.Value.Offset;
        int extractLength = extractEnd - extractStart;

        string extracted = doc.GetText(extractStart, extractLength);
        return new OperationResult
        {
            Success = true,
            ExtractedText = extracted,
            ResolvedOffset = extractStart
        };
    }
}

/// <summary>
/// Checks whether the document contains a pattern. Does not mutate the document.
/// Always succeeds (Success=true); use MatchCount to check whether the pattern was found.
/// To fail a pipeline when a pattern is absent, use <see cref="AssertContainsOperation"/> instead.
/// </summary>
public sealed class ContainsOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "CONTAINS";

    public string Pattern { get; init; } = "";
    public bool UseRegex { get; init; } = false;
    public bool CaseSensitive { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Pattern))
            return OperationResult.Fail("Pattern cannot be empty");

        var opts = new SearchOptions { UseRegex = UseRegex, CaseSensitive = CaseSensitive };
        int count = doc.CountMatches(Pattern, opts);

        return new OperationResult
        {
            Success = true,
            MatchCount = count
        };
    }
}

/// <summary>
/// Asserts that the document contains a pattern. Does not mutate the document.
/// Returns Success=false (triggering pipeline rollback) when the pattern is not found.
/// </summary>
public sealed class AssertContainsOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "ASSERT_CONTAINS";

    public string Pattern { get; init; } = "";
    public bool UseRegex { get; init; } = false;
    public bool CaseSensitive { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Pattern))
            return OperationResult.Fail("Pattern cannot be empty");

        var opts = new SearchOptions { UseRegex = UseRegex, CaseSensitive = CaseSensitive };
        int count = doc.CountMatches(Pattern, opts);

        return count > 0
            ? OperationResult.Ok(matchCount: count)
            : OperationResult.Fail($"Assertion failed: pattern '{Pattern}' not found in document");
    }
}
