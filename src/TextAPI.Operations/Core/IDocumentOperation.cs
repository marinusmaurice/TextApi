using TextAPI.Core;

namespace TextAPI.Operations;

/// <summary>
/// Base interface for all document operations.
/// </summary>
public interface IDocumentOperation
{
    string Type { get; }
    OperationResult Execute(TextDocument doc);
}

/// <summary>
/// Operations that are aware of their offset position in the document.
/// The pipeline calls ApplyDrift to correct for offset changes from prior operations.
/// </summary>
public interface IOffsetAwareOperation : IDocumentOperation
{
    /// <summary>
    /// Adjusts the stored Offset by delta to account for characters inserted or deleted by prior operations.
    /// </summary>
    void ApplyDrift(int delta);
}

/// <summary>
/// Encapsulates the result of a single operation execution.
/// </summary>
public sealed class OperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int CharsInserted { get; init; }
    public int CharsDeleted { get; init; }
    public int MatchCount { get; init; }
    public int ResolvedOffset { get; init; }

    /// <summary>
    /// Matched strings returned by query operations such as FindAllOperation.
    /// </summary>
    public IReadOnlyList<string> FoundMatches { get; init; } = [];

    /// <summary>
    /// Text extracted by ExtractSectionOperation.
    /// </summary>
    public string? ExtractedText { get; init; }

    /// <summary>
    /// Net character change: positive means the document grew, negative means it shrank.
    /// </summary>
    public int DeltaChars => CharsInserted - CharsDeleted;

    public static OperationResult Ok(
        int charsInserted = 0,
        int charsDeleted = 0,
        int matchCount = 0,
        int resolvedOffset = 0)
        => new()
        {
            Success = true,
            CharsInserted = charsInserted,
            CharsDeleted = charsDeleted,
            MatchCount = matchCount,
            ResolvedOffset = resolvedOffset
        };

    public static OperationResult Fail(string message)
        => new() { Success = false, ErrorMessage = message };
}
