# Examples: Search

Search functionality examples.

**Navigation:** [Examples Hub](examples.md) | [Search Reference](search.md) | [Decorations Reference](decorations.md)

## Basic Search

```csharp
var doc = new TextDocument();
doc.Load("TODO: fix\nTODO: test\nDone: ship");

// Find first
var m = doc.FindFirst("TODO");
if (m.HasValue)
{
	var text = doc.GetText(m.Value.Offset, m.Value.Length);
	Console.WriteLine($"Found: {text}");
}

// Find next
var next = doc.FindNext("TODO", m.Value.Offset + 1);

// Find previous
var prev = doc.FindPrev("TODO", m.Value.Offset);
```

## Find All with LINQ

```csharp
var doc = new TextDocument();
doc.Load("apple banana apple cherry apple");

// Find all
var all = doc.FindAll("apple").ToList();
Console.WriteLine($"Total: {all.Count}");

// First 2 only (lazy)
var first2 = doc.FindAll("apple").Take(2);

// Filter by position
var afterOffset10 = doc.FindAll("apple")
	.Where(m => m.Offset >= 10);
```

## Case-Insensitive Search

```csharp
var doc = new TextDocument();
doc.Load("TODO\nTodo\ntodo\nToDo");

var opts = new SearchOptions { CaseSensitive = false };
var matches = doc.FindAll("todo", opts);

Console.WriteLine($"Found {matches.Count()} matches");
```

## Regex Search

```csharp
var doc = new TextDocument();
doc.Load("email@domain.com\ntest@example.org\nno-email");

var opts = new SearchOptions { UseRegex = true };
var emails = doc.FindAll(@"[\w.-]+@[\w.-]+\.\w+", opts);

foreach (var m in emails)
{
	var email = doc.GetText(m.Offset, m.Length);
	Console.WriteLine(email);
}
```

## Wrapping Search

```csharp
var doc = new TextDocument();
doc.Load("apple banana cherry apple banana");

var cursor = new TextCursor(doc, offset: 10);
string pattern = "apple";

// Search forward from current position
var m = doc.FindNext(pattern, cursor.CaretOffset + 1);

// Wrap around if not found
m ??= doc.FindNext(pattern, 0);

if (m.HasValue)
{
	cursor.MoveTo(m.Value.Offset);
	cursor.SelectTo(m.Value.Offset + m.Value.Length);
}
```

## Count Without Results

```csharp
var doc = new TextDocument();
doc.Load("foo bar foo baz foo");

// Count efficiently without materializing matches
int count = doc.CountMatches("foo");
Console.WriteLine($"Pattern appears {count} times");
```
