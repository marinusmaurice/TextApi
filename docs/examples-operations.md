# Examples: Operations

Operations and pipeline examples.

**Navigation:** [Examples Hub](examples.md) | [All 27 Operations](operations.md) | [Pipeline & Rollback](pipeline.md)

## Direct Operations

```csharp
var doc = new TextDocument();
doc.Load("old text");

// Direct operation execution
var op = new ReplaceAllOperation { Find = "old", Replace = "new" };
var result = op.Execute(doc);

Console.WriteLine(result.Success);        // true
Console.WriteLine(result.MatchCount);     // 1
Console.WriteLine(doc.GetText());         // "new text"
```

## Pipeline with Multiple Operations

```csharp
var doc = new TextDocument();
doc.Load("TODO: fix\n  TODO: test  \nDone: ship");

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
	.Add(new TrimTrailingWhitespaceOperation())
	.Add(new SortLinesOperation())
	.Execute();

if (result.Success)
{
	Console.WriteLine(result.AuditLog.Summary);
	// "3 op(s) succeeded; +0/-2 chars"
}
```

## Anchor-Based Operations

```csharp
var doc = new TextDocument();
doc.Load("/* BEGIN */\nold content\n/* END */");

// Replace content between anchors
var result = new DocumentPipeline(doc)
	.Add(new ReplaceSectionOperation
	{
		StartAnchor = "/* BEGIN */",
		EndAnchor = "/* END */",
		Text = "new content",
		IncludeAnchors = false
	})
	.Execute();

Console.WriteLine(doc.GetText());
// /* BEGIN */
// new content
// /* END */
```

## Line-Based Operations

```csharp
var doc = new TextDocument();
doc.Load("line1\nline2\nline3");

var result = new DocumentPipeline(doc)
	.Add(new InsertLineOperation 
	{ 
		LineIndex = 1,  // insert before line 2
		Text = "inserted" 
	})
	.Add(new DeleteLineOperation { LineIndex = 3 })
	.Execute();
```

## Transform Operations

```csharp
var doc = new TextDocument();
doc.Load("zulu\napple\nzulu\nbanana\napple");

var result = new DocumentPipeline(doc)
	.Add(new SortLinesOperation { CaseSensitive = false })
	.Add(new DeduplicateLinesOperation())
	.Add(new ConvertCaseOperation { Mode = CaseMode.Upper })
	.Execute();

Console.WriteLine(doc.GetText());
// APPLE
// BANANA
// ZULU
```

## Offset Drift Correction

```csharp
var doc = new TextDocument();
doc.Load("0123456789");

// Pipeline automatically adjusts offsets
var result = new DocumentPipeline(doc)
	.Add(new InsertAtOperation(0, "START "))   // +6 chars
	.Add(new InsertAtOperation(10, " END"))    // auto-adjusted to 16
	.Execute();

Console.WriteLine(doc.GetText());  // "START 0123456789 END"
```

## Query Operations (Read-Only)

```csharp
var doc = new TextDocument();
doc.Load("find this word and count");

// FindAll operation (doesn't modify)
var op = new FindAllOperation { Pattern = "\\w+", UseRegex = true };
var result = op.Execute(doc);

Console.WriteLine($"Words: {result.MatchCount}");
Console.WriteLine($"Matches: {string.Join(", ", result.FoundMatches)}");
```
