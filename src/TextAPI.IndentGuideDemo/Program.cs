using TextAPI.Core;
using TextAPI.Core.Language;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Source = """
    public class Example
    {
        public void Method()
        {
            if (condition)
            {
                DoSomething();
                if (nested)
                {
                    DeepCode();
                }
            }
            else
            {
                OtherCode();
            }
        }

        private void Helper()
        {
            for (int i = 0; i < 10; i++)
            {
                Process(i);
            }
        }
    }
    """;

var doc = new TextDocument();
doc.Load(Source);

// ── Scenario 1: Show all indent guides ────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Indent guides with │ markers");
Console.WriteLine("══════════════════════════════════════════════\n");

var guides = doc.GetIndentGuides(0, doc.LineCount - 1, tabWidth: 4);
Console.WriteLine($"  Found {guides.Count} indent guides:\n");

// Print each guide
foreach (var g in guides)
    Console.WriteLine($"    col={g.Column}  lines {g.StartLine + 1}..{g.EndLine + 1}");

Console.WriteLine();

// Render source with │ at guide columns
RenderWithGuides(doc, guides);

// ── Scenario 2: Different tab widths ─────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Tab-indented code (tabWidth=2)");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc2 = new TextDocument();
doc2.Load("function foo() {\n  if (x) {\n    doA();\n    doB();\n  }\n  return 1;\n}");
var guides2 = doc2.GetIndentGuides(0, doc2.LineCount - 1, tabWidth: 2);
Console.WriteLine($"  Found {guides2.Count} guides (tabWidth=2)");
RenderWithGuides(doc2, guides2);

// ── Scenario 3: Blank lines are transparent ───────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Blank lines don't break guide spans");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc3 = new TextDocument();
doc3.Load("class Foo\n{\n    void A()\n    {\n        x = 1;\n    }\n\n    void B()\n    {\n        y = 2;\n    }\n}");
var guides3 = doc3.GetIndentGuides(0, doc3.LineCount - 1, tabWidth: 4);
Console.WriteLine($"  Found {guides3.Count} guides (blank line between methods)");
RenderWithGuides(doc3, guides3);

static void RenderWithGuides(TextDocument doc, IReadOnlyList<IndentGuide> guides)
{
    // Build set of (line, col) positions that need a │
    var guidePositions = new HashSet<(int line, int col)>();
    foreach (var g in guides)
        for (int ln = g.StartLine; ln <= g.EndLine; ln++)
            guidePositions.Add((ln, g.Column));

    for (int i = 0; i < doc.LineCount; i++)
    {
        string text = doc.GetLine(i);
        var sb = new System.Text.StringBuilder("  ");

        // Render text with guide markers at the appropriate column positions
        int col = 0;
        foreach (char c in text)
        {
            if (guidePositions.Contains((i, col)))
                sb.Append("\x1b[90m│\x1b[0m");
            else
                sb.Append(c);
            col++;
        }
        Console.WriteLine(sb.ToString());
    }
    Console.WriteLine();
}
