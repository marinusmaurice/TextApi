using TextEditor.Core;
using TextEditor.Core.InlayHints;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Source = """
    var result = Add(10, 20);
    var name = GetName(42);
    var sum = result + 5;
    """;

var doc   = new TextDocument();
doc.Load(Source);
var model = doc.GetInlayHintModel();

// ── Scenario 1: Add synthetic parameter-name hints ────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Synthetic parameter-name hints");
Console.WriteLine("══════════════════════════════════════════════\n");

// Simulate LSP adding hints: "x:" before 10, "y:" before 20, "id:" before 42
var hints = new[]
{
    new InlayHint(doc.GetText().IndexOf("10"), "x:", InlayHintKind.Parameter, "Parameter 'x' of Add"),
    new InlayHint(doc.GetText().IndexOf("20"), "y:", InlayHintKind.Parameter, "Parameter 'y' of Add"),
    new InlayHint(doc.GetText().IndexOf("42"), "id:", InlayHintKind.Parameter, "Parameter 'id' of GetName"),
};
model.SetHints(hints);

PrintDocument(doc, model);

// ── Scenario 2: Add type hints ────────────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Add inferred-type hints");
Console.WriteLine("══════════════════════════════════════════════\n");

model.AddHint(new InlayHint(doc.GetText().IndexOf("result") + "result".Length, ": int", InlayHintKind.Type));
model.AddHint(new InlayHint(doc.GetText().IndexOf("name")   + "name".Length,   ": string", InlayHintKind.Type));
model.AddHint(new InlayHint(doc.GetText().IndexOf("sum")    + "sum".Length,    ": int", InlayHintKind.Type));

PrintDocument(doc, model);

// ── Scenario 3: Insert text — hints shift ────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Insert text before hints — offsets shift");
Console.WriteLine("══════════════════════════════════════════════\n");

int before = model.AllHints[0].Offset;
doc.Insert(0, "// Generated code\n");
int after  = model.AllHints[0].Offset;
Console.WriteLine($"  First hint offset before insert: {before}");
Console.WriteLine($"  First hint offset after  insert: {after}  (shifted by {"// Generated code\n".Length})");
Console.WriteLine();
PrintDocument(doc, model);

// ── Scenario 4: Delete text containing a hint ─────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 4: Delete text — covered hints removed");
Console.WriteLine("══════════════════════════════════════════════\n");

int countBefore = model.AllHints.Count;
// Delete the "Add(10, 20)" call — removes the x: and y: parameter hints
int addStart = doc.GetText().IndexOf("Add(");
int addEnd   = doc.GetText().IndexOf(";", addStart) + 1;
doc.Delete(addStart, addEnd - addStart);
int countAfter  = model.AllHints.Count;
Console.WriteLine($"  Hints before delete: {countBefore}");
Console.WriteLine($"  Hints after  delete: {countAfter}  ({countBefore - countAfter} removed)");
Console.WriteLine();
PrintDocument(doc, model);

// ── Scenario 5: GetHintsInRange ───────────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 5: GetHintsInRange for first line");
Console.WriteLine("══════════════════════════════════════════════\n");

int lineEnd = doc.PositionToOffset(1, 0); // start of second line
var firstLineHints = model.GetHintsInRange(0, lineEnd);
Console.WriteLine($"  First line hints ({firstLineHints.Count}):");
foreach (var h in firstLineHints)
    Console.WriteLine($"    offset={h.Offset} kind={h.Kind} text=\"{h.Text}\"");

static void PrintDocument(TextDocument doc, InlayHintModel model)
{
    Console.WriteLine("  Document with hints:");
    for (int line = 0; line < doc.LineCount; line++)
    {
        string text       = doc.GetLine(line);
        int    lineStart  = doc.PositionToOffset(line, 0);
        int    lineEnd    = lineStart + text.Length;
        var    lineHints  = model.GetHintsInRange(lineStart, lineEnd + 1);

        // Render: interleave hints into text
        var sb     = new System.Text.StringBuilder("  ");
        int offset = lineStart;
        foreach (var hint in lineHints.OrderBy(h => h.Offset))
        {
            int localOffset = hint.Offset - lineStart;
            if (localOffset > sb.Length - 2) // not past end
            {
                string before = text[Math.Max(0, offset - lineStart)..Math.Min(localOffset, text.Length)];
                sb.Append(before);
                sb.Append($"\x1b[90m{hint.Text}\x1b[0m"); // dim grey
                offset = hint.Offset;
            }
        }
        // Append remainder of line
        int remaining = offset - lineStart;
        if (remaining < text.Length) sb.Append(text[remaining..]);
        Console.WriteLine(sb.ToString());
    }
    Console.WriteLine($"\n  Total hints: {model.AllHints.Count}\n");
}
