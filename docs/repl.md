# C# REPL

`CSharpScriptHost` is a Roslyn-backed scripting engine that lets you run arbitrary C# code with full access to the document and cursor objects. Variables persist across submissions, so you can build up state interactively.

**Navigation:** [Overview](index.md) | [Script DSL](scripting.md) | [Examples](examples-scripting.md) | [Quick Start](quickstart.md)

## Basic Usage

```csharp
using TextAPI.Repl;
using TextAPI.Core.Cursor;

var doc = new TextDocument();
doc.Load("Hello\nWorld");

var mc = new MultiCursor(doc);
var host = new CSharpScriptHost(doc, mc);

// First submission: define a variable
var r1 = await host.ExecuteAsync("var n = doc.LineCount;");
Console.WriteLine($"Executed: {r1.Success}");

// Second submission: use the variable (n is still in scope)
var r2 = await host.ExecuteAsync("Print(n + 1);");
Console.WriteLine(r2.Output);  // "3" (assuming 2 lines)
```

## ExecutionResult

| Property | Type | Description |
|---|---|---|
| `Success` | `bool` | Whether the code compiled and executed. |
| `Output` | `string` | Captured console output. |
| `Error` | `string?` | Compilation or runtime error message. |
| `ReturnValue` | `object?` | Value returned by the expression (if any). |

## Available Objects in Scope

- **`doc`**: The `TextDocument` instance
- **`mc`**: The `MultiCursor` instance
- **`Print(object value)`**: Equivalent to `Console.WriteLine()`
- All imported namespaces (TextAPI, System, etc.)

## Persistence

Variables and functions defined in one submission remain in scope for subsequent submissions:

```csharp
await host.ExecuteAsync("var x = 42;");
await host.ExecuteAsync("var y = x + 8;");
await host.ExecuteAsync("Print(y);");  // 50
```

## Common Workflows

### Query the Document

```csharp
await host.ExecuteAsync(@"
	var lines = doc.LineCount;
	var len = doc.Length;
	Print($""{lines} lines, {len} chars"");
");
```

### Modify with Cursors

```csharp
await host.ExecuteAsync(@"
	mc.AddColumnSelection(0, 5, 0);
	mc.InsertText(""// "");
");
```

### Use Operations

```csharp
await host.ExecuteAsync(@"
	using TextAPI.Operations;
	var result = new DocumentPipeline(doc)
		.Add(new ReplaceAllOperation { Find = ""TODO"", Replace = ""DONE"" })
		.Execute();
	Print(result.Success ? ""Done"" : ""Failed"");
");
```

## Error Handling

```csharp
var result = await host.ExecuteAsync("invalid c# code");
if (!result.Success)
	Console.WriteLine($"Error: {result.Error}");
```
