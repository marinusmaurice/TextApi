using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextAPI.Operations.Serialization;

/// <summary>
/// Serializes and deserializes IDocumentOperation instances to/from JSON.
/// The JSON discriminator field is "type", matching each operation's Type property.
///
/// Supported type strings:
///   INSERT_AT, DELETE_AT, REPLACE_AT,
///   INSERT_AFTER, INSERT_BEFORE, APPEND, PREPEND,
///   REPLACE_SECTION, DELETE_SECTION,
///   REPLACE_ALL, REGEX_REPLACE,
///   INSERT_LINE, DELETE_LINE, REPLACE_LINE, INSERT_AFTER_LINE_MATCH,
///   NORMALISE_WHITESPACE, TRIM_TRAILING_WHITESPACE, CONVERT_CASE,
///   SORT_LINES, DEDUPLICATE_LINES, INDENT, WRAP_LINES,
///   FIND_ALL, EXTRACT_SECTION, CONTAINS, ASSERT_CONTAINS,
///   MOVE_SECTION, SWAP_SECTIONS
/// </summary>
public static class OperationMapper
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Deserializes a JSON array of operation objects into a list of IDocumentOperation.
    /// </summary>
    public static IReadOnlyList<IDocumentOperation> FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("Expected a JSON array of operation objects.");

        var ops = new List<IDocumentOperation>();
        foreach (var element in doc.RootElement.EnumerateArray())
            ops.Add(DeserializeOperation(element));
        return ops;
    }

    /// <summary>
    /// Deserializes a pipeline JSON object with an "operations" array.
    /// Example: { "operations": [ ... ] }
    /// </summary>
    public static IReadOnlyList<IDocumentOperation> FromPipelineJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("operations", out var opsElement))
            throw new JsonException("Expected a 'operations' property in the pipeline JSON object.");
        if (opsElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("'operations' must be a JSON array.");

        var ops = new List<IDocumentOperation>();
        foreach (var element in opsElement.EnumerateArray())
            ops.Add(DeserializeOperation(element));
        return ops;
    }

    /// <summary>
    /// Serializes a single operation to a JSON object string.
    /// </summary>
    public static string ToJson(IDocumentOperation op)
        => JsonSerializer.Serialize((object)op, op.GetType(), Options);

    /// <summary>
    /// Serializes a list of operations as a pipeline JSON object: { "operations": [ ... ] }.
    /// </summary>
    public static string ToPipelineJson(IEnumerable<IDocumentOperation> ops)
    {
        var list = ops.Select(op => JsonSerializer.SerializeToElement((object)op, op.GetType(), Options)).ToList();
        var wrapper = new { operations = list };
        return JsonSerializer.Serialize(wrapper, Options);
    }

    private static IDocumentOperation DeserializeOperation(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var typeProp))
            throw new JsonException("Operation object is missing required 'type' field.");

        string type = typeProp.GetString()
            ?? throw new JsonException("'type' field must be a non-null string.");

        string raw = element.GetRawText();

        return type switch
        {
            "INSERT_AT"                 => Deserialize<InsertAtOperation>(raw),
            "DELETE_AT"                 => Deserialize<DeleteAtOperation>(raw),
            "REPLACE_AT"                => Deserialize<ReplaceAtOperation>(raw),
            "INSERT_AFTER"              => Deserialize<InsertAfterOperation>(raw),
            "INSERT_BEFORE"             => Deserialize<InsertBeforeOperation>(raw),
            "APPEND"                    => Deserialize<AppendOperation>(raw),
            "PREPEND"                   => Deserialize<PrependOperation>(raw),
            "REPLACE_SECTION"           => Deserialize<ReplaceSectionOperation>(raw),
            "DELETE_SECTION"            => Deserialize<DeleteSectionOperation>(raw),
            "REPLACE_ALL"               => Deserialize<ReplaceAllOperation>(raw),
            "REGEX_REPLACE"             => Deserialize<RegexReplaceOperation>(raw),
            "INSERT_LINE"               => Deserialize<InsertLineOperation>(raw),
            "DELETE_LINE"               => Deserialize<DeleteLineOperation>(raw),
            "REPLACE_LINE"              => Deserialize<ReplaceLineOperation>(raw),
            "INSERT_AFTER_LINE_MATCH"   => Deserialize<InsertAfterLineMatchOperation>(raw),
            "NORMALISE_WHITESPACE"      => Deserialize<NormaliseWhitespaceOperation>(raw),
            "TRIM_TRAILING_WHITESPACE"  => Deserialize<TrimTrailingWhitespaceOperation>(raw),
            "CONVERT_CASE"              => Deserialize<ConvertCaseOperation>(raw),
            "SORT_LINES"                => Deserialize<SortLinesOperation>(raw),
            "DEDUPLICATE_LINES"         => Deserialize<DeduplicateLinesOperation>(raw),
            "INDENT"                    => Deserialize<IndentOperation>(raw),
            "WRAP_LINES"                => Deserialize<WrapLinesOperation>(raw),
            "FIND_ALL"                  => Deserialize<FindAllOperation>(raw),
            "EXTRACT_SECTION"           => Deserialize<ExtractSectionOperation>(raw),
            "CONTAINS"                  => Deserialize<ContainsOperation>(raw),
            "ASSERT_CONTAINS"           => Deserialize<AssertContainsOperation>(raw),
            "MOVE_SECTION"              => Deserialize<MoveSectionOperation>(raw),
            "SWAP_SECTIONS"             => Deserialize<SwapSectionsOperation>(raw),
            _ => throw new JsonException($"Unknown operation type '{type}'.")
        };
    }

    private static T Deserialize<T>(string json) where T : class
        => JsonSerializer.Deserialize<T>(json, Options)
           ?? throw new JsonException($"Failed to deserialize {typeof(T).Name}.");
}
