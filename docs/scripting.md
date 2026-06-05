# Script DSL

`ScriptRunner` executes a line-oriented command language directly on a `TextDocument`. No compilation step — paste a block of text, call `Run()`, get a result. Great for batch processing, macros, and AI-generated edits.

**Navigation:** [Overview](index.md) | [C# REPL](repl.md) | [Examples](examples-scripting.md) | [Scenarios](scenarios.md)

## Basic Usage

```csharp
using TextAPI.Core.Scripting;

var runner = new ScriptRunner(doc);

ScriptResult result = runner.Run("""
	REPLACE_ALL "TODO" "DONE"
	INSERT_AT 0 "// Auto-generated\n"
	DELETE_LINE 1
	GOTO 3 0
	INSERT "Hello"
""");

if (!result.Success)
	Console.WriteLine($"Error at line {result.ErrorLine}: {result.ErrorMessage}");
```

## All Commands

### Cursor Movement

| Command | Description |
|---|---|
| `MOVE <offset>` | Move cursor to absolute character offset. |
| `GOTO <line> [<col>]` | Move to 1-based line, optional 0-based column (default 0). |
| `SELECT <start> <end>` | Set cursor selection from `start` offset to `end` offset. |

### Insertion & Deletion

| Command | Description |
|---|---|
| `INSERT "<text>"` | Insert text at the current cursor position. |
| `INSERT_AT <offset> "<text>"` | Insert text at an explicit offset. |
| `DELETE <n>` | Delete `n` characters at the cursor. |
| `DELETE_AT <offset> <n>` | Delete `n` characters at an explicit offset. |
| `DELETE_LINE [<line>]` | Delete 1-based line number. Defaults to the cursor's current line. |

### Search & Replace

| Command | Description |
|---|---|
| `REPLACE_ALL "<find>" "<replace>"` | Replace all occurrences. |
| `FIND "<pattern>"` | Find first match and move cursor there. |

### Formatting

| Command | Description |
|---|---|
| `SORT_LINES` | Sort lines. |
| `TRIM_TRAILING_WHITESPACE` | Remove trailing spaces. |
| `DEDUPLICATE_LINES` | Remove duplicate lines. |

## ScriptResult

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | Whether all commands executed successfully. |
| `ErrorMessage` | `string?` | Error description (null if successful). |
| `ErrorLine` | `int` | Line number where error occurred (0-based). |
| `CharsInserted` | `int` | Total characters inserted. |
| `CharsDeleted` | `int` | Total characters deleted. |

## Example: Bulk Edit with Script DSL

```csharp
var runner = new ScriptRunner(doc);
var result = runner.Run("""
	GOTO 1 0
	SELECT 0 100
	REPLACE_ALL "TODO:" "FIXME:"
	TRIM_TRAILING_WHITESPACE
	SORT_LINES
""");

Console.WriteLine($"Done: {result.CharsDeleted} chars deleted, {result.CharsInserted} inserted");
```
