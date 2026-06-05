namespace TextAPI.Operations;

/// <summary>
/// Records what happened during a single operation execution.
/// </summary>
public sealed class AuditEntry
{
    public required string OperationType { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int CharsInserted { get; init; }
    public int CharsDeleted { get; init; }
    public int MatchCount { get; init; }
    public DateTime TimestampUtc { get; init; }
    public string? Detail { get; init; }
}

/// <summary>
/// An append-only log of AuditEntry records produced by a pipeline run.
/// </summary>
public sealed class AuditLog
{
    private readonly List<AuditEntry> _entries;

    public AuditLog() => _entries = [];

    public AuditLog(IEnumerable<AuditEntry> entries) => _entries = [.. entries];

    public IReadOnlyList<AuditEntry> Entries => _entries;

    public int TotalCharsInserted => _entries.Sum(e => e.CharsInserted);
    public int TotalCharsDeleted => _entries.Sum(e => e.CharsDeleted);
    public bool AllSucceeded => _entries.Count > 0 && _entries.All(e => e.Success);

    /// <summary>
    /// One-line summary of the pipeline run.
    /// </summary>
    public string Summary
    {
        get
        {
            int total = _entries.Count;
            int failed = _entries.Count(e => !e.Success);
            int inserted = TotalCharsInserted;
            int deleted = TotalCharsDeleted;
            return failed == 0
                ? $"{total} op(s) succeeded; +{inserted}/-{deleted} chars"
                : $"{total - failed}/{total} op(s) succeeded ({failed} failed); +{inserted}/-{deleted} chars";
        }
    }
}
