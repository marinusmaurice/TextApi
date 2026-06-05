namespace TextAPI.Operations.Pipeline;

/// <summary>
/// Represents the outcome of running a DocumentPipeline.
/// </summary>
public sealed class PipelineResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>0-based index of the first failed operation, or -1 if all succeeded.</summary>
    public int FailedOperationIndex { get; init; } = -1;

    public IReadOnlyList<OperationResult> OperationResults { get; init; } = [];
    public AuditLog AuditLog { get; init; } = new();

    /// <summary>Snapshot of the document text before the pipeline was executed.</summary>
    public string TextBefore { get; init; } = "";

    /// <summary>
    /// Document text after pipeline completion.
    /// If the pipeline was rolled back, this equals TextBefore.
    /// </summary>
    public string TextAfter { get; init; } = "";

    public int TotalCharsInserted => OperationResults.Sum(r => r.CharsInserted);
    public int TotalCharsDeleted => OperationResults.Sum(r => r.CharsDeleted);

    /// <summary>True when the pipeline did not complete successfully and the document was restored.</summary>
    public bool WasRolledBack => !Success;
}
