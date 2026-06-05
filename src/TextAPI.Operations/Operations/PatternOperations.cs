using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using TextAPI.Core;
using TextAPI.Core.Search;

namespace TextAPI.Operations;

/// <summary>
/// Replaces all occurrences of a literal string (optionally whole-word or case-insensitive).
/// </summary>
public sealed class ReplaceAllOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "REPLACE_ALL";

    public string Find { get; init; } = "";
    public string Replace { get; init; } = "";
    public bool CaseSensitive { get; init; } = true;
    public bool WholeWord { get; init; } = false;

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Find))
            return OperationResult.Fail("Find pattern cannot be empty");

        var opts = new SearchOptions
        {
            CaseSensitive = CaseSensitive,
            WholeWord = WholeWord
        };
        int count = doc.ReplaceAll(Find, Replace, opts);
        return OperationResult.Ok(matchCount: count);
    }
}

/// <summary>
/// Replaces all matches of a .NET regular expression pattern.
/// </summary>
public sealed class RegexReplaceOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "REGEX_REPLACE";

    public string Pattern { get; init; } = "";
    public string Replacement { get; init; } = "";

    public OperationResult Execute(TextDocument doc)
    {
        if (string.IsNullOrEmpty(Pattern))
            return OperationResult.Fail("Pattern cannot be empty");

        // Validate the regex before sending it to the document layer
        try
        {
            _ = new Regex(Pattern);
        }
        catch (ArgumentException ex)
        {
            return OperationResult.Fail($"Invalid regex pattern: {ex.Message}");
        }

        var opts = new SearchOptions { UseRegex = true, CaseSensitive = true };
        int count = doc.ReplaceAll(Pattern, Replacement, opts);
        return OperationResult.Ok(matchCount: count);
    }
}
