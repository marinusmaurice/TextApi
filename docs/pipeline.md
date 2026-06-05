# Pipeline & Rollback

`DocumentPipeline` runs a sequence of operations atomically. Any failure rolls back the document to the exact state it was in before the pipeline started — no partial mutations.

**Navigation:** [Overview](index.md) | [Operations](operations.md) | [Examples](examples-pipeline.md) | [Serialization](serialization.md)

## Basic Usage

```csharp
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

var pipeline = new DocumentPipeline(doc);

pipeline
	.Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
	.Add(new TrimTrailingWhitespaceOperation())
	.Add(new SortLinesOperation());

PipelineResult result = pipeline.Execute();
```

## Chaining (fluent API)

```csharp
var result = new DocumentPipeline(doc)
	.Add(new PrependOperation   { Text = "// generated\n" })
	.Add(new ReplaceAllOperation  { Find = "old", Replace = "new" })
	.Add(new AppendOperation    { Text = "\n// end" })
	.Execute();
```

## Adding Multiple Operations at Once

```csharp
var ops = OperationMapper.FromPipelineJson(jsonString);
pipeline.Add(ops);  // IEnumerable<IDocumentOperation>
```

## PipelineResult

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | True if all operations succeeded. |
| `ErrorMessage` | `string?` | Error message from the failed operation. |
| `FailedOperationIndex` | `int` | 0-based index of the failing operation. -1 on success. |
| `WasRolledBack` | `bool` | True if the document was restored to its pre-pipeline state. |
| `TextBefore` | `string` | Snapshot of the document before the pipeline ran. |
| `TextAfter` | `string` | Document text after the pipeline (equals TextBefore if rolled back). |
| `OperationResults` | `IReadOnlyList<OperationResult>` | Per-operation results (only for executed operations). |
| `AuditLog` | `AuditLog` | Structured audit trail. See below. |

```csharp
if (!result.Success)
{
	Console.WriteLine($"Failed at op #{result.FailedOperationIndex}: {result.ErrorMessage}");
	Console.WriteLine($"Rolled back: {result.WasRolledBack}");
	// document is guaranteed to be at its pre-pipeline state
}
```

## Audit Log

Every pipeline execution records a structured log of what happened:

```csharp
AuditLog log = result.AuditLog;

Console.WriteLine(log.Summary);             // "3 op(s) succeeded; +14/-3 chars"
Console.WriteLine(log.AllSucceeded);        // true/false
Console.WriteLine(log.TotalCharsInserted);  // total insertions across all ops
Console.WriteLine(log.TotalCharsDeleted);   // total deletions

foreach (AuditEntry entry in log.Entries)
{
	Console.WriteLine($"[{entry.TimestampUtc:HH:mm:ss.fff}] {entry.OperationType}");
	Console.WriteLine($"  success={entry.Success} +{entry.CharsInserted}/-{entry.CharsDeleted}");
	if (entry.MatchCount > 0) Console.WriteLine($"  matches={entry.MatchCount}");
	if (entry.ErrorMessage != null) Console.WriteLine($"  error: {entry.ErrorMessage}");
}
```

## Offset Drift Correction

When multiple offset-based operations run in sequence, earlier inserts/deletes shift the positions of later ones. The pipeline handles this automatically:

```csharp
// These two INSERT_AT operations work correctly in a pipeline:
// Op 1 inserts 6 chars at offset 0 → shifts all offsets by +6
// Op 2's offset is automatically adjusted from 10 → 16
new DocumentPipeline(doc)
	.Add(new InsertAtOperation(0,  "START "))   // +6 chars drift
	.Add(new InsertAtOperation(10, " END"))     // auto-adjusted to offset 16
	.Execute();
```

Drift correction applies only to operations implementing `IOffsetAwareOperation` (the three offset-based ops). Anchor-based operations re-resolve their anchors at execution time and are naturally drift-immune.

## Cancellation

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

var result = pipeline.Execute(cts.Token);
// If cancelled mid-run, document is rolled back and result.Success = false
// result.ErrorMessage = "Pipeline cancelled"
```

## Rollback Guarantee

The rollback mechanism is simple and reliable:

1. Before any operation runs, `snapshot = doc.GetText()` is captured.
2. On any failure (operation returns `Success=false`, throws an exception, or is cancelled), `doc.Load(snapshot)` is called immediately.
3. The document is guaranteed to be in its pre-pipeline state. No exception leaks out.

Because rollback uses `doc.Load()`, it resets the undo stack. The pre-pipeline state cannot be recovered via `Undo()`. If you need the pre-pipeline state preserved in undo history, snapshot it yourself before calling `Execute()`.
