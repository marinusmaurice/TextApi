# TextAPI

A deterministic execution runtime for structured, auditable text mutations — built for AI systems, automation pipelines, and tooling that needs to manipulate documents with precision, not guesswork.

---

## Navigation

### Getting Started
- [Overview](index.md)
- [Quick Start](quickstart.md)
- [Scenarios & Cookbook](scenarios.md)

### Core API
- [TextDocument](textdocument.md)
- [Cursors](cursor.md)
- [Search](search.md)
- [Decorations](decorations.md)
- [Undo / Redo](undo-redo.md)
- [Encoding & EOL](encoding-eol.md)
- [Diff](diff.md)
- [Advanced Features](advanced.md)

### Operations Layer
- [All 27 Operations](operations.md)
- [Pipeline & Rollback](pipeline.md)
- [JSON Serialization](serialization.md)

### Scripting
- [Script DSL](scripting.md)
- [C# REPL](repl.md)

### Examples
- [Examples Hub](examples.md)
- [TextDocument](examples-textdocument.md)
- [All 27 Operations](examples-operations.md)
- [Pipeline & Serialization](examples-pipeline.md)
- [Cursors](examples-cursor.md)
- [Search, Decorations & Diff](examples-search.md)
- [Script DSL & REPL](examples-scripting.md)
- [Advanced Features](examples-advanced.md)

---

## What is TextAPI?

TextAPI is not a text editor library. It is a **document mutation engine**. The core idea: instead of letting code (or an LLM) rewrite a string freely, you express every change as a named, serializable operation. Operations are applied atomically, logged to an audit trail, and are fully rollback-safe.

This makes TextAPI ideal for:

- **AI-driven document editing** — an LLM emits JSON; TextAPI executes it deterministically
- **Batch text processing pipelines** — transform thousands of files with the same operation set
- **Auditable document workflows** — every change is recorded with what, where, and how many characters changed
- **Scripted text automation** — run named operations from a script file or the C# REPL

## Architecture

| Layer | Assembly | Purpose |
|---|---|---|
| **Core** | `TextAPI.Core` | Piece-table document model, cursor, search, decorations, undo/redo, diff, encoding |
| **Operations** | `TextAPI.Operations` | 27 named operations, DocumentPipeline with atomic rollback, JSON serialization |
| **REPL** | `TextAPI.Repl` | Stateful C# scripting host (Roslyn) with full access to doc + operations |

## Key Concepts

### TextDocument

The central object. Backed by a **piece table** (O(log n) insert/delete, minimal allocation). Supports load, save, full-text search, syntax tokenization, decorations, undo/redo, encoding detection, and EOL normalization. See [TextDocument reference](textdocument.md).

### Operations

27 typed operations organized into six categories: offset-based, anchor-based, pattern-based, line-based, transform, and query. Each operation implements `IDocumentOperation` and returns an `OperationResult` that reports success, chars inserted/deleted, and match counts. See [Operations reference](operations.md).

### Pipeline

A `DocumentPipeline` runs a sequence of operations atomically. If any operation fails, the document is restored to a pre-pipeline snapshot. No partial mutations are left behind. See [Pipeline & Rollback](pipeline.md).

### JSON Serialization

Every operation serializes to/from camelCase JSON. An LLM can emit `{"type":"REPLACE_ALL","find":"TODO","replace":"DONE"}` and TextAPI will execute it exactly. See [JSON Serialization](serialization.md).

### Script DSL

A lightweight line-oriented command language (`INSERT_AT 5 "hello"`, `REPLACE_ALL "foo" "bar"`) for simple automation without writing C#. See [Script DSL](scripting.md).

### C# REPL

A stateful Roslyn-backed scripting host. Variables persist across submissions. Full access to `doc`, `mc`, all 27 operation types, `NewPipeline()`, `RunJson()`, and `ParseOps()`. See [C# REPL](repl.md).

## Five-Minute Example

```csharp
// 1. Create and load a document
var doc = new TextDocument();
doc.Load("TODO: fix the bug\nTODO: write tests\nDone: deploy");

// 2. Build and execute a pipeline
var result = new DocumentPipeline(doc)
	.Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
	.Add(new TrimTrailingWhitespaceOperation())
	.Add(new SortLinesOperation())
	.Execute();

Console.WriteLine(result.Success);              // True
Console.WriteLine(result.AuditLog.Summary);    // "3 op(s) succeeded; ..."
Console.WriteLine(doc.GetText());

// 3. Same thing via JSON (e.g. from an LLM)
var result2 = new DocumentPipeline(doc)
	.Add(OperationMapper.FromPipelineJson("""
	{
	  "operations": [
		{ "type": "REPLACE_ALL", "find": "DONE", "replace": "CHECKED" },
		{ "type": "SORT_LINES", "descending": true }
	  ]
	}
	"""))
	.Execute();
```

## Test Coverage

| Project | Tests |
|---|---|
| TextAPI.Tests (Core) | 1 955 |
| TextAPI.Operations.Tests | 182 |
| **Total** | **2 137** |

## Navigation

Use the sidebar to jump to any topic. If you're new, start with the [Quick Start](quickstart.md), then browse the [Scenarios & Cookbook](scenarios.md) for real-world examples.
