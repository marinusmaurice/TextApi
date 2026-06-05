# All 27 Operations

Every operation implements `IDocumentOperation`, carries a JSON type discriminator, and returns an `OperationResult`. Operations are safe to use directly or within a [DocumentPipeline](pipeline.md).

**Navigation:** [Overview](index.md) | [Pipeline](pipeline.md) | [Examples](examples-operations.md) | [Serialization](serialization.md)

## IDocumentOperation

```csharp
public interface IDocumentOperation
{
	string Type { get; }                        // JSON discriminator e.g. "INSERT_AT"
	OperationResult Execute(TextDocument doc);
}
```

## OperationResult

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | Whether the operation succeeded. |
| `ErrorMessage` | `string?` | Failure reason (null on success). |
| `CharsInserted` | `int` | Characters added to the document. |
| `CharsDeleted` | `int` | Characters removed from the document. |
| `DeltaChars` | `int` | `CharsInserted - CharsDeleted`. Used by the pipeline for offset drift. |
| `MatchCount` | `int` | For pattern ops: number of matches found/replaced. |
| `FoundMatches` | `IReadOnlyList<string>` | Text of matches (FindAllOperation only). |
| `ExtractedText` | `string?` | Extracted text (ExtractSectionOperation only). |
| `ResolvedOffset` | `int` | Offset where the operation actually wrote. |

## Offset-Based Operations (3)

These implement `IOffsetAwareOperation`, which adds `ApplyDrift(int delta)`. The [pipeline](pipeline.md) calls this automatically to adjust offsets as earlier operations insert or delete characters.

### INSERT_AT

```csharp
new InsertAtOperation(int offset, string text)
```

Insert `text` at absolute character offset `offset`.

```csharp
new InsertAtOperation(10, "hello")
// JSON: {"type":"INSERT_AT","offset":10,"text":"hello"}
```

### DELETE_AT

```csharp
new DeleteAtOperation(int offset, int length)
```

Delete `length` characters starting at `offset`.

```csharp
new DeleteAtOperation(0, 5)
// JSON: {"type":"DELETE_AT","offset":0,"length":5}
```

### REPLACE_AT

```csharp
new ReplaceAtOperation(int offset, int deleteLength, string text)
```

Delete `deleteLength` chars at `offset`, then insert `text`.

```csharp
new ReplaceAtOperation(6, 5, "Everyone")
// JSON: {"type":"REPLACE_AT","offset":6,"deleteLength":5,"text":"Everyone"}
```

## Anchor-Based Operations (6)

Anchors are resolved at execution time by searching for the anchor string in the current document. No offset arithmetic required.

### INSERT_AFTER

```csharp
new InsertAfterOperation
{
	Anchor = "Work Experience",
	Text = "\n- TextAPI developer",
	Occurrence = 1,         // default: 1st match
	CaseSensitive = true   // default: true
}
// JSON: {"type":"INSERT_AFTER","anchor":"Work Experience","text":"\n- TextAPI developer"}
```

### INSERT_BEFORE

```csharp
new InsertBeforeOperation { Anchor = "## Section", Text = "<!-- new -->\n" }
```

### APPEND

```csharp
new AppendOperation { Text = "\n\n// End of file" }
// Inserts at doc.Length
```

### PREPEND

```csharp
new PrependOperation { Text = "// Copyright 2025\n" }
// Inserts at offset 0
```

### REPLACE_SECTION

Replace the content between two anchor strings. Use `IncludeAnchors = true` to also replace the anchors themselves.

```csharp
new ReplaceSectionOperation
{
	StartAnchor    = "/* BEGIN */",
	EndAnchor      = "/* END */",
	Text           = "new content here",
	IncludeAnchors = false   // default: replace only what's between the anchors
}
```

### DELETE_SECTION

```csharp
new DeleteSectionOperation
{
	StartAnchor    = "/* BEGIN */",
	EndAnchor      = "/* END */",
	IncludeAnchors = true
}
```

## Pattern-Based Operations (2)

### REPLACE_ALL

```csharp
new ReplaceAllOperation
{
	Find          = "TODO",
	Replace       = "DONE",
	CaseSensitive = false,  // default: true
	WholeWord     = false   // default: false
}
// result.MatchCount = number of replacements made
```

### REGEX_REPLACE

```csharp
new RegexReplaceOperation
{
	Pattern     = @"\d{3}-\d{3}-\d{4}",
	Replacement = "[REDACTED]"
}
// Uses .NET regex. Replacement supports capture group refs: $1, ${name}
```

## Line-Based Operations (4)

All line indexes are **0-based**.

### INSERT_LINE

```csharp
new InsertLineOperation { LineIndex = 3, Text = "new line content" }
// LineIndex 0 = before first line; LineCount = after last line
```

### DELETE_LINE

```csharp
new DeleteLineOperation { LineIndex = 2 }
// Deletes the line and its trailing newline
```

### REPLACE_LINE

```csharp
new ReplaceLineOperation { LineIndex = 0, Text = "replacement content" }
// Replaces line content only; newline is preserved
```

### INSERT_AFTER_LINE_MATCH

```csharp
new InsertAfterLineMatchOperation
{
	Pattern    = "def main",
	LineText   = "    # Auto-inserted comment",
	UseRegex   = false,
	Occurrence = 1
}
// Finds the Nth line whose text contains Pattern, inserts LineText after it
```

## Transform Operations (7)

**Note:** Most transform operations use `doc.Load()` internally, which resets the undo stack and the change-tracker baseline. This is the correct trade-off for whole-document transformations. Use them as a final step in a pipeline when undo history doesn't matter for the transformed result.

### NORMALISE_WHITESPACE

```csharp
new NormaliseWhitespaceOperation()
// Collapses runs of spaces/tabs within each line to a single space.
// Newlines are untouched.
```

### TRIM_TRAILING_WHITESPACE

```csharp
new TrimTrailingWhitespaceOperation()
// Removes trailing spaces/tabs from every line.
```

### CONVERT_CASE

```csharp
// Whole document
new ConvertCaseOperation { Mode = CaseMode.Upper }

// Between two anchors
new ConvertCaseOperation
{
	Mode        = CaseMode.TitleCase,
	StartAnchor = "## Name",
	EndAnchor   = "## End"
}
// CaseMode: Upper | Lower | TitleCase
```

### SORT_LINES

```csharp
new SortLinesOperation
{
	StartLine     = 0,      // default: 0
	EndLine       = null,   // default: last line
	Descending    = false,  // default: ascending
	CaseSensitive = true    // default: true
}
```

### DEDUPLICATE_LINES

```csharp
new DeduplicateLinesOperation
{
	ConsecutiveOnly = false  // false = remove all dupes; true = only adjacent dupes
}
```

### INDENT

```csharp
new IndentOperation
{
	StartLine = 2,
	EndLine   = 5,
	Prefix   = "    ",   // default: 4 spaces
	Dedent   = false     // true = remove prefix instead of adding it
}
// Uses doc.Replace() per line (not doc.Load()) — undo stack is preserved
```

### WRAP_LINES

```csharp
new WrapLinesOperation
{
	MaxColumns = 80,   // default: 80
	StartLine  = 0,    // default: 0
	EndLine    = null  // default: last line
}
// Hard-wraps long lines at word boundaries.
```

## Query Operations (Read-Only) (3)

These operations never mutate the document. They always return `Success = true`.

### FIND_ALL

```csharp
var op = new FindAllOperation
{
	Pattern       = @"\bTODO\b",
	UseRegex      = true,
	CaseSensitive = true,
	WholeWord     = false
};
var result = op.Execute(doc);
// result.MatchCount  = number of matches
// result.FoundMatches = list of matched strings
```

### EXTRACT_SECTION

```csharp
var op = new ExtractSectionOperation
{
	StartAnchor    = "<!-- start -->",
	EndAnchor      = "<!-- end -->",
	IncludeAnchors = false
};
var result = op.Execute(doc);
// result.ExtractedText = the text between the two anchors
```

### CONTAINS

```csharp
var op = new ContainsOperation
{
	Pattern  = "TODO",
	UseRegex = false
};
var result = op.Execute(doc);
// result.Success    = true always
// result.MatchCount = 1 if found, 0 if not
// result.ErrorMessage = "Pattern not found" when MatchCount == 0
```

## Structural Operations (2)

### MOVE_SECTION

Cuts a section defined by two anchor strings and inserts it at a destination anchor. The destination anchor is re-resolved after the cut (positions shift).

```csharp
new MoveSectionOperation
{
	SourceStartAnchor     = "[S]",
	SourceEndAnchor       = "[E]",
	DestinationAnchor     = "[DEST]",
	InsertAfterDestination = true   // default: true (insert after)
}
```

### SWAP_SECTIONS

Swaps the content of two non-overlapping sections. Section 1 must appear before section 2.

```csharp
new SwapSectionsOperation
{
	Section1Start = "[S1]", Section1End = "[E1]",
	Section2Start = "[S2]", Section2End = "[E2]"
}
// result.CharsInserted == result.CharsDeleted (symmetric swap)
```

## Quick Reference

| JSON Type | Class | Category |
|---|---|---|
| `INSERT_AT` | `InsertAtOperation` | Offset |
| `DELETE_AT` | `DeleteAtOperation` | Offset |
| `REPLACE_AT` | `ReplaceAtOperation` | Offset |
| `INSERT_AFTER` | `InsertAfterOperation` | Anchor |
| `INSERT_BEFORE` | `InsertBeforeOperation` | Anchor |
| `APPEND` | `AppendOperation` | Anchor |
| `PREPEND` | `PrependOperation` | Anchor |
| `REPLACE_SECTION` | `ReplaceSectionOperation` | Anchor |
| `DELETE_SECTION` | `DeleteSectionOperation` | Anchor |
| `REPLACE_ALL` | `ReplaceAllOperation` | Pattern |
| `REGEX_REPLACE` | `RegexReplaceOperation` | Pattern |
| `INSERT_LINE` | `InsertLineOperation` | Line |
| `DELETE_LINE` | `DeleteLineOperation` | Line |
| `REPLACE_LINE` | `ReplaceLineOperation` | Line |
| `INSERT_AFTER_LINE_MATCH` | `InsertAfterLineMatchOperation` | Line |
| `NORMALISE_WHITESPACE` | `NormaliseWhitespaceOperation` | Transform |
| `TRIM_TRAILING_WHITESPACE` | `TrimTrailingWhitespaceOperation` | Transform |
| `CONVERT_CASE` | `ConvertCaseOperation` | Transform |
| `SORT_LINES` | `SortLinesOperation` | Transform |
| `DEDUPLICATE_LINES` | `DeduplicateLinesOperation` | Transform |
| `INDENT` | `IndentOperation` | Transform |
| `WRAP_LINES` | `WrapLinesOperation` | Transform |
| `FIND_ALL` | `FindAllOperation` | Query |
| `EXTRACT_SECTION` | `ExtractSectionOperation` | Query |
| `CONTAINS` | `ContainsOperation` | Query |
| `MOVE_SECTION` | `MoveSectionOperation` | Structural |
| `SWAP_SECTIONS` | `SwapSectionsOperation` | Structural |

Next: [Pipeline & Rollback](pipeline.md) · [JSON Serialization](serialization.md)
