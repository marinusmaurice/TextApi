# Cursors

TextAPI provides two cursor types: `TextCursor` (single caret with optional selection) and `MultiCursor` (N synchronized cursors, all editing in one undo step). Both operate on character offsets and support grapheme-cluster-aware movement.

**Navigation:** [Overview](index.md) | [TextDocument](textdocument.md) | [Examples](examples-cursor.md) | [Undo/Redo](undo-redo.md)

## TextCursor


### Construction

```csharp
using TextAPI.Core.Cursor;

var cursor = new TextCursor(doc);           // starts at offset 0
var cursor = new TextCursor(doc, offset: 42); // starts at offset 42
```

### Position Properties

| Property | Type | Description |
|---|---|---|
| `Document` | `TextDocument` | The document this cursor operates on. |
| `ActiveOffset` | `int` | The moving end of the selection (where the caret is). |
| `CaretOffset` | `int` | Alias for `ActiveOffset`. |
| `AnchorOffset` | `int` | The fixed (anchor) end of the selection. |
| `CaretLine` | `int` | Zero-based line index of the caret. |
| `CaretColumn` | `int` | Zero-based column index of the caret. |
| `HasSelection` | `bool` | True when anchor and active offsets differ. |
| `SelectionStart` | `int` | Left edge of selection (≤ `SelectionEnd`). |
| `SelectionEnd` | `int` | Right edge of selection (≥ `SelectionStart`). |
| `SelectedText` | `string` | The selected text, or empty when no selection. |

### Direct Positioning

| Method | Description |
|---|---|
| `MoveTo(offset)` | Move caret, collapse any selection. |
| `SelectTo(offset)` | Extend selection by moving the active end. |
| `SetSelection(anchor, active)` | Set both anchor and active explicitly. |
| `CollapseToStart()` | Collapse selection to its left edge. |
| `CollapseToEnd()` | Collapse selection to its right edge. |

### Horizontal Movement

| Method | Description |
|---|---|
| `MoveLeft(count = 1)` | Move left by count grapheme clusters. |
| `MoveRight(count = 1)` | Move right by count grapheme clusters. |
| `MoveWordLeft()` | Move to the start of the previous (or current) word. |
| `MoveWordRight()` | Move past the current word and trailing non-word chars. |
| `MoveToLineStart()` | Move to column 0 of the current line. |
| `MoveToLineEnd()` | Move to just after the last character on the line. |
| `MoveToDocumentStart()` | Move to offset 0. |
| `MoveToDocumentEnd()` | Move to the end of the document. |

### Horizontal Selection

| Method | Description |
|---|---|
| `SelectLeft(count = 1)` | Extend selection left by count grapheme clusters. |
| `SelectRight(count = 1)` | Extend selection right by count grapheme clusters. |
| `SelectWordLeft()` | Extend selection left by one word. |
| `SelectWordRight()` | Extend selection right by one word. |
| `SelectToLineStart()` | Extend selection to column 0 of the current line. |
| `SelectToLineEnd()` | Extend selection to the end of the current line. |
| `SelectToDocumentStart()` | Extend selection to the start of the document. |
| `SelectToDocumentEnd()` | Extend selection to the end of the document. |

### Bulk Selection

| Method | Description |
|---|---|
| `SelectAll()` | Select the entire document. |
| `SelectLine(lineIndex?)` | Select the specified line (or the caret's current line). |
| `SelectWordAtCaret()` | Select the word under the caret. |

### Vertical Movement

| Method | Description |
|---|---|
| `MoveUp(count = 1)` | Move up by count lines, preserving preferred visual column. |
| `MoveDown(count = 1)` | Move down by count lines, preserving preferred visual column. |
| `SelectUp(count = 1)` | Extend selection up by count lines. |
| `SelectDown(count = 1)` | Extend selection down by count lines. |

### Editing

| Method | Description |
|---|---|
| `InsertText(text)` | Insert text at caret, replacing any selection first. |
| `DeleteSelection()` | Delete the current selection. |
| `DeleteLeft(count = 1)` | Delete left (Backspace). Deletes grapheme clusters. |
| `DeleteRight(count = 1)` | Delete right (Delete key). |
| `DeleteWordLeft()` | Delete the word immediately to the left (Ctrl+Backspace). |
| `DeleteWordRight()` | Delete the word immediately to the right (Ctrl+Delete). |

### Word Boundary Helpers

```csharp
// Static helpers for custom word navigation
bool isWord = TextCursor.IsWordChar('a');      // true
int  left   = cursor.WordLeft(cursor.CaretOffset);
int  right  = cursor.WordRight(cursor.CaretOffset);
```

### TextCursor Example

```csharp
var cursor = new TextCursor(doc);

// Simulate Ctrl+A → delete → type replacement
cursor.SelectAll();
cursor.DeleteSelection();
cursor.InsertText("Fresh content\n");

// Jump to line 3, select to end of line
int off = doc.PositionToOffset(2, 0);  // line 3 (0-based), col 0
cursor.MoveTo(off);
cursor.SelectToLineEnd();
Console.WriteLine(cursor.SelectedText);
```

---

## MultiCursor

Manages N independent `TextCursor` instances. All edit operations run as a single named undo step so the user can undo them all at once.

### Construction

```csharp
var mc = new MultiCursor(doc);  // starts with one cursor at offset 0
```

### Collection Properties

| Member | Type | Description |
|---|---|---|
| `Count` | `int` | Number of active cursors. |
| `Primary` | `TextCursor` | The primary cursor (last added, or index 0 after merge). |
| `All` | `IReadOnlyList<TextCursor>` | All cursors in ascending offset order. |

### Collection Management

| Method | Description |
|---|---|
| `AddCursor(offset)` | Add a collapsed cursor at offset (becomes Primary). |
| `AddCursor(anchor, active)` | Add a cursor with explicit selection (becomes Primary). |
| `RemoveCursor(index)` | Remove cursor at list index. No-op when only one cursor. |
| `Clear()` | Replace all cursors with a single cursor at offset 0. |
| `SetSingle(offset)` | Replace all cursors with a single collapsed cursor at offset. |

### Column Selection

```csharp
// Add one cursor per line from line 2 to line 8, all at column 4
mc.AddColumnSelection(startLine: 2, endLine: 8, column: 4);
mc.InsertText("    ");  // indent all 7 lines in one undo step
```

### Movement & Selection

All `TextCursor` movement and selection methods are mirrored on `MultiCursor` and apply to every cursor simultaneously:

```csharp
mc.MoveToLineEnd();        // every cursor jumps to its line's end
mc.SelectWordAtCaret();    // every cursor selects its word
mc.MoveWordRight();        // every cursor jumps one word right
```

### Editing (Single Undo Step)

```csharp
mc.InsertText("// ");    // insert at all cursors
mc.DeleteLeft(3);        // backspace 3 at all cursors
mc.DeleteSelection();   // delete all cursors' selections

doc.Undo();  // single undo — reverts ALL cursor edits together
```

### Paste (Distributed)

```csharp
// If clipboard has exactly N lines and there are N cursors, each cursor gets its line.
// Otherwise all cursors get the full clipboard text (broadcast).
mc.Paste(new[] { "line A", "line B", "line C" });  // 3 cursors → one line each
mc.Paste(new[] { "text" });                         // broadcast to all cursors
```

### MultiCursor Example: Comment Toggle

```csharp
// Comment out lines 4–8 using a column selection
var mc = new MultiCursor(doc);
mc.AddColumnSelection(4, 8, 0);  // one cursor per line at col 0
mc.InsertText("// ");             // prepend comment marker

// Undo removes all markers in one step
doc.Undo();
```

After every multi-cursor edit, overlapping or touching cursors are automatically merged to prevent duplicate edits.
