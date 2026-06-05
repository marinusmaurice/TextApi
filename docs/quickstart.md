# Quick Start

Get from zero to running in five minutes.

**Navigation:** [Overview](index.md) | [Scenarios](scenarios.md) | [Examples](examples.md)

## 1. Add References


Add project references to the assemblies you need:

```xml
<!-- Core document model -->
<ProjectReference Include="TextAPI.Core/TextAPI.Core.csproj" />

<!-- 27 operations + pipeline (requires Core) -->
<ProjectReference Include="TextAPI.Operations/TextAPI.Operations.csproj" />

<!-- C# REPL (requires Core + Operations) -->
<ProjectReference Include="TextAPI.Repl/TextAPI.Repl.csproj" />
```

## 2. Load a Document

```csharp
using TextAPI.Core;

var doc = new TextDocument();

// From a string
doc.Load("Hello, World!\nSecond line.");

// From a file (auto-detects encoding + EOL style)
await doc.LoadFileAsync("/path/to/file.txt");
```

## 3. Read Content

```csharp
Console.WriteLine(doc.GetText());          // full text
Console.WriteLine(doc.Length);            // char count
Console.WriteLine(doc.LineCount);         // line count
Console.WriteLine(doc.GetLine(0));         // first line (no newline)

var (line, col) = doc.OffsetToPosition(7); // offset → (line, column)
int offset = doc.PositionToOffset(0, 7);   // (line, col) → offset
```

## 4. Direct Mutations (Core)

Use `Insert`, `Delete`, `Replace` for precise, undo-tracked edits:

```csharp
doc.Insert(5, " Beautiful");         // insert at offset 5
doc.Delete(0, 5);                    // delete 5 chars at offset 0
doc.Replace(0, 5, "Hi");             // replace 5 chars at offset 0 with "Hi"

doc.Undo();                            // undo last edit
doc.Redo();                            // redo
```

## 5. Operations Pipeline

For structured, auditable, atomic mutations:

```csharp
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation  { Find = "TODO", Replace = "DONE" })
	.Add(new TrimTrailingWhitespaceOperation())
	.Add(new SortLinesOperation { Descending = true })
	.Execute();

if (result.Success)
	Console.WriteLine($"Done: {result.AuditLog.Summary}");
else
	Console.WriteLine($"Failed at op {result.FailedOperationIndex}: {result.ErrorMessage}");
```

## 6. JSON Pipeline (for AI / external systems)

```csharp
using TextAPI.Operations.Serialization;

string json = """
{
  "operations": [
	{ "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
	{ "type": "TRIM_TRAILING_WHITESPACE" },
	{ "type": "SORT_LINES" }
  ]
}
""";

var ops    = OperationMapper.FromPipelineJson(json);
var result = new DocumentPipeline(doc).Add(ops).Execute();
```

## 7. Script DSL

```csharp
using TextAPI.Core.Scripting;

var runner = new ScriptRunner(doc);
var result = runner.Run("""
	REPLACE_ALL "foo" "bar"
	INSERT_AT 0 "// Auto-generated\n"
	DELETE_LINE 5
""");

Console.WriteLine(result.Success);
```

## 8. C# REPL

```csharp
using TextAPI.Repl;
using TextAPI.Core.Cursor;

var mc   = new MultiCursor(doc);
var host = new CSharpScriptHost(doc, mc);

var r1 = await host.ExecuteAsync("var n = doc.LineCount;");
var r2 = await host.ExecuteAsync("Print(n);");  // n still in scope
Console.WriteLine(r2.Output);
```

## Next Steps

- [TextDocument API](textdocument.md) — full reference for the core document model
- [All 27 Operations](operations.md) — every operation with properties and examples
- [Pipeline & Rollback](pipeline.md) — atomic execution, cancellation, audit logs
- [Script DSL](scripting.md) — line-oriented command language
- [C# REPL](repl.md) — interactive scripting with Roslyn
- [Scenarios & Cookbook](scenarios.md) — real-world worked examples
