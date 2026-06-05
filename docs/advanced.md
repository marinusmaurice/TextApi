# Advanced Topics

Advanced usage patterns and architectural considerations for TextAPI.

**Navigation:** [Overview](index.md) | [Core API](textdocument.md) | [Examples](examples-advanced.md) | [Diff](diff.md)

## Piece Table Internals

The document is backed by a piece table data structure that maintains O(log n) insert/delete performance:

```csharp
// Access the piece table directly for performance-critical scenarios
var pieceTable = doc.PieceTable;

// Search directly on the piece table without materializing
var searcher = new TextSearcher(pieceTable);
var matches = searcher.FindAll("pattern");
```

## Custom Tokenizers

Implement your own syntax tokenizer for custom languages:

```csharp
public class MyTokenizer : ITokenizer
{
	public string LanguageId => "mylang";
	public IReadOnlyList<Token> Tokenize(string text, int? maxTokens = null)
	{
		// Implement tokenization
		return tokens;
	}
}

var doc = new TextDocument(new MyTokenizer());
```

## Memory Management

For very large documents, consider:

- **Compacting**: Call `doc.Compact()` after many edits to consolidate pieces
- **Streaming**: Process line-by-line instead of loading entire document
- **Read-only regions**: Mark unchangeable sections to optimize mutations

```csharp
// Reduce piece tree depth after heavy editing
doc.Compact();

// Check memory usage
var stats = doc.GetStats();
Console.WriteLine($"Pieces: {stats.PieceCount}");
```

## Diff Comparison

Compare documents efficiently:

```csharp
var oldDoc = new TextDocument();
oldDoc.Load(File.ReadAllText("v1.txt"));

var newDoc = new TextDocument();
newDoc.Load(File.ReadAllText("v2.txt"));

var diffResult = TextDiff.Diff(oldDoc, newDoc);
Console.WriteLine(diffResult.ToUnifiedDiff());
```

## Decorations for Complex UIs

Layer multiple decoration types for rich editing experiences:

```csharp
// Syntax highlighting
doc.AddDecoration(start, end, DecorationType.SyntaxHighlight, tag: "keyword");

// Error diagnostics
doc.AddDecoration(errStart, errEnd, DecorationType.ErrorSquiggle, tag: "CS0101");

// Search results
doc.AddDecoration(matchStart, matchEnd, DecorationType.SearchMatch);
```

## Change Tracking

Monitor what changed:

```csharp
// Enable change tracking
doc.IsModified = false;  // reset flag

doc.Insert(0, "text");
Console.WriteLine(doc.IsModified);  // true

doc.Undo();
Console.WriteLine(doc.IsModified);  // false (back to saved state)
```
