using TextEditor.Core;
using TextEditor.Core.Folding;
using TextEditor.Core.Language;

bool colour = !Console.IsOutputRedirected;

// ── Embedded sample ────────────────────────────────────────────────────────

string sample = """
using System;
using System.Collections.Generic;

namespace Demo
{
    /// <summary>A minimal event bus.</summary>
    public class EventBus
    {
        private readonly Dictionary<string, List<Action<object>>> _handlers
            = new Dictionary<string, List<Action<object>>>();

        public void Subscribe(string topic, Action<object> handler)
        {
            if (!_handlers.TryGetValue(topic, out var list))
            {
                list = new List<Action<object>>();
                _handlers[topic] = list;
            }
            list.Add(handler);
        }

        public void Publish(string topic, object payload)
        {
            if (!_handlers.TryGetValue(topic, out var list)) return;
            foreach (var h in list)
            {
                try   { h(payload); }
                catch { /* swallow */ }
            }
        }

        public void Unsubscribe(string topic, Action<object> handler)
        {
            if (_handlers.TryGetValue(topic, out var list))
                list.Remove(handler);
        }
    }
}
""";

var doc = new TextDocument(new CSharpTokeniser());
doc.Load(sample);

var model    = doc.GetFoldingModel();
var strategy = new BraceFoldingStrategy();
model.UpdateRegions(strategy);

// ── Helpers ────────────────────────────────────────────────────────────────

void Esc(string code) { if (colour) Console.Write($"\x1b[{code}m"); }
void Reset()          { if (colour) Console.Write("\x1b[0m"); }

void PrintHeader(string title)
{
    int width = colour ? Math.Max(40, Math.Min(Console.WindowWidth - 1, 72)) : 72;
    Esc("1;97");
    Console.WriteLine($"\n{new string('━', width)}");
    Console.WriteLine($"  {title}");
    Console.WriteLine(new string('━', width));
    Reset();
}

// Render the document with fold-state decorations in the gutter.
// Hidden lines are collapsed: only the first hidden line of each fold is
// shown as a placeholder; the rest are skipped entirely.
void PrintWithFolds(string annotation = "")
{
    if (annotation.Length > 0)
    {
        Esc("90"); Console.WriteLine($"  [{annotation}]"); Reset();
    }

    var regionByStart = model.Regions.ToDictionary(r => r.StartLine);
    // Build a lookup: for each hidden line, which outermost fold hides it?
    // (We only want to show the placeholder once per fold, on StartLine+1.)
    var skipUntil = -1;   // skip doc lines up to (and including) this index

    for (int line = 0; line < doc.LineCount; line++)
    {
        // If this line is inside a block we already rendered a placeholder for, skip.
        if (line <= skipUntil) continue;

        bool visible = model.IsLineVisible(line);

        // Gutter decoration
        Esc("90");
        Console.Write($"  {line + 1,3} ");
        if (regionByStart.TryGetValue(line, out var startRegion))
        {
            Esc(startRegion.IsFolded ? "1;33" : "36");
            Console.Write(startRegion.IsFolded ? "▶" : "▼");
            Reset();
        }
        else
        {
            // Check if this line is the EndLine of any region (for "└" marker).
            bool isEnd = model.Regions.Any(r => r.EndLine == line);
            Esc("90");
            Console.Write(isEnd ? "└" : "│");
            Reset();
        }

        if (!visible)
        {
            // First hidden line of a fold: find the outermost fold covering it.
            var fold = model.Regions
                .Where(r => r.IsFolded && r.StartLine < line && r.EndLine >= line)
                .OrderBy(r => r.StartLine)
                .FirstOrDefault();
            Esc("33");
            string lbl = fold?.Label ?? "…";
            if (lbl.Length > 45) lbl = lbl[..45] + " …";
            Console.Write($" ··· {lbl}");
            Reset();
            Console.WriteLine();
            if (fold != null) skipUntil = fold.EndLine;  // EndLine is also hidden inside the fold
            continue;
        }

        Console.Write(" ");
        Console.WriteLine(doc.GetLine(line));
    }
}

// ── Scenario 1: Detected fold regions ────────────────────────────────────

PrintHeader("Scenario 1 — Detected fold regions  (BraceFoldingStrategy)");
Console.WriteLine($"\n  {model.Regions.Count} foldable region(s) detected:\n");

foreach (var r in model.Regions)
{
    Esc("36");
    Console.Write($"  [{r.StartLine + 1,2} – {r.EndLine + 1,2}]");
    Reset();
    Console.Write($"  {r.LineCount,2} lines  label: ");
    Esc("33");
    string label = r.Label.Length > 50 ? r.Label[..50] + " …" : r.Label;
    Console.Write($"\"{label}\"");
    Reset();
    Console.WriteLine();
}

// ── Scenario 2: Source listing with fold gutter ───────────────────────────

PrintHeader("Scenario 2 — Source listing with fold gutter  (all regions open)");
Console.WriteLine();
PrintWithFolds("all open — ▼ = foldable start  └ = foldable end  │ = body");

// ── Scenario 3: Fold inner methods ────────────────────────────────────────

PrintHeader("Scenario 3 — Fold all inner method bodies");

model.FoldAll();
// Keep the outermost (class body) unfolded so nested content is visible.
var outermost = model.Regions.OrderByDescending(r => r.HiddenLineCount).First();
model.Unfold(outermost.StartLine);

Console.WriteLine($"\n  Visible lines: {model.VisibleLineCount} / {doc.LineCount}\n");
PrintWithFolds("inner methods folded — ▶ = folded");

// ── Scenario 4: Display-line mapping ─────────────────────────────────────

PrintHeader("Scenario 4 — Display-line ↔ document-line mapping");

Console.WriteLine("\n  doc line → display line  (hidden lines show −1)\n");
for (int i = 0; i < doc.LineCount; i++)
{
    int disp = model.ToDisplayLine(i);
    bool vis = disp >= 0;

    Esc(vis ? "32" : "31");
    Console.Write($"  doc {i + 1,3}  →  ");
    if (vis)
        Console.Write($"display {disp + 1,3}");
    else
        Console.Write("  hidden    ");
    Reset();

    Esc("90");
    string lineText = doc.GetLine(i);
    string preview  = lineText.TrimStart();
    if (preview.Length > 40) preview = preview[..40] + "…";
    Console.Write($"   {preview}");
    Reset();
    Console.WriteLine();
}

Console.WriteLine("\n  display line → document line  (round-trip check)\n");
int visCount = model.VisibleLineCount;
bool allOk   = true;
for (int d = 0; d < visCount; d++)
{
    int docLine  = model.ToDocumentLine(d);
    int backDisp = docLine >= 0 ? model.ToDisplayLine(docLine) : -1;
    bool ok      = backDisp == d;
    if (!ok) allOk = false;

    Esc(ok ? "32" : "31");
    Console.Write($"  display {d + 1,3}  →  doc {(docLine >= 0 ? docLine + 1 : -1),3}  →  display {(backDisp >= 0 ? backDisp + 1 : -1),3}  {(ok ? "✓" : "✗")}");
    Reset();
    Console.WriteLine();
}

Console.WriteLine();
Esc(allOk ? "1;32" : "1;31");
Console.WriteLine($"  Round-trip: {(allOk ? "all correct" : "MISMATCH")}");
Reset();
Console.WriteLine();

// ── Scenario 5: Unfold all + re-detect after edit ─────────────────────────

PrintHeader("Scenario 5 — Re-detect regions after document edit");

model.UnfoldAll();
// Insert a new method after "Subscribe".
int insertLine = 0;
for (int i = 0; i < doc.LineCount; i++)
{
    if (doc.GetLine(i).Contains("list.Add(handler)")) { insertLine = i + 2; break; }
}
int insertOffset = doc.PositionToOffset(insertLine, 0);
string newMethod =
    "        public int Count(string topic)\n" +
    "        {\n" +
    "            return _handlers.TryGetValue(topic, out var l) ? l.Count : 0;\n" +
    "        }\n\n";
doc.Insert(insertOffset, newMethod);

model.UpdateRegions(strategy);
Console.WriteLine($"\n  After inserting a new method: {model.Regions.Count} region(s) detected.");
Console.WriteLine($"  Fold state preserved: {model.Regions.Count(r => r.IsFolded)} still folded.\n");

foreach (var r in model.Regions)
{
    Esc("36");
    Console.Write($"  [{r.StartLine + 1,2} – {r.EndLine + 1,2}]");
    Reset();
    Console.Write($"  {(r.IsFolded ? "folded" : "open  ")}  ");
    Esc("33");
    string lbl = r.Label.Length > 48 ? r.Label[..48] + " …" : r.Label;
    Console.WriteLine($"\"{lbl}\"");
    Reset();
}

Console.WriteLine();
