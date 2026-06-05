# Examples: TextDocument

TextDocument API examples.

**Navigation:** [Examples Hub](examples.md) | [TextDocument Reference](textdocument.md) | [All Operations](examples-operations.md)

## Load and Basic Operations

```csharp
var doc = new TextDocument();

// Load from string
doc.Load("Hello\nWorld");

// Read text
Console.WriteLine(doc.GetText());        // "Hello\nWorld"
Console.WriteLine(doc.Length);           // 11
Console.WriteLine(doc.LineCount);        // 2
Console.WriteLine(doc.GetLine(0));       // "Hello"

// Position conversion
var (line, col) = doc.OffsetToPosition(6);  // offset 6 → line 1, col 0
int offset = doc.PositionToOffset(0, 5);    // line 0, col 5 → offset 5
```

## Insert, Delete, Replace

```csharp
var doc = new TextDocument();
doc.Load("Hello World");

// Insert
doc.Insert(5, " Beautiful");
Console.WriteLine(doc.GetText());  // "Hello Beautiful World"

// Delete
doc.Delete(5, 10);
Console.WriteLine(doc.GetText());  // "Hello World"

// Replace
doc.Replace(0, 5, "Hi");
Console.WriteLine(doc.GetText());  // "Hi World"

// Undo
doc.Undo();
Console.WriteLine(doc.GetText());  // "Hello World"
```

## Save and Load Files

```csharp
var doc = new TextDocument();

// Load with auto-detected encoding
await doc.LoadFileAsync("document.txt");

// Check what was detected
Console.WriteLine($"Encoding: {doc.DetectedEncoding?.Encoding.EncodingName}");
Console.WriteLine($"EOL Style: {doc.OriginalEolStyle}");
Console.WriteLine($"Has BOM: {doc.HasBom}");

// Edit
doc.Insert(0, "// Auto-generated\n");

// Save (preserves original encoding and EOL)
await doc.SaveFileAsync();

// Or save with different encoding
doc.SaveEncoding = System.Text.Encoding.UTF8;
await doc.SaveFileAsync("output.txt");
```

## Search Operations

```csharp
var doc = new TextDocument();
doc.Load("TODO: fix bug\nTODO: write tests\nDone: deploy");

// Find all
var matches = doc.FindAll("TODO").ToList();
Console.WriteLine($"Found {matches.Count} matches");

// Find next/prev
var next = doc.FindNext("TODO", 0);
if (next.HasValue)
{
	var text = doc.GetText(next.Value.Offset, next.Value.Length);
	Console.WriteLine($"Found: {text}");
}

// Count matches
int count = doc.CountMatches("TODO");
Console.WriteLine($"Total: {count}");

// Case-insensitive search
var opts = new SearchOptions { CaseSensitive = false };
var results = doc.FindAll("todo", opts);
```

## Decorations

```csharp
var doc = new TextDocument();
doc.Load("Hello World");

// Add a decoration
Guid id = doc.AddDecoration(
	start: 0, 
	end: 5, 
	DecorationType.SyntaxHighlight, 
	tag: "keyword"
);

// Query decorations
var decorations = doc.GetDecorationsInRange(0, 11).ToList();
foreach (var d in decorations)
	Console.WriteLine($"{d.Type} [{d.Start},{d.End})");

// Remove
doc.RemoveDecoration(id);
```
