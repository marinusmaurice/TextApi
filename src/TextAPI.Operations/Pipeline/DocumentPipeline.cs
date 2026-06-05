using TextAPI.Core;

namespace TextAPI.Operations.Pipeline;

/// <summary>
/// Runs a sequence of IDocumentOperation instances against a TextDocument.
/// On any failure the document is restored to the pre-pipeline snapshot (rollback).
/// Offset-aware operations receive cumulative offset drift from earlier mutations.
/// </summary>
public sealed class DocumentPipeline
{
    private readonly TextDocument _doc;
    private readonly List<IDocumentOperation> _ops = [];

    public DocumentPipeline(TextDocument doc) { _doc = doc; }

    public DocumentPipeline Add(IDocumentOperation op) { _ops.Add(op); return this; }
    public DocumentPipeline Add(IEnumerable<IDocumentOperation> ops) { _ops.AddRange(ops); return this; }

    public PipelineResult Execute(CancellationToken ct = default)
    {
        string snapshot = _doc.GetText();
        var results = new List<OperationResult>(_ops.Count);
        var auditEntries = new List<AuditEntry>(_ops.Count);
        int cumulativeDelta = 0;

        for (int i = 0; i < _ops.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                _doc.Load(snapshot);
                return Fail(snapshot, results, auditEntries, i, "Pipeline cancelled");
            }

            var op = _ops[i];

            // Apply offset drift to position-aware operations
            if (op is IOffsetAwareOperation driftable)
                driftable.ApplyDrift(cumulativeDelta);

            OperationResult result;
            try
            {
                result = op.Execute(_doc);
            }
            catch (Exception ex)
            {
                _doc.Load(snapshot);
                result = OperationResult.Fail($"Exception in {op.Type}: {ex.Message}");
                auditEntries.Add(new AuditEntry
                {
                    OperationType = op.Type,
                    Success = false,
                    ErrorMessage = result.ErrorMessage,
                    TimestampUtc = DateTime.UtcNow
                });
                return Fail(snapshot, results, auditEntries, i, result.ErrorMessage!);
            }

            auditEntries.Add(new AuditEntry
            {
                OperationType = op.Type,
                Success = result.Success,
                ErrorMessage = result.ErrorMessage,
                CharsInserted = result.CharsInserted,
                CharsDeleted = result.CharsDeleted,
                MatchCount = result.MatchCount,
                TimestampUtc = DateTime.UtcNow,
                Detail = result.CharsInserted > 0 || result.CharsDeleted > 0 ? $"offset={result.ResolvedOffset}" : null
            });
            results.Add(result);

            if (!result.Success)
            {
                _doc.Load(snapshot);
                return Fail(snapshot, results, auditEntries, i, result.ErrorMessage!);
            }

            cumulativeDelta += result.DeltaChars;
        }

        return new PipelineResult
        {
            Success = true,
            OperationResults = results,
            AuditLog = new AuditLog(auditEntries),
            TextBefore = snapshot,
            TextAfter = _doc.GetText(),
            FailedOperationIndex = -1
        };
    }

    private static PipelineResult Fail(
        string snapshot,
        List<OperationResult> results,
        List<AuditEntry> entries,
        int failedIndex,
        string msg)
        => new()
        {
            Success = false,
            ErrorMessage = msg,
            FailedOperationIndex = failedIndex,
            OperationResults = results,
            AuditLog = new AuditLog(entries),
            TextBefore = snapshot,
            TextAfter = snapshot
        };
}
