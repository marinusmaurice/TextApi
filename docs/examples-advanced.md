# Examples: Advanced

Advanced usage patterns and real-world scenarios.

**Navigation:** [Examples Hub](examples.md) | [Advanced Topics](advanced.md) | [Scenarios](scenarios.md)

## Custom Tokenizer

```csharp
public class MyLanguageTokenizer : ITokenizer
{
	public string LanguageId => "mylang";

	public IReadOnlyList<Token> Tokenize(string text, int? maxTokens = null)
	{
		var tokens = new List<Token>();
		// Implement tokenization logic
		return tokens;
	}
}

var doc = new TextDocument(new MyLanguageTokenizer());
await doc.LoadFileAsync("script.mylang");
```

## Piece Table Direct Access

```csharp
var doc = new TextDocument();
doc.Load("test content");

// Access piece table for performance
var searcher = new TextSearcher(doc.PieceTable);
var matches = searcher.FindAll("test");

Console.WriteLine($"Found {matches.Count} matches");
```

## Memory Optimization

```csharp
var doc = new TextDocument();
await doc.LoadFileAsync("large_file.txt");

// Perform many edits
for (int i = 0; i < 1000; i++)
	doc.Insert(i * 10, "text");

// Compact the piece table to reduce overhead
doc.Compact();
```

## Document Statistics

```csharp
var doc = new TextDocument();
await doc.LoadFileAsync("document.txt");

var stats = doc.GetStats();
Console.WriteLine($"Characters: {stats.CharCount}");
Console.WriteLine($"Words: {stats.WordCount}");
Console.WriteLine($"Lines: {stats.LineCount}");
Console.WriteLine($"Paragraphs: {stats.ParagraphCount}");
Console.WriteLine($"Pieces: {stats.PieceCount}");
```

## Diff with Custom Options

```csharp
var oldDoc = new TextDocument();
oldDoc.Load(oldContent);

var newDoc = new TextDocument();
newDoc.Load(newContent);

var opts = new DiffOptions
{
	IgnoreCase = true,
	IgnoreWhitespace = true,
	MaxEditDistance = 1000
};

var diff = TextDiff.Diff(oldDoc, newDoc, opts);

foreach (var hunk in diff.Hunks)
{
	if (hunk.Kind == DiffKind.Equal) continue;

	Console.WriteLine($"{hunk.Kind}: lines {hunk.OldStart}-{hunk.OldStart + hunk.OldCount}");
	foreach (var line in hunk.Lines)
		Console.WriteLine($"  {line}");
}
```

## Complex Decoration Layers

```csharp
var doc = new TextDocument();
doc.Load(code);

// Layer 1: Syntax highlighting
for (int i = 0; i < syntaxTokens.Count; i++)
{
	var token = syntaxTokens[i];
	doc.AddDecoration(token.Start, token.End, DecorationType.SyntaxHighlight, tag: token.Type);
}

// Layer 2: Diagnostics
foreach (var error in diagnostics)
{
	doc.AddDecoration(error.Start, error.End, DecorationType.ErrorSquiggle, tag: error.Code);
}

// Layer 3: Search results
foreach (var match in searchResults)
{
	doc.AddDecoration(match.Offset, match.Offset + match.Length, DecorationType.SearchMatch);
}

// Query combined
var all = doc.GetDecorationsInRange(0, doc.Length).ToList();
Console.WriteLine($"Total decorations: {all.Count}");
```

## AI-Driven Batch Transformation

```csharp
async Task TransformWithAI(string sourceCode, string prompt)
{
	var doc = new TextDocument();
	doc.Load(sourceCode);

	// Get AI to generate operations
	var llmResponse = await CallLLM(prompt);
	var ops = OperationMapper.FromPipelineJson(llmResponse);

	// Execute atomically
	var result = new DocumentPipeline(doc)
		.Add(ops)
		.Execute();

	if (result.Success)
	{
		await doc.SaveFileAsync("output.txt");
		Console.WriteLine($"Transformed: {result.AuditLog.Summary}");
	}
	else
	{
		Console.WriteLine($"Failed: {result.ErrorMessage}");
	}
}
```
