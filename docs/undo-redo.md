# Undo / Redo

Every mutation to `TextDocument` is automatically tracked. Single-grapheme edits are coalesced into groups (like a real editor), while multi-character programmatic edits each form their own undo step. Named composite commands group multiple edits under a single description.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Cursors](cursor.md) | [Pipeline](pipeline.md)

## Basic Undo/Redo

```csharp
// Check availability
if (doc.CanUndo) doc.Undo();
if (doc.CanRedo) doc.Redo();
```

## Undo/Redo API

| Member | Type | Description |
|---|---|---|
| `CanUndo` | `bool` | True when there is at least one step to undo. |
| `CanRedo` | `bool` | True when there are redo steps available. |
| `Undo()` | `void` | Undo one step. No-op if `CanUndo` is false. |
| `Redo()` | `void` | Redo one step. No-op if `CanRedo` is false. |
| `UndoDescriptions` | `IReadOnlyList<string>` | Human-readable description for each undo step, most recent first. |
| `FlushUndoGroup()` | `void` | Close the current coalescing group and start a new one. |

## Coalescing Groups

Single-character edits at adjacent positions are automatically merged into one undo step, the same way a keyboard-driven editor works. Call `FlushUndoGroup()` to seal the current group:

```csharp
// Three single-char inserts → coalesced into one undo step
doc.Insert(0, "a");
doc.Insert(1, "b");
doc.Insert(2, "c");
// One Undo() removes all three

// Seal the group before doing something different
doc.FlushUndoGroup();
doc.Insert(0, "X");  // new separate undo step

doc.Undo();  // removes "X"
doc.Undo();  // removes "abc"
```

Multi-character programmatic edits (e.g. `doc.Insert(0, "Hello World")`) always create their own undo step and are never coalesced.

## Composite Commands

Bundle multiple edits into a single named undo step with `ExecuteComposite`:

```csharp
using TextAPI.Core.Commands;

doc.ExecuteComposite("Rename symbol", new IEditorCommand[]
{
	new InsertCommand(offset: 0,  "newName"),
	new DeleteCommand(offset: 50, length: 7),
	new InsertCommand(offset: 50, "newName"),
});

// One undo step labelled "Rename symbol"
doc.Undo();
```

## Inspecting the Undo Stack

```csharp
Console.WriteLine($"Undo steps: {doc.UndoDescriptions.Count}");

foreach (var desc in doc.UndoDescriptions)
	Console.WriteLine($"  • {desc}");

// Example output:
//   • Insert
//   • ReplaceAll ("foo" → "bar", 3 replacements)
//   • Rename symbol
```

## Undo Stack Reset

Certain operations reset the undo stack entirely:

- `doc.Load(content)` — loads new content and clears history.
- `await doc.LoadFileAsync(path)` — same.
- `DocumentPipeline.Execute()` rollback — calls `doc.Load(snapshot)` internally, resetting the undo stack.

If you need the pre-pipeline state in undo history, snapshot the text yourself (`string snap = doc.GetText()`) before calling `Execute()`. The pipeline rollback resets the undo stack as a side effect of calling `doc.Load()`.

## IsModified Flag

`doc.IsModified` is set to `true` after any mutation and cleared when `SaveAsync` / `SaveFileAsync` succeeds:

```csharp
doc.Load("Hello");
Console.WriteLine(doc.IsModified); // false

doc.Insert(0, "X");
Console.WriteLine(doc.IsModified); // true

doc.Undo();
Console.WriteLine(doc.IsModified); // false (back to saved state)
```

## Undo with MultiCursor

Multi-cursor edits are committed as a single undo step automatically. One `Undo()` reverts all cursors' changes:

```csharp
var mc = new MultiCursor(doc);
mc.AddColumnSelection(0, 5, 0);  // 6 cursors
mc.InsertText("// ");            // one undo step for all 6 insertions

doc.Undo();  // removes all 6 "// " prefixes in one shot
```
