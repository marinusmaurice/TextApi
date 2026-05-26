using TextAPI.Core;
using TextAPI.Core.Cursor;

// ─────────────────────────────────────────────────────────────────────────────
// Multi-line paste across multi-cursors — demo
//
// Shows both paste modes:
//   • Distributed: N-line clipboard + N cursors → each cursor gets one line
//   • Broadcast:   any other count → every cursor gets the full joined text
// ─────────────────────────────────────────────────────────────────────────────

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Multi-line paste across multi-cursors");
Console.WriteLine("═══════════════════════════════════════════════════════\n");

// ── Scenario 1: Distributed paste ────────────────────────────────────────────

Console.WriteLine("Scenario 1 — Distributed paste (3 lines, 3 cursors)");
Console.WriteLine("────────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    // Three blank lines — each gets a different value from the clipboard.
    doc.Load("\n\n");

    var mc = new MultiCursor(doc);
    // Place one collapsed cursor at the start of each line.
    mc.SetSingle(0);   // offset 0 → line 0
    mc.AddCursor(1);   // offset 1 → line 1
    mc.AddCursor(2);   // offset 2 → line 2

    Console.WriteLine("Before paste:");
    PrintDocument(doc);

    string[] clipboard = ["firstName = \"Alice\"", "firstName = \"Bob\"", "firstName = \"Carol\""];
    Console.WriteLine($"\nClipboard ({clipboard.Length} lines):");
    for (int i = 0; i < clipboard.Length; i++)
        Console.WriteLine($"  [{i}] {clipboard[i]}");

    mc.Paste(clipboard);

    Console.WriteLine("\nAfter distributed paste:");
    PrintDocument(doc);
    Console.WriteLine($"\nCursor positions: {string.Join(", ", mc.All.Select(c => c.CaretOffset))}");

    Console.WriteLine("\nUndo — restores original:");
    doc.Undo();
    PrintDocument(doc);
}

// ── Scenario 2: Broadcast paste ──────────────────────────────────────────────

Console.WriteLine("\nScenario 2 — Broadcast paste (2 lines, 3 cursors)");
Console.WriteLine("─────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    // Three placeholder variables that all get replaced by the same snippet.
    doc.Load("let x = TODO;\nlet y = TODO;\nlet z = TODO;");

    var mc = new MultiCursor(doc);

    // Select each "TODO" word: find and select in each line.
    int line0Todo = doc.GetText(0, doc.Length).IndexOf("TODO");
    int line1Todo = doc.GetText(0, doc.Length).IndexOf("TODO", line0Todo + 4);
    int line2Todo = doc.GetText(0, doc.Length).IndexOf("TODO", line1Todo + 4);

    mc.SetSingle(line0Todo);
    mc.Primary.SelectTo(line0Todo + 4);
    mc.AddCursor(line1Todo, line1Todo + 4);
    mc.AddCursor(line2Todo, line2Todo + 4);

    Console.WriteLine("Before paste:");
    PrintDocument(doc);

    // Two-line snippet — doesn't match 3 cursors → broadcast.
    string[] clipboard = ["compute(", "  input)"];
    Console.WriteLine($"\nClipboard ({clipboard.Length} lines):");
    foreach (var l in clipboard)
        Console.WriteLine($"  {l}");
    Console.WriteLine("  (count doesn't match cursor count → broadcast)");

    mc.Paste(clipboard);

    Console.WriteLine("\nAfter broadcast paste:");
    PrintDocument(doc);
}

// ── Scenario 3: Undo is one step ─────────────────────────────────────────────

Console.WriteLine("\nScenario 3 — Distributed paste undoes in one step");
Console.WriteLine("──────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    doc.Load("_\n_\n_");

    var mc = new MultiCursor(doc);
    mc.SetSingle(0);
    mc.AddCursor(2);
    mc.AddCursor(4);

    mc.Paste(["red", "green", "blue"]);
    Console.WriteLine($"After paste:  \"{doc.GetText(0, doc.Length)}\"");

    doc.Undo();
    Console.WriteLine($"After 1 undo: \"{doc.GetText(0, doc.Length)}\"");
    Console.WriteLine("  (single Undo() reverted all three insertions)");
}

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Done.");

static void PrintDocument(TextDocument doc)
{
    string full = doc.GetText(0, doc.Length);
    string[] lines = full.Split('\n');
    for (int i = 0; i < lines.Length; i++)
        Console.WriteLine($"  {i,2}: {(lines[i].Length == 0 ? "(empty)" : lines[i])}");
}
