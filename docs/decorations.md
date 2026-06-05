# Decorations

Decorations attach metadata to character ranges — squiggles, syntax highlights, search highlights, bookmarks, and custom data. They automatically track text edits: an insert before a decoration shifts its offsets; a delete that encompasses the decoration removes it.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Examples](examples-search.md) | [Search](search.md)

## Quick Reference

```csharp
using TextAPI.Core.Decorations;

// Add an error squiggle on chars 10–20
Guid id = doc.AddDecoration(start: 10, end: 20, DecorationType.ErrorSquiggle, tag: "CS0101");

// Remove it later
doc.RemoveDecoration(id);

// Query decorations overlapping a range
foreach (var d in doc.GetDecorationsInRange(0, 100))
	Console.WriteLine($"{d.Type} [{d.Start},{d.End}) tag={d.Tag}");
```

## DecorationType Enum

| Value | Typical Use |
|---|---|
| `SyntaxHighlight` | Token colouring from the active tokeniser. |
| `ErrorSquiggle` | Red underline for errors. |
| `WarningSquiggle` | Yellow underline for warnings. |
| `InfoSquiggle` | Blue underline for informational hints. |
| `Selection` | Current cursor selection highlight. |
| `SearchMatch` | Find-and-replace match highlights. |
| `Bookmark` | Named bookmark on a range. |
| `Custom` | Application-defined decorations. |

## Decoration Properties

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique identifier. Use this to remove the decoration later. |
| `Start` | `int` | Start offset (inclusive). Updated automatically on edits. |
| `End` | `int` | End offset (exclusive). Updated automatically on edits. |
| `Length` | `int` | `End - Start`. |
| `Type` | `DecorationType` | Category of this decoration. |
| `Tag` | `string?` | Short string label (e.g. error code, token type). |
| `Data` | `object?` | Caller-supplied payload (e.g. diagnostic object, tooltip). |

## TextDocument Decoration API

| Method | Returns | Description |
|---|---|---|
| `AddDecoration(start, end, type, tag?, data?)` | `Guid` | Add a decoration and return its ID. |
| `RemoveDecoration(id)` | `void` | Remove decoration by ID. |
| `GetDecorationsInRange(start, end)` | `IEnumerable<Decoration>` | All decorations that overlap the given range. |

## Offset Tracking

Decorations automatically update their offsets when the document is edited:

- **Insert before**: offsets shift right
- **Insert inside**: decoration grows (end shifts right)
- **Delete that encompasses**: decoration is removed automatically
- **Delete that partially overlaps**: end shifts left proportionally

```csharp
var id = doc.AddDecoration(10, 20, DecorationType.Custom);
doc.Insert(5, "hello");  // +5 chars before the decoration
var d = doc.GetDecorationsInRange(0, 100).FirstOrDefault();
Console.WriteLine(d.Start);  // 15 (was 10, shifted by +5)
```
