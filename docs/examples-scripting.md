# Examples: Scripting

Script DSL examples.

**Navigation:** [Examples Hub](examples.md) | [Script DSL Reference](scripting.md) | [C# REPL](repl.md)

## Basic Script Execution

```csharp
var doc = new TextDocument();
doc.Load("Hello\nWorld");

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	GOTO 1 0
	INSERT "Greetings: "
""");

if (result.Success)
	Console.WriteLine(doc.GetText());
	// Hello
	// Greetings: World
```

## Replacing Content

```csharp
var doc = new TextDocument();
doc.Load("old old old\nold new old");

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	REPLACE_ALL "old" "new"
	TRIM_TRAILING_WHITESPACE
""");

Console.WriteLine(doc.GetText());
// new new new
// new new new
```

## Batch Editing

```csharp
var doc = new TextDocument();
doc.Load("line1\nline2\nline3\nline4\nline5");

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	GOTO 1 0
	INSERT_LINE 1 "inserted"
	DELETE_LINE 3
	SORT_LINES
""");

if (result.Success)
	Console.WriteLine($"Changes: +{result.CharsInserted} -{result.CharsDeleted}");
```

## Complex Script

```csharp
var doc = new TextDocument();
doc.Load("TODO: item1\nTODO: item2\nDone: item3");

var runner = new ScriptRunner(doc);
var script = """
	REPLACE_ALL "TODO" "PENDING"
	REPLACE_ALL "Done" "COMPLETED"
	SORT_LINES
	TRIM_TRAILING_WHITESPACE
""";

var result = runner.Run(script);

if (!result.Success)
{
	Console.WriteLine($"Error at line {result.ErrorLine}: {result.ErrorMessage}");
}
else
{
	Console.WriteLine("Success");
	Console.WriteLine(doc.GetText());
}
```

## Script with Navigation

```csharp
var doc = new TextDocument();
doc.Load("apple\nbanana\ncherry\ndate");

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	GOTO 2 0
	SELECT 0 6
	INSERT "grapes\n"
	GOTO 3 3
	DELETE 4
""");

Console.WriteLine(doc.GetText());
```

## Error Handling

```csharp
var doc = new TextDocument();
doc.Load("content");

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	GOTO 1 0
	INVALID_COMMAND
	INSERT "text"
""");

if (!result.Success)
{
	Console.WriteLine($"Parse error at line {result.ErrorLine}");
	Console.WriteLine($"Message: {result.ErrorMessage}");
}
```
