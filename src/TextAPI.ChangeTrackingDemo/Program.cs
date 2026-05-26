using TextAPI.Core;
using TextAPI.Core.ChangeTracking;

// ── Change Tracking Demo ──────────────────────────────────────────────────────
//
// Shows the gutter change-bar model (like VS Code's line decorations):
//   ▌ green  = Added
//   ▌ yellow = Modified
//   ◂ red    = baseline lines deleted just above this position
//   (space)  = Clean

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Sample = """
    using System;

    namespace Demo
    {
        public class Greeter
        {
            public string Name { get; set; } = "World";

            public void Greet()
            {
                Console.WriteLine($"Hello, {Name}!");
            }
        }
    }
    """;

var doc     = new TextDocument();
var tracker = doc.GetChangeTracker();

// Wire up the ChangesUpdated event (a real renderer would repaint the gutter here)
tracker.ChangesUpdated += (_, _) => { /* repaint gutter */ };

// ── Scenario 1: Fresh load — all lines Clean ──────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Fresh Load — all lines Clean");
Console.WriteLine("══════════════════════════════════════════════");
doc.Load(Sample);
PrintGutter(doc, tracker);

// ── Scenario 2: Modify an existing line ───────────────────────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Modify line 7 (Name property default)");
Console.WriteLine("══════════════════════════════════════════════");
// Find "World" on the Name property line and change it
int worldOffset = doc.GetText().IndexOf("\"World\"", StringComparison.Ordinal);
doc.Replace(worldOffset, 7, "\"Alice\"");
PrintGutter(doc, tracker);

// ── Scenario 3: Insert new lines ─────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Insert a new method after Greet()");
Console.WriteLine("══════════════════════════════════════════════");
int closingBrace = doc.GetText().LastIndexOf("        }\n", StringComparison.Ordinal);
int insertAt     = closingBrace + "        }\n".Length;
string newMethod = "\n        public void SayBye()\n        {\n            Console.WriteLine(\"Goodbye!\");\n        }\n";
doc.Insert(insertAt, newMethod);
PrintGutter(doc, tracker);

// ── Scenario 4: Delete a line ─────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 4: Delete the 'using System;' line");
Console.WriteLine("══════════════════════════════════════════════");
int usingEnd = doc.GetText().IndexOf('\n') + 1; // end of first line
doc.Delete(0, usingEnd);
PrintGutter(doc, tracker);

// ── Scenario 5: Undo the delete — line comes back Clean ───────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 5: Undo the delete");
Console.WriteLine("══════════════════════════════════════════════");
doc.Undo();
PrintGutter(doc, tracker);

// ── Scenario 6: Save — baseline resets, all lines become Clean ────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 6: Save — all marks reset to Clean");
Console.WriteLine("══════════════════════════════════════════════");
await using var stream = new MemoryStream();
await doc.SaveAsync(stream);
PrintGutter(doc, tracker);

// ── Scenario 7: Edit after save — relative to NEW baseline ───────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 7: Edit again (relative to saved version)");
Console.WriteLine("══════════════════════════════════════════════");
int nameOffset = doc.GetText().IndexOf("\"Alice\"", StringComparison.Ordinal);
doc.Replace(nameOffset, 7, "\"Bob\"");
PrintGutter(doc, tracker);

// ── Summary ───────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Summary");
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine($"  Changed lines : {tracker.ChangedLines().Count()}");
Console.WriteLine($"  Deletion pts  : {tracker.DeletionPoints().Count()}");
Console.WriteLine($"  HasAnyChanges : {tracker.HasAnyChanges}");

// ── Helpers ───────────────────────────────────────────────────────────────────

static void PrintGutter(TextDocument doc, ChangeTracker tracker)
{
    Console.WriteLine();
    for (int i = 0; i < doc.LineCount; i++)
    {
        // Deletion marker above this line?
        if (tracker.HasDeletionAbove(i))
            PrintDeletionMarker(i);

        var status = tracker.GetStatus(i);
        string gutter = status switch
        {
            LineStatus.Added    => ColorText("▌ ", ConsoleColor.Green),
            LineStatus.Modified => ColorText("▌ ", ConsoleColor.Yellow),
            _                   => "  ",
        };

        string lineNum = $"{i + 1,3}";
        string content = doc.GetLine(i);
        Console.WriteLine($"  {lineNum} {gutter}{content}");
    }

    // Deletion marker after the last line?
    if (tracker.HasDeletionAbove(doc.LineCount))
        PrintDeletionMarker(doc.LineCount);

    Console.WriteLine();
    int addedCount    = tracker.ChangedLines().Count(l => tracker.GetStatus(l) == LineStatus.Added);
    int modifiedCount = tracker.ChangedLines().Count(l => tracker.GetStatus(l) == LineStatus.Modified);
    int deletedCount  = tracker.DeletionPoints().Count();
    Console.Write("  ");
    Console.Write(ColorText($"+{addedCount} added  ", ConsoleColor.Green));
    Console.Write(ColorText($"~{modifiedCount} modified  ", ConsoleColor.Yellow));
    Console.WriteLine(ColorText($"-{deletedCount} deletion points", ConsoleColor.Red));
}

static void PrintDeletionMarker(int atLine)
{
    string marker = ColorText($"  ◂── deleted above line {atLine + 1}", ConsoleColor.Red);
    Console.WriteLine(marker);
}

static string ColorText(string text, ConsoleColor color)
{
    // ANSI escape codes for terminal colour
    string code = color switch
    {
        ConsoleColor.Green  => "\x1b[32m",
        ConsoleColor.Yellow => "\x1b[33m",
        ConsoleColor.Red    => "\x1b[31m",
        _                   => "",
    };
    return $"{code}{text}\x1b[0m";
}
