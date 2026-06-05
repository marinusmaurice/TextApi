# Examples

Code examples demonstrating TextAPI features.

**Navigation:** [Overview](index.md) | [Quick Start](quickstart.md) | [All Examples](#examples-hub)

## Example: Text Statistics

```csharp
var doc = new TextDocument();
doc.Load(File.ReadAllText("document.txt"));

var stats = doc.GetStats();
Console.WriteLine($"Lines: {stats.LineCount}");
Console.WriteLine($"Words: {stats.WordCount}");
Console.WriteLine($"Characters: {stats.CharCount}");
```

## Example: Search and Replace

```csharp
var doc = new TextDocument();
doc.Load("Hello World\nHello TextAPI");

var matches = doc.FindAll("Hello").ToList();
Console.WriteLine($"Found {matches.Count} matches");

doc.ReplaceAll("Hello", "Hi");
Console.WriteLine(doc.GetText());  // "Hi World\nHi TextAPI"
```

## Example: Undo Stack

```csharp
var doc = new TextDocument();
doc.Load("Start");

doc.Insert(0, "A");
doc.Insert(1, "B");
doc.Insert(2, "C");

Console.WriteLine(doc.GetText());  // "ABCStart"

doc.Undo();
Console.WriteLine(doc.GetText());  // "Start"
```

## Example: Multi-Cursor Editing

```csharp
var doc = new TextDocument();
doc.Load("line1\nline2\nline3\nline4\nline5");

var mc = new MultiCursor(doc);
mc.AddColumnSelection(0, 4, 0);  // Column 0, lines 0-4

mc.InsertText("// ");

Console.WriteLine(doc.GetText());
// // line1
// // line2
// // line3
// // line4
// // line5

doc.Undo();  // removes all five comment markers in one step
```

## Example: Pipeline with Rollback

```csharp
var doc = new TextDocument();
doc.Load("TODO: fix bug\nTODO: write tests");

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
	.Add(new InsertLineOperation { LineIndex = 0, Text = "Header" })
	.Execute();

if (result.Success)
{
	Console.WriteLine("Pipeline succeeded");
	Console.WriteLine(result.AuditLog.Summary);
}
else
{
	Console.WriteLine($"Pipeline failed at op {result.FailedOperationIndex}");
	Console.WriteLine($"Rolled back: {result.WasRolledBack}");  // true
}
```

## Example: JSON Operations

```csharp
var json = """
[
  { "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
  { "type": "TRIM_TRAILING_WHITESPACE" }
]
""";

var doc = new TextDocument();
doc.Load("foo   \nbar  \n");

var ops = OperationMapper.FromJson(json);
var result = new DocumentPipeline(doc).Add(ops).Execute();

Console.WriteLine(doc.GetText());  // "bar\nbar\n"
```

## Example: Diff Two Documents

```csharp
var doc1 = new TextDocument();
doc1.Load("line 1\nline 2\nline 3");

var doc2 = new TextDocument();
doc2.Load("line 1\nline 2 modified\nline 3");

var diff = TextDiff.Diff(doc1, doc2);

foreach (var hunk in diff.Hunks.Where(h => h.Kind != DiffKind.Equal))
{
	Console.WriteLine($"{hunk.Kind}:");
	foreach (var line in hunk.Lines)
		Console.WriteLine($"  {line}");
}
```
