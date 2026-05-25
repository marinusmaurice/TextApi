using TextEditor.Core;
using TextEditor.Core.Language;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Minimal C# TextMate grammar embedded directly
const string Grammar = """
    {
      "scopeName": "source.cs",
      "patterns": [
        { "include": "#comments" },
        { "include": "#strings" },
        { "include": "#keywords" },
        { "include": "#numbers" }
      ],
      "repository": {
        "comments": {
          "patterns": [
            {
              "name": "comment.line.double-slash.cs",
              "match": "//.*$"
            },
            {
              "name": "comment.block.cs",
              "begin": "/\\*",
              "end": "\\*/"
            }
          ]
        },
        "strings": {
          "patterns": [
            {
              "name": "string.quoted.double.cs",
              "begin": "\"",
              "end": "\"",
              "patterns": [
                { "name": "constant.character.escape.cs", "match": "\\\\." }
              ]
            }
          ]
        },
        "keywords": {
          "patterns": [
            {
              "name": "keyword.control.cs",
              "match": "\\b(if|else|for|foreach|while|do|switch|case|break|continue|return|throw|try|catch|finally|using|namespace|class|struct|interface|enum|void|new|this|base|static|public|private|protected|internal|sealed|abstract|virtual|override|readonly|const|var|true|false|null)\\b"
            }
          ]
        },
        "numbers": {
          "patterns": [
            {
              "name": "constant.numeric.cs",
              "match": "\\b[0-9]+(\\.[0-9]+)?\\b"
            }
          ]
        }
      }
    }
    """;

const string CSharpSource = """
    using System;

    namespace Demo
    {
        // A simple calculator
        public class Calculator
        {
            /* Multi-line
               comment */
            public int Add(int a, int b)
            {
                if (a < 0 || b < 0)
                    throw new ArgumentException("negative");
                return a + b; // sum
            }

            public double Pi => 3.14159;
        }
    }
    """;

// ── Scenario 1: Tokenise with TmLanguage grammar ──────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Tokenise C# with TextMate grammar");
Console.WriteLine("══════════════════════════════════════════════\n");

var tokeniser = new TmLanguageTokeniser(Grammar);
var doc = new TextDocument(tokeniser);
doc.Load(CSharpSource);

PrintColoredSource(doc);

// ── Scenario 2: Token type distribution ───────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Token type distribution");
Console.WriteLine("══════════════════════════════════════════════\n");

var typeCount = new Dictionary<string, int>();
for (int line = 0; line < doc.LineCount; line++)
{
    foreach (var tok in doc.GetSyntaxTokens(line))
    {
        typeCount.TryGetValue(tok.Type, out int c);
        typeCount[tok.Type] = c + 1;
    }
}
foreach (var (type, count) in typeCount.OrderByDescending(x => x.Value))
    Console.WriteLine($"  {type,-20} {count} tokens");

// ── Scenario 3: Multi-line block comment ──────────────────────────────────
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Multi-line block comment state");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc2 = new TextDocument(tokeniser);
doc2.Load("int x = 1;\n/* Block comment\n   continues here\n*/\nint y = 2;");

for (int line = 0; line < doc2.LineCount; line++)
{
    var tokens = doc2.GetSyntaxTokens(line);
    string lineText = doc2.GetLine(line);
    Console.Write($"  Line {line}: \"{lineText}\" → ");
    if (tokens.Count == 0)
        Console.WriteLine("(no tokens)");
    else
        Console.WriteLine(string.Join(", ", tokens.Select(t => $"{t.Type}[{t.Start}-{t.End}]")));
}

static void PrintColoredSource(TextDocument doc)
{
    var typeColors = new Dictionary<string, string>
    {
        ["keyword"] = "\x1b[34m",   // blue
        ["comment"] = "\x1b[32m",   // green
        ["string"]  = "\x1b[33m",   // yellow
        ["number"]  = "\x1b[35m",   // magenta
    };
    string reset = "\x1b[0m";

    for (int line = 0; line < doc.LineCount; line++)
    {
        string text = doc.GetLine(line);
        var tokens  = doc.GetSyntaxTokens(line);
        int lineStart = doc.PositionToOffset(line, 0);

        // Build a list of (start, end, color) spans
        var spans = new List<(int start, int end, string color)>();
        foreach (var tok in tokens)
        {
            int s = tok.Start - lineStart;
            int e = tok.End   - lineStart;
            if (s >= 0 && e <= text.Length && typeColors.TryGetValue(tok.Type, out string? col))
                spans.Add((s, e, col));
        }
        spans.Sort((a, b) => a.start.CompareTo(b.start));

        var sb = new System.Text.StringBuilder("  ");
        int pos = 0;
        foreach (var (s, e, col) in spans)
        {
            if (s > pos) sb.Append(text[pos..s]);
            sb.Append(col);
            sb.Append(text[s..Math.Min(e, text.Length)]);
            sb.Append(reset);
            pos = e;
        }
        if (pos < text.Length) sb.Append(text[pos..]);
        Console.WriteLine(sb.ToString());
    }
}
