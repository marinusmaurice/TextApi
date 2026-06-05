# Diff

`TextDiff` computes the minimum edit script between two texts using the Myers O(ND) algorithm. Results come as structured `DiffHunk` lists, character-level `DiffSpan` sequences, or rendered unified-diff strings.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Examples](examples-search.md) | [Advanced](advanced.md)

## Quick Reference

```csharp
using TextAPI.Core.Diff;

// Line-level diff of two strings
DiffResult result = TextDiff.Diff(oldText, newText);

Console.WriteLine($"+{result.AddedLines} / -{result.DeletedLines}");
Console.WriteLine(result.ToUnifiedDiff());

// Character-level diff
var spans = TextDiff.DiffChars("Hello World", "Hello TextAPI");
foreach (var s in spans)
	Console.WriteLine($"{s.Kind}: \"{s.Text}\"");
```

## TextDiff Static Methods

| Method | Returns | Description |
|---|---|---|
| `Diff(string old, string new, opts?)` | `DiffResult` | Line-level diff of two strings. |
| `Diff(string[] old, string[] new, opts?)` | `DiffResult` | Line-level diff of pre-split line arrays. |
| `Diff(TextDocument old, TextDocument new, opts?)` | `DiffResult` | Line-level diff of two documents. |
| `DiffChars(string old, string new)` | `IReadOnlyList<DiffSpan>` | Character-level diff. |

## DiffOptions

| Property | Type | Default | Description |
|---|---|---|---|
| `IgnoreCase` | `bool` | `false` | Treat uppercase and lowercase as equal. |
| `IgnoreWhitespace` | `bool` | `false` | Trim and collapse runs of whitespace before comparing. |
| `MaxEditDistance` | `int` | `int.MaxValue` | Abort if edit distance exceeds this. Returns partial result. |

```csharp
var opts = new DiffOptions
{
	IgnoreCase       = true,
	IgnoreWhitespace = true
};

DiffResult r = TextDiff.Diff(a, b, opts);

// Or use the defaults
DiffResult r2 = TextDiff.Diff(a, b, DiffOptions.Default);
```

## DiffResult

| Property | Type | Description |
|---|---|---|
| `Hunks` | `IReadOnlyList<DiffHunk>` | All hunks in document order. |
| `AddedLines` | `int` | Total lines inserted across all hunks. |
| `DeletedLines` | `int` | Total lines deleted across all hunks. |
| `HasChanges` | `bool` | True when the two texts differ. |
| `ToUnifiedDiff(old?, new?, ctx?)` | `string` | Render as unified diff. Default 3 context lines. |

## DiffHunk

| Property | Type | Description |
|---|---|---|
| `Kind` | `DiffKind` | `Equal`, `Insert`, or `Delete`. |
| `OldStart` | `int` | Zero-based start line in the old document. |
| `OldCount` | `int` | Number of lines from the old document. |
| `NewStart` | `int` | Zero-based start line in the new document. |
| `NewCount` | `int` | Number of lines from the new document. |
| `Lines` | `IReadOnlyList<string>` | Line content for this hunk. |

## DiffKind Enum

| Value | Meaning |
|---|---|
| `Equal` | Lines are the same in both versions. |
| `Insert` | Lines were added in the new version. |
| `Delete` | Lines were removed from the old version. |

## DiffSpan (Character-Level)

```csharp
readonly record struct DiffSpan(DiffKind Kind, string Text);
```

Each `DiffSpan` is a contiguous run of equal, inserted, or deleted characters. Use this for inline diff rendering in a UI:

```csharp
var spans = TextDiff.DiffChars("the cat sat", "the bat sat");
// Equal:"the ", Delete:"c", Insert:"b", Equal:"at sat"

foreach (var s in spans)
{
	string prefix = s.Kind switch
	{
		DiffKind.Insert => "+",
		DiffKind.Delete => "-",
		_               => " "
	};
	Console.Write($"{prefix}{s.Text}");
}
```

## Unified Diff Output

```csharp
var result = TextDiff.Diff(oldText, newText);

// Default: 3 context lines, paths "a" and "b"
string patch = result.ToUnifiedDiff();

// Custom paths and context
string patch2 = result.ToUnifiedDiff(
	oldPath:      "original/file.cs",
	newPath:      "modified/file.cs",
	contextLines: 5
);
```

Output format:

```
--- original/file.cs
+++ modified/file.cs
@@ -1,4 +1,4 @@
 line 1
-old line
+new line
 line 3
```

## Iterating Hunks

```csharp
var result = TextDiff.Diff(oldDoc, newDoc);

foreach (var hunk in result.Hunks.Where(h => h.Kind != DiffKind.Equal))
{
	string symbol = hunk.Kind == DiffKind.Insert ? "+" : "-";
	foreach (var line in hunk.Lines)
		Console.WriteLine($"{symbol} {line}");
}
```

## Performance Notes

- Uses the Myers O(ND) algorithm — optimal for typical source code diffs.
- `MaxEditDistance` can be used to cap cost on very large or wildly different files.
- Character-level diff (`DiffChars`) is best for single words or short lines. For long texts prefer line-level diff first, then character diff per changed line.
