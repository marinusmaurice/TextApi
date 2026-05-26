using TextAPI.Core;
using TextAPI.Core.ReadOnly;

// ─────────────────────────────────────────────────────────────────────────────
// Read-only regions — demo
//
// Scenarios:
//  1. Protect a block comment; edits outside work, inside throw.
//  2. Silent mode (EnforceReadOnly = false) silently ignores blocked edits.
//  3. Region remapping — protect shifts right after an insert before it.
//  4. Unprotect re-enables editing.
// ─────────────────────────────────────────────────────────────────────────────

const string Source = """
    /* Copyright (c) 2024 Acme Corp. All rights reserved. */

    int Add(int a, int b) => a + b;
    int Sub(int a, int b) => a - b;
    """;

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Read-only regions demo");
Console.WriteLine("═══════════════════════════════════════════════════════\n");

// ── Scenario 1: Protect block comment ────────────────────────────────────────

Console.WriteLine("Scenario 1 — Protect block comment, enforce mode (throws)");
Console.WriteLine("──────────────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    doc.Load(Source);

    // Find the block comment span.
    string text   = doc.GetText(0, doc.Length);
    int commentStart = text.IndexOf("/*");
    int commentEnd   = text.IndexOf("*/") + 2;

    var model = doc.GetReadOnlyModel();
    var id    = model.Protect(commentStart, commentEnd);

    Console.WriteLine("Document content:");
    PrintDocument(doc, model);
    Console.WriteLine($"\nProtected region: [{commentStart}, {commentEnd}) — the block comment.");

    // Edit OUTSIDE the region — should succeed.
    int addLine = text.IndexOf("int Add");
    Console.WriteLine($"\nEditing outside: replace 'Add' with 'Sum'...");
    doc.Replace(addLine + 4, 3, "Sum");
    Console.WriteLine("  OK — document now contains 'Sum'.");

    // Edit INSIDE the region — should throw.
    Console.WriteLine("\nEditing inside: trying to change copyright year...");
    int yearOffset = doc.GetText(0, doc.Length).IndexOf("2024");
    try
    {
        doc.Replace(yearOffset, 4, "2025");
        Console.WriteLine("  ERROR: should have thrown!");
    }
    catch (ReadOnlyViolationException ex)
    {
        Console.WriteLine($"  Caught ReadOnlyViolationException: {ex.Message}");
    }

    Console.WriteLine($"\nUnprotecting the region...");
    model.Unprotect(id);
    doc.Replace(yearOffset, 4, "2025"); // now succeeds
    Console.WriteLine("  OK — year updated to 2025.");

    Console.WriteLine("\nFinal document:");
    PrintDocument(doc, model);
}

// ── Scenario 2: Silent mode ───────────────────────────────────────────────────

Console.WriteLine("\nScenario 2 — Silent mode (EnforceReadOnly = false)");
Console.WriteLine("───────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    doc.Load("protected content / editable");

    var model = doc.GetReadOnlyModel();
    model.Protect(0, 19); // "protected content"

    doc.EnforceReadOnly = false;

    Console.WriteLine("Attempting insert inside protected region (silent mode)...");
    doc.Insert(5, "BLOCKED");
    Console.WriteLine($"  Document unchanged: \"{doc.GetText(0, doc.Length)}\"");

    Console.WriteLine("Attempting insert outside protected region...");
    doc.Insert(19, " [appended]");
    Console.WriteLine($"  Document after allowed insert: \"{doc.GetText(0, doc.Length)}\"");
}

// ── Scenario 3: Region remapping ─────────────────────────────────────────────

Console.WriteLine("\nScenario 3 — Region remapping after insert before it");
Console.WriteLine("─────────────────────────────────────────────────────");
{
    var doc = new TextDocument();
    doc.Load("BEFORE  [PROTECTED]  AFTER");
    //        0123456789012345678901234
    //              8  9       19

    var model = doc.GetReadOnlyModel();
    // Protect "[PROTECTED]" at offsets 8..19.
    int protStart = doc.GetText(0, doc.Length).IndexOf("[PROTECTED]");
    int protEnd   = protStart + "[PROTECTED]".Length;
    model.Protect(protStart, protEnd);
    Console.WriteLine($"Protected: [{protStart}, {protEnd}) = \"{doc.GetText(protStart, protEnd - protStart)}\"");

    // Insert text before the region.
    string prefix = "<<INSERTED>> ";
    doc.Insert(0, prefix);
    Console.WriteLine($"Inserted \"{prefix}\" at offset 0.");

    // Region should have shifted right.
    var regions = model.GetRegions();
    var (_, newStart, newEnd) = regions[0];
    Console.WriteLine($"Region remapped to [{newStart}, {newEnd}) — shifted by {prefix.Length}.");

    // Verify it still blocks inside.
    try
    {
        doc.Insert(newStart + 2, "!!");
        Console.WriteLine("  ERROR: should have thrown!");
    }
    catch (ReadOnlyViolationException)
    {
        Console.WriteLine($"  Insert at {newStart + 2} (inside shifted region) correctly blocked.");
    }
}

// ── Scenario 4: Multiple protected regions ───────────────────────────────────

Console.WriteLine("\nScenario 4 — Multiple protected regions");
Console.WriteLine("────────────────────────────────────────");
{
    var doc = new TextDocument();
    doc.Load("AAA BBB CCC");
    var model = doc.GetReadOnlyModel();
    var idA = model.Protect(0, 3);
    var idB = model.Protect(4, 7);
    var idC = model.Protect(8, 11);
    Console.WriteLine("Protected: 'AAA' [0,3), 'BBB' [4,7), 'CCC' [8,11)");

    Console.WriteLine($"  IsReadOnly(0)={model.IsReadOnly(0)}, (5)={model.IsReadOnly(5)}, (10)={model.IsReadOnly(10)}");

    // Only 'BBB' can be unprotected.
    model.Unprotect(idB);
    Console.WriteLine("Unprotected 'BBB'.");
    Console.WriteLine($"  IsReadOnly(5)={model.IsReadOnly(5)} (now false)");

    // Editing the gap between AAA and CCC is fine.
    doc.Replace(4, 3, "NEW");
    Console.WriteLine($"  Replaced 'BBB' with 'NEW': \"{doc.GetText(0, doc.Length)}\"");
}

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Done.");

// ─────────────────────────────────────────────────────────────────────────────

static void PrintDocument(TextDocument doc, ReadOnlyRegionModel model)
{
    string full  = doc.GetText(0, doc.Length);
    string[] lines = full.Split('\n');
    int offset = 0;
    foreach (var line in lines)
    {
        // Mark each line with ★ if it contains any read-only character.
        bool hasProtected = false;
        for (int i = 0; i < line.Length; i++)
            if (model.IsReadOnly(offset + i)) { hasProtected = true; break; }
        char marker = hasProtected ? '★' : ' ';
        Console.WriteLine($"  {marker} {line}");
        offset += line.Length + 1; // +1 for '\n'
    }
}
