using System.Text;
using TextAPI.Core;
using TextAPI.Core.Language;
using TextAPI.Core.Cursor;

// ─────────────────────────────────────────────────────────────────────────────
// TextAPI Unicode Demo
// ─────────────────────────────────────────────────────────────────────────────
// Demonstrates:
//   1. Grapheme cluster navigation (NextCluster / PreviousCluster)
//   2. Display width (East Asian Width — CJK = 2 columns, emoji = 2 columns)
//   3. ClusterCount vs CodeUnitCount vs RuneCount
//   4. Word boundaries with CJK / emoji
//   5. Cursor movement through Unicode clusters (correct vs naïve char stepping)
//   6. DocumentStats — grapheme-aware status bar numbers
// ─────────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = Encoding.UTF8;
bool colour = !Console.IsOutputRedirected;

void Esc(string code)  { if (colour) Console.Write($"\x1b[{code}m"); }
void Reset()           { if (colour) Console.Write("\x1b[0m"); }
void Bold()            => Esc("1");
void Dim()             => Esc("2");
void Cyan()            => Esc("36");
void Yellow()          => Esc("33");
void Green()           => Esc("32");
void Red()             => Esc("31");
void Magenta()         => Esc("35");

void Header(string title)
{
    int width = colour ? Math.Max(40, Math.Min(Console.WindowWidth - 1, 72)) : 72;
    Console.WriteLine();
    Bold(); Esc("97");
    Console.WriteLine(new string('━', width));
    Console.WriteLine($"  {title}");
    Console.WriteLine(new string('━', width));
    Reset();
}

void Row(string label, string value, string colCode = "32")
{
    Dim(); Console.Write($"  {label,-32}"); Reset();
    Esc(colCode); Console.Write(value); Reset();
    Console.WriteLine();
}

// ── Well-known test strings ────────────────────────────────────────────────

const string EWithCombining = "e\u0301";               // e + combining acute
const string WavingHand     = "\U0001F44B";             // 👋
const string WavingBrown    = "\U0001F44B\U0001F3FD";  // 👋🏽 with skin tone
const string FlagUS         = "\U0001F1FA\U0001F1F8";  // 🇺🇸
const string FamilyEmoji    = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
const string CJKHello       = "你好";                   // Nǐ hǎo (2 CJK, each width 2)
const string JapaneseWord   = "日本語";                  // nihongo

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 1 — Cluster navigation basics
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 1 — Grapheme cluster sizes");
Console.WriteLine();

void ShowCluster(string label, string text)
{
    int codeUnits  = text.Length;
    int clusters   = GraphemeHelper.ClusterCount(text);
    int runes      = text.EnumerateRunes().Count();
    int dispWidth  = GraphemeHelper.TotalDisplayWidth(text);

    Dim(); Console.Write($"  {label,-22}"); Reset();
    Yellow(); Console.Write($"{text,-8}"); Reset();
    Row($"  code-units={codeUnits}  runes={runes}  clusters={clusters}  display={dispWidth}cols", "", "90");
}

ShowCluster("ASCII 'A'",                  "A");
ShowCluster("Latin precomposed é",        "\u00E9");
ShowCluster("Latin combining é",          EWithCombining);
ShowCluster("Waving hand 👋",             WavingHand);
ShowCluster("👋 + skin tone 👋🏽",        WavingBrown);
ShowCluster("US flag 🇺🇸",              FlagUS);
ShowCluster("Family 👨‍👩‍👧‍👦",             FamilyEmoji);
ShowCluster("CJK 你好",                   CJKHello);
ShowCluster("Japanese 日本語",            JapaneseWord);

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 2 — Forward / backward cluster walk
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 2 — Forward and backward cluster walk");
Console.WriteLine();

void WalkForward(string text)
{
    Console.Write("  Forward : ");
    int offset = 0;
    while (offset < text.Length)
    {
        int next    = GraphemeHelper.NextCluster(text, offset);
        string cl   = text[offset..next];
        int    w    = GraphemeHelper.DisplayWidth(cl);
        Yellow(); Console.Write($"[{cl}]"); Reset();
        Dim();    Console.Write($"(+{next - offset}cu,w{w}) "); Reset();
        offset = next;
    }
    Console.WriteLine();
}

void WalkBackward(string text)
{
    Console.Write("  Backward: ");
    int offset = text.Length;
    var clusters = new Stack<string>();
    while (offset > 0)
    {
        int prev = GraphemeHelper.PreviousCluster(text, offset);
        clusters.Push(text[prev..offset]);
        offset = prev;
    }
    foreach (var cl in clusters)
    {
        Yellow(); Console.Write($"[{cl}]"); Reset();
        Console.Write(" ");
    }
    Console.WriteLine();
}

string mixedText = "Hi " + WavingHand + " " + FlagUS + " " + CJKHello + "!";
Dim(); Console.WriteLine($"  Text: \"{mixedText}\" ({mixedText.Length} code units)"); Reset();
Console.WriteLine();
WalkForward(mixedText);
WalkBackward(mixedText);

Console.WriteLine();
string familyText = "Family: " + FamilyEmoji + "!";
Dim(); Console.WriteLine($"  Text: \"{familyText}\" ({familyText.Length} code units)"); Reset();
Console.WriteLine();
WalkForward(familyText);
WalkBackward(familyText);

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 3 — Display-width ruler
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 3 — Display-width ruler (East Asian Width)");
Console.WriteLine();

void PrintWidthRuler(string text)
{
    // Line 1: actual characters
    Console.Write("  ");
    int offset = 0;
    while (offset < text.Length)
    {
        int next  = GraphemeHelper.NextCluster(text, offset);
        int width = GraphemeHelper.DisplayWidth(text.AsSpan()[offset..next]);
        Yellow(); Console.Write(text[offset..next]); Reset();
        if (width == 2) Console.Write(" "); // pad narrow terminal for wide chars
        offset = next;
    }
    Console.WriteLine();

    // Line 2: column widths
    Console.Write("  ");
    offset = 0;
    while (offset < text.Length)
    {
        int next  = GraphemeHelper.NextCluster(text, offset);
        int width = GraphemeHelper.DisplayWidth(text.AsSpan()[offset..next]);
        Dim(); Console.Write(width == 0 ? "0" : width.ToString());
        if (width == 2) Console.Write(" ");
        Reset();
        offset = next;
    }
    Console.WriteLine();

    int total = GraphemeHelper.TotalDisplayWidth(text);
    Dim(); Console.WriteLine($"  Total display columns: {total}"); Reset();
}

Console.WriteLine($"  ASCII \"Hello World\":");
PrintWidthRuler("Hello World");
Console.WriteLine();
Console.WriteLine($"  CJK {CJKHello} (你好):");
PrintWidthRuler(CJKHello);
Console.WriteLine();
Console.WriteLine($"  Mixed \"a{CJKHello}b\":");
PrintWidthRuler("a" + CJKHello + "b");
Console.WriteLine();
Console.WriteLine($"  Emoji mix:");
PrintWidthRuler(WavingHand + " " + WavingBrown + " " + FlagUS + " " + FamilyEmoji);

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 4 — Before/after: naïve char stepping vs cluster stepping
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 4 — Naïve char steps vs grapheme cluster steps");
Console.WriteLine();

string demo = WavingBrown + FlagUS + "abc";
Console.WriteLine($"  Text : \"{demo}\"  ({demo.Length} code units)");
Console.WriteLine();

Console.WriteLine("  Naïve (char-by-char)  vs  Grapheme-correct:");
Console.WriteLine();

int naivePos   = 0;
int correctPos = 0;
int maxSteps   = Math.Max(demo.Length, GraphemeHelper.ClusterCount(demo)) + 2;

for (int step = 0; step <= maxSteps && (naivePos <= demo.Length || correctPos <= demo.Length); step++)
{
    string naiveStr   = naivePos   < demo.Length ? demo[naivePos].ToString()   : "(end)";
    string correctStr = correctPos < demo.Length
        ? demo[correctPos..GraphemeHelper.NextCluster(demo, correctPos)]
        : "(end)";

    bool naiveMid   = naivePos   > 0 && naivePos   < demo.Length && char.IsLowSurrogate(demo[naivePos]);
    bool correctOk  = correctPos <= demo.Length;

    Console.Write($"  Step {step,2}: char[{naivePos,2}]= ");
    if (naiveMid) { Red();   Console.Write($"'{naiveStr}' (mid-pair!)"); Reset(); }
    else          { Green(); Console.Write($"'{naiveStr}'");             Reset(); }

    Console.Write("   cluster[");
    Cyan(); Console.Write($"{correctPos,2}"); Reset();
    Console.Write("]= ");
    Green(); Console.Write($"\"{correctStr}\""); Reset();
    Console.WriteLine();

    if (naivePos < demo.Length)   naivePos++;
    if (correctPos < demo.Length) correctPos = GraphemeHelper.NextCluster(demo, correctPos);
    else break;
}

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 5 — Cursor movement through Unicode
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 5 — TextCursor grapheme-aware movement");
Console.WriteLine();

string cursorText = "Hi" + WavingBrown + FlagUS + "!";
var doc    = new TextDocument();
doc.Load(cursorText);
var cursor = new TextCursor(doc, 0);

Console.WriteLine($"  Document: \"{cursorText}\"  ({cursorText.Length} code units)");
Console.WriteLine();

// Move right step by step and print position
Console.WriteLine("  Moving right:");
while (cursor.CaretOffset < doc.Length)
{
    int start = cursor.CaretOffset;
    cursor.MoveRight();
    int end = cursor.CaretOffset;
    string cluster = cursorText[start..Math.Min(end, cursorText.Length)];
    string advance = end - start == 1 ? "1 code unit" : $"{end - start} code units";
    Console.Write($"    [{start,2}→{end,2}]  ");
    Yellow(); Console.Write($"{cluster,-6}"); Reset();
    Dim(); Console.Write($"({advance})"); Reset();
    Console.WriteLine();
}

Console.WriteLine();
Console.WriteLine("  Moving left back to start:");
while (cursor.CaretOffset > 0)
{
    int end = cursor.CaretOffset;
    cursor.MoveLeft();
    int start = cursor.CaretOffset;
    string cluster = cursorText[start..Math.Min(end, cursorText.Length)];
    Console.Write($"    [{end,2}→{start,2}]  ");
    Yellow(); Console.Write($"{cluster,-6}"); Reset();
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 6 — DocumentStats (status bar numbers)
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 6 — DocumentStats (grapheme-aware status bar)");
Console.WriteLine();

(string Label, string Content)[] statDemos =
[
    ("ASCII prose",    "Hello, World!"),
    ("Latin combining",EWithCombining + EWithCombining + EWithCombining),
    ("CJK phrase",     CJKHello + " " + JapaneseWord),
    ("Emoji mix",      WavingHand + WavingBrown + FlagUS + FamilyEmoji),
    ("Multi-line",     "line one\nline two\nline three"),
    ("Mixed Unicode",  "Hi " + FamilyEmoji + " from " + CJKHello + "!"),
];

foreach (var (label, content) in statDemos)
{
    var d = new TextDocument();
    d.Load(content);
    var s = d.GetStats();
    Bold(); Esc("97"); Console.WriteLine($"\n  {label}: \"{content}\""); Reset();
    Row("  Code units  (doc.Length)", s.CodeUnitCount.ToString(), "33");
    Row("  Unicode code points",      s.RuneCount.ToString(),      "33");
    Row("  Grapheme clusters",        s.GraphemeCount.ToString(),   "32");
    Row("  Display columns",          s.DisplayColumns.ToString(),  "36");
    Row("  Words",                    s.WordCount.ToString(),       "35");
    Row("  Lines",                    s.LineCount.ToString(),       "90");
}

// ─────────────────────────────────────────────────────────────────────────────
// Scenario 7 — Delete demo (cluster-aware backspace)
// ─────────────────────────────────────────────────────────────────────────────

Header("Scenario 7 — Backspace through Unicode (cluster-aware delete)");
Console.WriteLine();

string delText = FamilyEmoji + WavingBrown + "Hi";
Console.WriteLine($"  Starting text : \"{delText}\"  ({delText.Length} code units)");
Console.WriteLine();

var delDoc    = new TextDocument();
delDoc.Load(delText);
var delCursor = new TextCursor(delDoc, delDoc.Length);

int step2 = 0;
while (delDoc.Length > 0)
{
    step2++;
    delCursor.DeleteLeft();
    string current = delDoc.GetText();
    Console.Write($"  Backspace {step2}: ");
    Yellow(); Console.Write($"\"{current}\""); Reset();
    Dim(); Console.Write($"  ({delDoc.Length} code units, {GraphemeHelper.ClusterCount(current)} clusters)");
    Reset(); Console.WriteLine();
}

Console.WriteLine();
Green(); Console.WriteLine("  All clusters deleted correctly — document is empty."); Reset();
Console.WriteLine();
