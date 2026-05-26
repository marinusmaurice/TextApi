using TextAPI.Core.Diff;

bool colour = !Console.IsOutputRedirected;

// ── Sample data ───────────────────────────────────────────────────────────────

// Scenario 1 — a small refactor: rename a method and fix its body
string scenarioOld1 = """
using System;

public class Calculator
{
    // Compute the sum of two integers
    public int Add(int a, int b)
    {
        return a + b;
    }

    // Compute the product of two integers
    public int Multiply(int x, int y)
    {
        return x * y;
    }

    public double Divide(int a, int b)
    {
        return a / b;
    }
}
""";

string scenarioNew1 = """
using System;

public class Calculator
{
    /// <summary>Returns the sum of <paramref name="a"/> and <paramref name="b"/>.</summary>
    public int Add(int a, int b) => a + b;

    /// <summary>Returns the product of <paramref name="a"/> and <paramref name="b"/>.</summary>
    public int Multiply(int a, int b) => a * b;

    /// <summary>Returns a divided by b; throws on divide-by-zero.</summary>
    public double Divide(int a, int b)
    {
        if (b == 0) throw new DivideByZeroException(nameof(b));
        return (double)a / b;
    }
}
""";

// Scenario 2 — a Git-style config file tweak (no C# — tests language-agnostic diff)
string scenarioOld2 = """
[server]
host     = localhost
port     = 8080
timeout  = 30
workers  = 4

[database]
engine   = sqlite
path     = ./data/app.db
pool     = 5

[logging]
level    = info
file     = ./logs/app.log
""";

string scenarioNew2 = """
[server]
host     = 0.0.0.0
port     = 443
timeout  = 60
workers  = 16

[database]
engine   = postgres
host     = db.internal
port     = 5432
name     = appdb
pool     = 20

[logging]
level    = warn
file     = ./logs/app.log
rotate   = daily
""";

// Scenario 3 — DiffChars: highlight changed characters within a single line
(string, string)[] charPairs =
[
    ("public int Add(int a, int b)",      "public int Add(int a, int b) => a + b;"),
    ("engine   = sqlite",                  "engine   = postgres"),
    ("return x * y;",                      "return a * b;"),
    ("level    = info",                    "level    = warn"),
];

// ── Rendering helpers ─────────────────────────────────────────────────────────

void PrintHeader(string title)
{
    if (colour) Console.Write("\x1b[1;97m");   // bold white
    Console.WriteLine($"\n{'━'.ToString().PadRight(0)}{'━'.ToString().PadLeft(0)}");
    Console.WriteLine($"  {title}");
    int width = colour ? Math.Max(40, Math.Min(Console.WindowWidth - 1, 72)) : 72;
    Console.WriteLine(new string('━', width));
    if (colour) Console.Write("\x1b[0m");
}

void PrintStats(DiffResult r)
{
    if (colour) Console.Write("\x1b[90m");
    Console.Write($"  {r.Hunks.Count(h => h.Kind != DiffKind.Equal)} change section(s)  ");
    if (colour) Console.Write("\x1b[32m");
    Console.Write($"+{r.AddedLines} line(s)");
    if (colour) Console.Write("\x1b[31m");
    Console.Write($"  -{r.DeletedLines} line(s)");
    if (colour) Console.Write("\x1b[0m");
    Console.WriteLine();
}

void PrintUnifiedDiff(DiffResult r, string oldName, string newName, int ctx = 3)
{
    string unified = r.ToUnifiedDiff(oldName, newName, ctx);
    foreach (string line in unified.Split('\n'))
    {
        if (!colour) { Console.WriteLine(line); continue; }

        string esc = line switch
        {
            _ when line.StartsWith("---") || line.StartsWith("+++") => "\x1b[1;97m",  // bold
            _ when line.StartsWith("@@")                            => "\x1b[96m",     // cyan
            _ when line.StartsWith('+')                             => "\x1b[32m",     // green
            _ when line.StartsWith('-')                             => "\x1b[31m",     // red
            _                                                       => "\x1b[90m",     // grey context
        };
        Console.Write(esc);
        Console.WriteLine(line);
        Console.Write("\x1b[0m");
    }
}

void PrintCharDiff(string oldLine, string newLine)
{
    var spans = TextDiff.DiffChars(oldLine, newLine);

    Console.Write("  old: ");
    foreach (var s in spans)
    {
        if (s.Kind == DiffKind.Insert) continue;
        if (colour && s.Kind == DiffKind.Delete) Console.Write("\x1b[41;97m");  // red bg
        Console.Write(s.Text);
        if (colour && s.Kind == DiffKind.Delete) Console.Write("\x1b[0m");
    }
    Console.WriteLine();

    Console.Write("  new: ");
    foreach (var s in spans)
    {
        if (s.Kind == DiffKind.Delete) continue;
        if (colour && s.Kind == DiffKind.Insert) Console.Write("\x1b[42;97m");  // green bg
        Console.Write(s.Text);
        if (colour && s.Kind == DiffKind.Insert) Console.Write("\x1b[0m");
    }
    Console.WriteLine();
}

// ── Run scenarios ─────────────────────────────────────────────────────────────

PrintHeader("Scenario 1 — C# refactor  (method signatures + body changes)");
var r1 = TextDiff.Diff(scenarioOld1, scenarioNew1);
PrintStats(r1);
PrintUnifiedDiff(r1, "Calculator.cs (before)", "Calculator.cs (after)");

PrintHeader("Scenario 2 — Config file  (server / database / logging changes)");
var r2 = TextDiff.Diff(scenarioOld2, scenarioNew2);
PrintStats(r2);
PrintUnifiedDiff(r2, "app.conf (before)", "app.conf (after)", ctx: 2);

PrintHeader("Scenario 3 — DiffChars  (character-level inline highlighting)");
Console.WriteLine();
foreach (var (a, b) in charPairs)
    PrintCharDiff(a, b);

PrintHeader("Scenario 4 — Options: IgnoreWhitespace + IgnoreCase");
string[] v1 = ["  Public Void Setup()",  "    this.X = 0;", "    this.Y = 0;", "  }"];
string[] v2 = ["public void Setup()",    "  this.x = 0;",   "  this.y = 0;",   "}"];

Console.WriteLine("\n  With defaults (whitespace + case sensitive):");
var rSensitive = TextDiff.Diff(v1, v2);
Console.Write("  ");
if (colour) Console.Write(rSensitive.HasChanges ? "\x1b[31m" : "\x1b[32m");
Console.WriteLine(rSensitive.HasChanges
    ? $"Differs: +{rSensitive.AddedLines} / -{rSensitive.DeletedLines}"
    : "Identical");
if (colour) Console.Write("\x1b[0m");

Console.WriteLine("\n  With IgnoreWhitespace + IgnoreCase:");
var rLenient = TextDiff.Diff(v1, v2,
    new DiffOptions { IgnoreWhitespace = true, IgnoreCase = true });
Console.Write("  ");
if (colour) Console.Write(rLenient.HasChanges ? "\x1b[31m" : "\x1b[32m");
Console.WriteLine(rLenient.HasChanges
    ? $"Differs: +{rLenient.AddedLines} / -{rLenient.DeletedLines}"
    : "Identical");
if (colour) Console.Write("\x1b[0m");

Console.WriteLine();
