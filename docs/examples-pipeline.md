# Examples: Pipeline

Pipeline and batch processing examples.

**Navigation:** [Examples Hub](examples.md) | [Pipeline Reference](pipeline.md) | [JSON Serialization](serialization.md)

## Basic Pipeline

```csharp
var doc = new TextDocument();
doc.Load("TODO: fix bug\nTODO: write tests\nTODO: deploy");

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
	.Add(new TrimTrailingWhitespaceOperation())
	.Execute();

Console.WriteLine(result.Success);  // true
Console.WriteLine(doc.GetText());   // "DONE: fix bug\n..."
```

## Error Handling with Rollback

```csharp
var doc = new TextDocument();
doc.Load("content");

var snapshot = doc.GetText();

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "missing", Replace = "x" })  // won't match
	.Add(new InsertAtOperation(0, "X"))  // won't execute
	.Execute();

Console.WriteLine(result.Success);        // false
Console.WriteLine(result.WasRolledBack);  // true
Console.WriteLine(doc.GetText());         // "content" (restored)
```

## Audit Trail

```csharp
var doc = new TextDocument();
doc.Load("test");

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "test", Replace = "done" })
	.Add(new AppendOperation { Text = "\nend" })
	.Execute();

foreach (var entry in result.AuditLog.Entries)
{
	Console.WriteLine($"{entry.OperationType}");
	Console.WriteLine($"  Success: {entry.Success}");
	Console.WriteLine($"  Inserted: {entry.CharsInserted}, Deleted: {entry.CharsDeleted}");
	if (entry.MatchCount > 0)
		Console.WriteLine($"  Matches: {entry.MatchCount}");
}
```

## Cancellation

```csharp
using var cts = new CancellationTokenSource();
cts.CancelAfter(TimeSpan.FromSeconds(5));

var ops = new List<IDocumentOperation>
{
	new ReplaceAllOperation { Find = "a", Replace = "x" },
	new SortLinesOperation(),
	new DeduplicateLinesOperation()
};

var result = new DocumentPipeline(doc)
	.Add(ops)
	.Execute(cts.Token);

if (!result.Success)
	Console.WriteLine($"Error: {result.ErrorMessage}");  // "Pipeline cancelled"
```

## Large Batch Processing

```csharp
var doc = new TextDocument();
doc.Load(largeContent);

var snapshot = doc.GetText();

var result = new DocumentPipeline(doc)
	.Add(new NormaliseWhitespaceOperation())
	.Add(new TrimTrailingWhitespaceOperation())
	.Add(new SortLinesOperation())
	.Execute();

if (result.Success)
{
	Console.WriteLine(result.AuditLog.Summary);
}
else
{
	doc.Load(snapshot);  // manual restore if needed
}
```

## JSON Operations Pipeline

```csharp
var json = """
{
  "operations": [
	{ "type": "REPLACE_ALL", "find": "TODO", "replace": "DONE" },
	{ "type": "TRIM_TRAILING_WHITESPACE" },
	{ "type": "SORT_LINES", "descending": false }
  ]
}
""";

var ops = OperationMapper.FromPipelineJson(json);
var result = new DocumentPipeline(doc).Add(ops).Execute();

Console.WriteLine($"Success: {result.Success}");
Console.WriteLine($"Summary: {result.AuditLog.Summary}");
```
