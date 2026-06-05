# Search

TextAPI's search engine uses Boyer-Moore-Horspool for plain-text patterns (O(n)) and .NET's `Regex` for regex patterns, both operating directly on the piece table with piece-boundary bridging so no full-text copy is needed.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Examples](examples-search.md) | [Decorations](decorations.md)

## Quick Reference

```csharp
using TextAPI.Core.Search;

// Case-insensitive regex find-all
var opts = new SearchOptions { CaseSensitive = false, UseRegex = true };

foreach (var m in doc.FindAll(@"\bTODO\b", opts))
	Console.WriteLine($"offset {m.Offset}, length {m.Length}");

// Simple forward scan
SearchMatch? next = doc.FindNext("hello", fromOffset: 0);

// Count occurrences
int n = doc.CountMatches("TODO");
```

## SearchOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `CaseSensitive` | `bool` | `true` | When false, performs a case-insensitive match. |
| `WholeWord` | `bool` | `false` | Match only when surrounded by non-word characters. |
| `UseRegex` | `bool` | `false` | Treat the pattern as a .NET regular expression. |
| `MaxResults` | `int` | `0` | Cap the number of results. 0 = unlimited. |

```csharp
// Plain-text, whole-word, case-insensitive
var opts = new SearchOptions
{
	CaseSensitive = false,
	WholeWord     = true
};

// Regex, limited to first 50 results
var opts2 = new SearchOptions { UseRegex = true, MaxResults = 50 };
```

## SearchMatch

| Field | Type | Description |
|---|---|---|
| `Offset` | `int` | Start offset of the match in the document. |
| `Length` | `int` | Character length of the match. |

```csharp
SearchMatch? m = doc.FindNext("World", 0);
if (m is not null)
{
	string text = doc.GetText(m.Value.Offset, m.Value.Length);
	Console.WriteLine($"Found: '{text}' at {m.Value.Offset}");
}
```

## TextDocument Search Methods

| Method | Returns | Description |
|---|---|---|
| `FindAll(pattern, opts?)` | `IEnumerable<SearchMatch>` | All matches in document order. Lazy enumeration. |
| `FindNext(pattern, fromOffset, opts?)` | `SearchMatch?` | First match at or after `fromOffset`. Null if none. |
| `FindPrev(pattern, beforeOffset, opts?)` | `SearchMatch?` | Last match before `beforeOffset`. Null if none. |
| `CountMatches(pattern, opts?)` | `int` | Count without materializing matches. O(n). |

## FindAll — Lazy Enumeration

`FindAll` returns an `IEnumerable` that searches lazily. You can `break` early or use LINQ without scanning the full document:

```csharp
// First 10 matches only
var first10 = doc.FindAll("TODO").Take(10).ToList();

// Only matches on even offsets (contrived, but shows LINQ composability)
var even = doc.FindAll("x").Where(m => m.Offset % 2 == 0);
```

## FindNext / FindPrev — Wrapping Search

Implement wrap-around search (like Ctrl+F "find next" in an editor):

```csharp
int caretOffset = cursor.CaretOffset;
string pattern = "hello";

// Try forward from current position
SearchMatch? m = doc.FindNext(pattern, caretOffset + 1);

// Wrap around if not found
m ??= doc.FindNext(pattern, 0);

if (m is not null)
	cursor.SetSelection(m.Value.Offset, m.Value.Offset + m.Value.Length);
```

## Regex Patterns

When `UseRegex = true`, the pattern is compiled as a .NET `Regex`. All .NET regex features are supported:

```csharp
var opts = new SearchOptions { UseRegex = true, CaseSensitive = false };

// Match any ISO date
var dates = doc.FindAll(@"\d{4}-\d{2}-\d{2}", opts);

// Match function declarations
var fns = doc.FindAll(@"^\s*(public|private|protected)\s+\w+\s+\w+\s*\(", opts);
```

## Search via TextSearcher (Direct)

The lower-level `TextSearcher` operates directly on the piece table. Use it when building a component that doesn't go through `TextDocument`:

```csharp
using TextAPI.Core.Search;

var searcher = new TextSearcher(doc.PieceTable);

SearchMatch?             first  = searcher.FindFirst("TODO");
IEnumerable<SearchMatch> all    = searcher.FindAll("TODO");
SearchMatch?             next   = searcher.FindNext("TODO", fromOffset: 100);
SearchMatch?             prev   = searcher.FindPrev("TODO", beforeOffset: 500);
int                      count  = searcher.Count("TODO");
```

## Performance Notes

- Plain-text search uses Boyer-Moore-Horspool: O(n) with sub-linear average case.
- Piece-boundary bridging means the piece table is never materialised to a single string for searching — essential for large files.
- Regex search materialises only the pieces that cross boundaries; the match itself runs on a `Span<char>`.
- Use `MaxResults` to cap results for UI autocomplete or preview scenarios.
