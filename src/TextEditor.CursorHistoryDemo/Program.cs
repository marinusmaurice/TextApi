using TextEditor.Core;
using TextEditor.Core.Navigation;

// ─────────────────────────────────────────────────────────────────────────────
// Cursor position history — demo
//
// Simulates a developer session: five Find-Next jumps across a document,
// then Alt+Left (Back) navigation to retrace the path, and Alt+Right
// (Forward) to return to the most recent position.
// ─────────────────────────────────────────────────────────────────────────────

const string Source = """
    // Line  0: top of file
    using System;

    namespace Demo
    {
        // Line  5: namespace body
        public class Program
        {
            // Line  8: class body
            static void Main()
            {
                Console.WriteLine("hello");   // line 11
                var x = Compute(42);           // line 12
                Console.WriteLine(x);          // line 13
            }

            static int Compute(int n)          // line 16
            {
                return n * n;                  // line 18
            }
        }
    }
    """;

var doc = new TextDocument();
doc.Load(Source);

var history = doc.GetCursorHistory();

// Helpers — convert offset to (line, col) for display.
(int line, int col) Pos(int offset) => doc.OffsetToPosition(offset);
string Fmt(HistoryEntry? e) =>
    e is null
        ? "(none)"
        : $"offset {e.Value.Offset,4}  → line {Pos(e.Value.Offset).line,2}" +
          (e.Value.FilePath is not null ? $"  [{e.Value.FilePath}]" : "");

Console.WriteLine("═══════════════════════════════════════════════════════");
Console.WriteLine("  Cursor position history demo");
Console.WriteLine("═══════════════════════════════════════════════════════\n");

// ── Simulate 5 Find-Next jumps ────────────────────────────────────────────

Console.WriteLine("── Simulating 5 Find-Next jumps ─────────────────────");

var jumps = new[] {
    doc.FindFirst("using")?.Offset ?? 0,
    doc.FindFirst("namespace")?.Offset ?? 0,
    doc.FindFirst("Main")?.Offset ?? 0,
    doc.FindFirst("Compute")?.Offset ?? 0,
    doc.FindFirst("return")?.Offset ?? 0,
};

foreach (var offset in jumps)
{
    history.Push(offset);
    var (line, col) = Pos(offset);
    Console.WriteLine($"  → jumped to offset {offset,4}  (line {line,2}, col {col})");
}

Console.WriteLine($"\nHistory count: {history.Count}  |  Current: {Fmt(history.Current)}");
Console.WriteLine($"CanGoBack={history.CanGoBack}  CanGoForward={history.CanGoForward}");

// ── Navigate backwards ────────────────────────────────────────────────────

Console.WriteLine("\n── Alt+Left (Back) ──────────────────────────────────");
while (history.CanGoBack)
{
    var entry = history.Back();
    Console.WriteLine($"  ← Back  →  {Fmt(entry)}  " +
                      $"[CanBack={history.CanGoBack}, CanFwd={history.CanGoForward}]");
}
Console.WriteLine("  (reached oldest entry — Back is now no-op)");
Console.WriteLine($"  extra Back() = {history.Back()}");

// ── Navigate forwards ─────────────────────────────────────────────────────

Console.WriteLine("\n── Alt+Right (Forward) ──────────────────────────────");
while (history.CanGoForward)
{
    var entry = history.Forward();
    Console.WriteLine($"  → Fwd   →  {Fmt(entry)}  " +
                      $"[CanBack={history.CanGoBack}, CanFwd={history.CanGoForward}]");
}
Console.WriteLine("  (reached newest entry — Forward is now no-op)");

// ── Push mid-history truncates forward ───────────────────────────────────

Console.WriteLine("\n── New jump mid-history truncates forward entries ───");
history.Back(); history.Back(); // step back two
Console.WriteLine($"  Stepped back twice — Current: {Fmt(history.Current)}");
Console.WriteLine($"  CanGoForward before new jump: {history.CanGoForward}");
int newOffset = doc.FindFirst("Console")?.Offset ?? 0;
history.Push(newOffset, "Program.cs");
Console.WriteLine($"  New jump to offset {newOffset} (Console.WriteLine)");
Console.WriteLine($"  CanGoForward after new jump:  {history.CanGoForward}");
Console.WriteLine($"  History count: {history.Count}  (forward entries discarded)");

// ── Capacity eviction ─────────────────────────────────────────────────────

Console.WriteLine("\n── Capacity eviction (cap=3) ────────────────────────");
{
    var small = new CursorHistory(capacity: 3);
    small.Push(100); small.Push(200); small.Push(300); // full
    small.Push(400); // evicts 100
    Console.WriteLine($"  Pushed 100,200,300,400 into cap-3 buffer.");
    Console.WriteLine($"  Count={small.Count}  (oldest 100 evicted)");
    Console.WriteLine($"  Back chain: ", true);
    Console.Write("  ");
    while (small.CanGoBack)
        Console.Write(small.Back()!.Value.Offset + " ← ");
    Console.WriteLine("(start)");
}

// ── Load clears history ───────────────────────────────────────────────────

Console.WriteLine("\n── Load clears the history ──────────────────────────");
Console.WriteLine($"  Before load: history.Count = {history.Count}");
doc.Load("fresh content");
Console.WriteLine($"  After  load: history.Count = {history.Count}");

Console.WriteLine("\n═══════════════════════════════════════════════════════");
Console.WriteLine("  Done.");
