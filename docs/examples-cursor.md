# Examples: Cursor

Cursor and multi-cursor examples.

**Navigation:** [Examples Hub](examples.md) | [Cursors Reference](cursor.md) | [TextDocument Examples](examples-textdocument.md)

## TextCursor Navigation

```csharp
var doc = new TextDocument();
doc.Load("Hello\nWorld\nTest");

var cursor = new TextCursor(doc);

// Position info
Console.WriteLine($"Offset: {cursor.CaretOffset}");
Console.WriteLine($"Line: {cursor.CaretLine}, Col: {cursor.CaretColumn}");

// Move
cursor.MoveRight(5);
cursor.MoveDown(1);
cursor.MoveToLineStart();

// Selection
cursor.SelectToLineEnd();
Console.WriteLine($"Selected: {cursor.SelectedText}");

cursor.CollapseToEnd();
Console.WriteLine($"Has selection: {cursor.HasSelection}");  // false
```

## TextCursor Editing

```csharp
var doc = new TextDocument();
doc.Load("Hello World");

var cursor = new TextCursor(doc);

// Insert at cursor
cursor.InsertText(" TextAPI");
Console.WriteLine(doc.GetText());  // "Hello TextAPI World"

// Delete left/right
cursor.DeleteLeft(4);  // delete " API"
Console.WriteLine(doc.GetText());  // "Hello Text World"

// Select and delete
cursor.SelectWordAtCaret();
cursor.DeleteSelection();
```

## MultiCursor Column Selection

```csharp
var doc = new TextDocument();
doc.Load("line1\nline2\nline3\nline4");

var mc = new MultiCursor(doc);

// Add cursors at column 0 for lines 1-3
mc.AddColumnSelection(1, 3, 0);

// Insert at all cursors
mc.InsertText(">> ");

Console.WriteLine(doc.GetText());
// line1
// >> line2
// >> line3
// >> line4

// Single undo removes all inserts
doc.Undo();
```

## MultiCursor Movement

```csharp
var doc = new TextDocument();
doc.Load("word1 word2\nword3 word4");

var mc = new MultiCursor(doc);
mc.AddCursor(0);
mc.AddCursor(6);

// All cursors move simultaneously
mc.MoveWordRight();
mc.SelectRight(2);

foreach (var c in mc.All)
	Console.WriteLine($"Cursor at {c.CaretOffset}: '{c.SelectedText}'");
```
