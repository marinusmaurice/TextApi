using TextEditor.Core;
using TextEditor.Core.Cursor;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// ── Helper ────────────────────────────────────────────────────────────────

static void TypeString(TextDocument doc, TextCursor cursor, string text)
{
    foreach (char c in text)
        cursor.InsertText(c.ToString());
}

static int CountUndoSteps(TextDocument doc)
{
    doc.FlushUndoGroup();
    int steps = 0;
    while (doc.CanUndo) { doc.Undo(); steps++; }
    while (doc.CanRedo) doc.Redo();
    return steps;
}

// ── Demo 1: Word-by-word undo via grouping ────────────────────────────────

Console.WriteLine("=== Demo 1: Smart Undo Grouping (word-level undo) ===");
Console.WriteLine();

{
    var doc = new TextDocument();
    var cur = new TextCursor(doc);

    TypeString(doc, cur, "hello world");
    doc.FlushUndoGroup();

    int steps = CountUndoSteps(doc);
    Console.WriteLine($"Typed \"hello world\" character by character → {steps} undo step(s) (expected: 1)");
    Console.WriteLine();

    // Show undo stepping
    TypeString(doc, cur, "hello world");
    doc.FlushUndoGroup();
    Console.WriteLine($"After typing: \"{doc.GetText()}\"");
    doc.Undo();
    Console.WriteLine($"After 1x Undo: \"{doc.GetText()}\" (entire word erased in one step)");
    doc.Redo();
    Console.WriteLine($"After Redo:    \"{doc.GetText()}\"");
}

Console.WriteLine();

// ── Demo 2: Multiple groups with cursor navigation ────────────────────────

Console.WriteLine("=== Demo 2: Two Groups (type, move, type) ===");
Console.WriteLine();

{
    var doc = new TextDocument();
    var cur = new TextCursor(doc);

    TypeString(doc, cur, "hello");
    cur.MoveTo(5); // flush "hello" group
    TypeString(doc, cur, " world");
    doc.FlushUndoGroup();

    Console.WriteLine($"Document: \"{doc.GetText()}\"");
    doc.Undo();
    Console.WriteLine($"After 1st Undo: \"{doc.GetText()}\" (' world' undone)");
    doc.Undo();
    Console.WriteLine($"After 2nd Undo: \"{doc.GetText()}\" ('hello' undone)");
    doc.Redo();
    doc.Redo();
    Console.WriteLine($"After 2x Redo:  \"{doc.GetText()}\"");
}

Console.WriteLine();

// ── Demo 3: Unicode emoji grouping ───────────────────────────────────────

Console.WriteLine("=== Demo 3: Unicode Emoji Grouping ===");
Console.WriteLine();

{
    var doc = new TextDocument();
    var cur = new TextCursor(doc);

    // Waving hand emoji: U+1F44B (2 code units, 1 grapheme cluster)
    string wave = "\U0001F44B";
    for (int i = 0; i < 5; i++) cur.InsertText(wave);
    doc.FlushUndoGroup();

    int steps = CountUndoSteps(doc);
    Console.WriteLine($"Typed 5 wave emojis (👋👋👋👋👋) one by one → {steps} undo step(s) (expected: 1)");

    TypeString(doc, cur, " "); // spacer
    for (int i = 0; i < 5; i++) cur.InsertText(wave);
    doc.FlushUndoGroup();

    Console.WriteLine($"Document: \"{doc.GetText()}\"");
    doc.Undo(); // undo 5 emojis
    Console.WriteLine($"After Undo 5 emojis: \"{doc.GetText()}\"");
    doc.Undo(); // undo space
    Console.WriteLine($"After Undo space:    \"{doc.GetText()}\"");
    doc.Undo(); // undo first 5 emojis
    Console.WriteLine($"After Undo first 5:  \"{doc.GetText()}\"");
}

Console.WriteLine();

// ── Demo 4: Family emoji (11 code units, 1 cluster) ──────────────────────

Console.WriteLine("=== Demo 4: Family Emoji (11 code units, 1 grapheme cluster) ===");
Console.WriteLine();

{
    string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
    Console.WriteLine($"Family emoji code units: {family.Length} (should be 11)");

    var doc = new TextDocument();
    var cur = new TextCursor(doc);
    cur.InsertText(family);
    cur.InsertText(family);
    doc.FlushUndoGroup();

    int steps = CountUndoSteps(doc);
    Console.WriteLine($"Typed 2 family emojis → {steps} undo step(s) (expected: 1)");
}

Console.WriteLine();

// ── Demo 5: Backspace grouping ────────────────────────────────────────────

Console.WriteLine("=== Demo 5: Backspace Grouping ===");
Console.WriteLine();

{
    var doc = new TextDocument();
    doc.Load("hello world");
    var cur = new TextCursor(doc, 11); // end of doc

    // Backspace all 11 chars
    for (int i = 0; i < 11; i++) cur.DeleteLeft();
    doc.FlushUndoGroup();

    int steps = CountUndoSteps(doc);
    Console.WriteLine($"Backspaced 11 chars from 'hello world' → {steps} undo step(s) (expected: 1)");

    // Restore and demonstrate
    doc.Load("hello world");
    cur = new TextCursor(doc, 11);
    for (int i = 0; i < 11; i++) cur.DeleteLeft();
    doc.FlushUndoGroup();
    Console.WriteLine($"After backspace: \"{doc.GetText()}\"");
    doc.Undo();
    Console.WriteLine($"After Undo:      \"{doc.GetText()}\"");
}

Console.WriteLine();

// ── Demo 6: Comparison summary ────────────────────────────────────────────

Console.WriteLine("=== Demo 6: Comparison — With vs Without Grouping ===");
Console.WriteLine();
Console.WriteLine("Without smart grouping: typing 'hello' = 5 Ctrl+Z steps (one per letter).");
Console.WriteLine("With smart grouping:    typing 'hello' = 1 Ctrl+Z step  (whole word).");
Console.WriteLine();
Console.WriteLine("This matches VS Code behaviour: arrow keys / mouse clicks end the current");
Console.WriteLine("undo group, so Ctrl+Z undoes the last contiguous typing run.");
