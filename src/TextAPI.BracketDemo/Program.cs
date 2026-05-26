using TextAPI.Core;
using TextAPI.Core.Language;

bool colour = !Console.IsOutputRedirected;

// ── Embedded sample ────────────────────────────────────────────────────────

string sample = """
using System;
using System.Collections.Generic;

namespace Demo
{
    public class Stack<T>
    {
        private readonly List<T> _items = new List<T>();

        public void Push(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _items.Add(item);
        }

        public T Pop()
        {
            if (_items.Count == 0)
                throw new InvalidOperationException("Stack is empty.");
            int last = _items.Count - 1;
            T   val  = _items[last];
            _items.RemoveAt(last);
            return val;
        }

        public bool TryPeek(out T value)
        {
            if (_items.Count == 0) { value = default!; return false; }
            value = _items[_items.Count - 1];
            return true;
        }

        // Pairs: ( ) [ ] { }
        public string Describe() => $"Stack({_items.Count} item(s))";
    }
}
""";

var doc = new TextDocument(new CSharpTokeniser());
doc.Load(sample);

// ── Rendering helpers ──────────────────────────────────────────────────────

void Esc(string code)  { if (colour) Console.Write($"\x1b[{code}m"); }
void Reset()           { if (colour) Console.Write("\x1b[0m"); }

void PrintHeader(string title)
{
    int width = colour ? Math.Max(40, Math.Min(Console.WindowWidth - 1, 72)) : 72;
    Esc("1;97");
    Console.WriteLine($"\n{new string('━', width)}");
    Console.WriteLine($"  {title}");
    Console.WriteLine(new string('━', width));
    Reset();
}

// ── Scenario 1: Bracket matching ──────────────────────────────────────────

PrintHeader("Scenario 1 — Bracket matching  (open ↔ close, skips strings & comments)");

// Locate brackets by searching the source text — more robust than hardcoding line/col.
string full = doc.GetText();

int BracketAfter(string needle, char bracket, int skip = 0)
{
    int pos = full.IndexOf(needle, StringComparison.Ordinal);
    if (pos < 0) return -1;
    int found = 0;
    for (int i = pos; i < full.Length; i++)
    {
        if (full[i] == bracket && found++ == skip) return i;
    }
    return -1;
}

(int Offset, string Name, string Description)[] probes =
[
    (BracketAfter("namespace Demo", '{'),  "namespace {", "namespace body open brace"),
    (BracketAfter("public class Stack",'{'),"class {",     "class body open brace"),
    (BracketAfter("void Push",      '('),  "Push (",      "Push parameter list"),
    (BracketAfter("void Push",      '{'),  "Push {",      "Push method body"),
    (BracketAfter("public T Pop",   '{'),  "Pop {",       "Pop method body"),
    (BracketAfter("bool TryPeek",   '{'),  "TryPeek {",   "TryPeek method body"),
];

Console.WriteLine();

foreach (var (offset, name, desc) in probes)
{
    char ch      = doc.GetText(offset, 1)[0];
    int  match   = doc.FindMatchingBracket(offset);

    Esc("96");
    Console.Write($"  {ch} at offset {offset,4}");
    Reset();
    Console.Write($"  ({desc})");

    if (match >= 0)
    {
        var (matchLine, matchCol) = doc.OffsetToPosition(match);
        char matchCh = doc.GetText(match, 1)[0];
        Esc("32");
        Console.Write($"  →  {matchCh} at offset {match,4}  (line {matchLine + 1}, col {matchCol + 1})");
        Reset();
    }
    else
    {
        Esc("31");
        Console.Write("  →  no match (unbalanced)");
        Reset();
    }
    Console.WriteLine();
}

// Also verify round-trip: match(match(x)) == x
Console.WriteLine();
Esc("90");
Console.WriteLine("  Round-trip verification (match(match(offset)) == offset):");
Reset();
foreach (var (offset, name, _) in probes)
{
    int m1   = doc.FindMatchingBracket(offset);
    int m2   = m1 >= 0 ? doc.FindMatchingBracket(m1) : -1;
    bool ok  = m2 == offset;
    Esc(ok ? "32" : "31");
    Console.Write($"  {(ok ? "✓" : "✗")}  {name,-14} offset {offset} → {m1} → {m2}");
    Reset();
    Console.WriteLine();
}

// ── Scenario 2: Brackets inside strings and comments ──────────────────────

PrintHeader("Scenario 2 — Brackets inside strings/comments are skipped");

string[] trickySamples =
[
    "(\"()\")                   // string contains () — skipped",
    "( // (\n)                  // line comment contains ( — skipped",
    "( /* ( */ )               // block comment contains ( — skipped",
    "{ string s = \"}\"; }      // string contains } — skipped",
];

foreach (string raw in trickySamples)
{
    // Extract just the code part (before //)
    string code = raw.Contains("//") ? raw[..raw.IndexOf("//")].TrimEnd() : raw;
    // Replace \n placeholder with real newline
    code = code.Replace("\\n", "\n");

    var d    = new TextDocument(new CSharpTokeniser());
    d.Load(code);
    int open  = d.GetText().IndexOf('(') >= 0 ? d.GetText().IndexOf('(')
              : d.GetText().IndexOf('{');
    char oc   = d.GetText(open, 1)[0];
    int match = d.FindMatchingBracket(open);
    char? mc  = match >= 0 ? d.GetText(match, 1)[0] : null;

    Esc("90");
    // Show just the first line for readability
    string display = code.Split('\n')[0];
    if (code.Contains('\n')) display += "↵…";
    Console.Write($"  {display,-42}  ");
    Reset();
    if (match >= 0)
    {
        Esc("32");
        Console.Write($"{oc} @ {open} matched {mc} @ {match}");
        Reset();
    }
    else
    {
        Esc("31");
        Console.Write("no match");
        Reset();
    }
    Console.WriteLine();
}

// ── Scenario 3: Auto-indent on Enter ────────────────────────────────────

PrintHeader("Scenario 3 — Auto-indent on Enter  (copies indent, +1 level after {)");

// Build probes by locating key lines in the document.
int LineOf(string needle)
{
    for (int i = 0; i < doc.LineCount; i++)
        if (doc.GetLine(i).Contains(needle)) return i;
    return 0;
}

int NamespaceLine  = LineOf("namespace Demo");
int ClassLine      = LineOf("public class Stack");
int PushSigLine    = LineOf("public void Push");
int PushBodyLine   = LineOf("public void Push") + 1;   // { is on next line
int PopBodyLine    = LineOf("public T Pop(") + 1;
int PopPlainLine   = LineOf("int last = _items.Count - 1");
int TryPeekSigLine = LineOf("public bool TryPeek");
int TryPeekBodyLine= LineOf("public bool TryPeek") + 1;

(int Offset, string Comment)[] enterPoints =
[
    (doc.PositionToOffset(0,              doc.GetLine(0).Length),  "top-level line  → no indent"),
    (doc.PositionToOffset(NamespaceLine,  doc.GetLine(NamespaceLine).Length),  "namespace Demo  → indent for body"),
    (doc.PositionToOffset(ClassLine,      doc.GetLine(ClassLine).Length),      "public class Stack<T>  → class-body indent"),
    (doc.PositionToOffset(PushBodyLine,   doc.GetLine(PushBodyLine).Length),   "Push method {  → method-body indent"),
    (doc.PositionToOffset(PopPlainLine,   doc.GetLine(PopPlainLine).Length),   "plain body line  → copies indent"),
    (doc.PositionToOffset(TryPeekBodyLine,doc.GetLine(TryPeekBodyLine).Length),"TryPeek { (with inline comment)  → method-body indent"),
];

Console.WriteLine();
foreach (var (offset, comment) in enterPoints)
{
    var (line, _) = doc.OffsetToPosition(offset);
    string indent = doc.GetAutoIndent(offset);
    string lineText = doc.GetLine(line);

    Esc("90");
    Console.Write($"  Line {line + 1,2}  ");
    Reset();

    // Show truncated line content
    string display = lineText.Length > 40 ? lineText[..40] + "…" : lineText;
    Console.Write($"│{display,-41}│  ");

    Esc("32");
    // Show indent as a visible string (replace spaces with · for clarity)
    string indentVis = indent.Replace("    ", "····").Replace(" ", "·").Replace("\t", "→");
    Console.Write($"indent = \"{indentVis}\"");
    Reset();

    Esc("90");
    Console.Write($"  ← {comment}");
    Reset();
    Console.WriteLine();
}

// ── Scenario 4: Closing-brace de-indent ──────────────────────────────────

PrintHeader("Scenario 4 — Closing-brace de-indent  (} snaps to its { line's indent)");

Console.WriteLine();

// Walk every '}' in the document and show what indent it should snap to.
string fullText = doc.GetText();
for (int offset = 0; offset < fullText.Length; offset++)
{
    if (fullText[offset] != '}') continue;

    string? snapIndent = doc.GetClosingBraceIndent(offset);
    if (snapIndent == null) continue;

    var (brLine, brCol) = doc.OffsetToPosition(offset);
    int matchOff        = doc.FindMatchingBracket(offset);
    var (opLine, _)     = doc.OffsetToPosition(matchOff);

    Esc("90");
    Console.Write($"  }} at line {brLine + 1,2}, col {brCol + 1,2}");
    Reset();
    Console.Write($"  matches {{ on line {opLine + 1,2}  →  snap indent = ");
    Esc("33");
    string snapVis = snapIndent.Replace("    ", "····").Replace(" ", "·").Replace("\t", "→");
    Console.Write($"\"{snapVis}\"");
    Reset();
    Console.WriteLine($"  ({snapIndent.Length} chars)");
}

Console.WriteLine();
