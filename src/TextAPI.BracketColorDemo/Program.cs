using TextAPI.Core;
using TextAPI.Core.Language;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string Source = """
    public class Calculator
    {
        public int Add(int a, int b)
        {
            return a + b;
        }

        public int[] GetValues(int count)
        {
            var result = new int[count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = i * 2;
            }
            return result;
        }
    }
    """;

var doc = new TextDocument(new CSharpTokeniser());
doc.Load(Source);

// ── Scenario 1: Show all bracket pairs with ANSI colors ───────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Bracket pairs colored by depth");
Console.WriteLine("══════════════════════════════════════════════\n");

var pairs = doc.GetBracketPairs(0, doc.LineCount - 1);
Console.WriteLine($"  Found {pairs.Count} bracket pairs\n");

// Render document with colored brackets
string[] colors = ["\x1b[33m", "\x1b[36m", "\x1b[35m"]; // yellow, cyan, magenta
string reset = "\x1b[0m";
string unmatchedColor = "\x1b[31m"; // red

// Build a lookup: offset → color string
var colorMap = new Dictionary<int, string>();
foreach (var pair in pairs)
{
    string c = pair.ColorIndex >= 0 ? colors[pair.ColorIndex] : unmatchedColor;
    if (pair.OpenOffset  >= 0) colorMap[pair.OpenOffset]  = c;
    if (pair.CloseOffset >= 0) colorMap[pair.CloseOffset] = c;
}

string text = doc.GetText();
var sb = new System.Text.StringBuilder("  ");
for (int i = 0; i < text.Length; i++)
{
    if (text[i] == '\n') { sb.Append("\n  "); continue; }
    if (colorMap.TryGetValue(i, out string? col))
        sb.Append($"{col}{text[i]}{reset}");
    else
        sb.Append(text[i]);
}
Console.WriteLine(sb.ToString());

// ── Scenario 2: Depth summary ─────────────────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Pair summary by color index");
Console.WriteLine("══════════════════════════════════════════════\n");

for (int ci = 0; ci <= 2; ci++)
{
    int count = pairs.Count(p => p.ColorIndex == ci);
    Console.WriteLine($"  Color {ci} ({(ci == 0 ? "yellow" : ci == 1 ? "cyan" : "magenta")}): {count} pairs");
}
int unmatched = pairs.Count(p => p.ColorIndex == -1);
Console.WriteLine($"  Unmatched (red): {unmatched} brackets");

// ── Scenario 3: Strings/comments excluded ────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Brackets in strings/comments excluded");
Console.WriteLine("══════════════════════════════════════════════\n");

var testDoc = new TextDocument(new CSharpTokeniser());
testDoc.Load("var s = \"(not a bracket)\"; // (also not) \nfoo(bar);");
var testPairs = testDoc.GetBracketPairs(0, testDoc.LineCount - 1);
Console.WriteLine($"  Source: var s = \"(not a bracket)\"; // (also not)");
Console.WriteLine($"  Code:   foo(bar);");
Console.WriteLine($"  Pairs found: {testPairs.Count} (expected 1 — only the foo() call)");
foreach (var p in testPairs)
{
    var (ol, oc) = testDoc.OffsetToPosition(p.OpenOffset);
    Console.WriteLine($"    ({ol},{oc}) color={p.ColorIndex}");
}
