using TextEditor.Core;
using TextEditor.Core.Language;

// ── Entry point ───────────────────────────────────────────────────────────────

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: SyntaxDemo <file.cs> [--plain]");
    Console.Error.WriteLine("  --plain   disable ANSI colours (useful for piping)");
    return 1;
}

string path = args[0];
bool useColour = !args.Contains("--plain") && !Console.IsOutputRedirected;

if (!File.Exists(path))
{
    Console.Error.WriteLine($"File not found: {path}");
    return 2;
}

// ── Load ──────────────────────────────────────────────────────────────────────

var doc = new TextDocument(new CSharpTokeniser());
doc.Load(File.ReadAllText(path), path);

// ── Render ────────────────────────────────────────────────────────────────────

// Warm the entire document in one pass so state propagates correctly.
// For very large files you would only warm the visible window instead.
doc.TokeniseLines(0, doc.LineCount - 1);

int lineWidth = doc.LineCount.ToString().Length;

for (int lineIdx = 0; lineIdx < doc.LineCount; lineIdx++)
{
    string lineText = doc.GetLine(lineIdx);
    var    tokens   = doc.GetSyntaxTokens(lineIdx);

    // Print line number gutter
    if (useColour) Console.Write("\x1b[90m");   // dark grey
    Console.Write($"{(lineIdx + 1).ToString().PadLeft(lineWidth)}: ");
    if (useColour) Console.Write("\x1b[0m");

    if (tokens.Count == 0 || !useColour)
    {
        Console.WriteLine(lineText);
        continue;
    }

    // Tokens carry absolute document offsets; convert to line-relative columns.
    int lineStartOffset = doc.PositionToOffset(lineIdx, 0);

    // Walk the line, switching colour at each token boundary.
    int col = 0;
    foreach (var token in tokens)
    {
        int tokStart = token.Start - lineStartOffset;
        int tokEnd   = token.End   - lineStartOffset;

        // Gap before this token — reset colour
        if (tokStart > col)
        {
            Console.Write("\x1b[0m");
            Console.Write(lineText[col..tokStart]);
        }

        // Token span with colour
        Console.Write(AnsiColour(token.Type));
        int safeEnd = Math.Min(tokEnd, lineText.Length);
        if (tokStart < safeEnd)
            Console.Write(lineText[tokStart..safeEnd]);

        col = safeEnd;
    }

    // Remainder of line after last token
    if (col < lineText.Length)
    {
        Console.Write("\x1b[0m");
        Console.Write(lineText[col..]);
    }

    Console.WriteLine("\x1b[0m");
}

return 0;

// ── ANSI colour map ───────────────────────────────────────────────────────────

static string AnsiColour(string tokenType) => tokenType switch
{
    "keyword"    => "\x1b[94m",   // bright blue
    "type"       => "\x1b[96m",   // bright cyan
    "string"     => "\x1b[93m",   // bright yellow
    "comment"    => "\x1b[32m",   // green
    "number"     => "\x1b[95m",   // bright magenta
    "operator"   => "\x1b[37m",   // light grey
    "identifier" => "\x1b[0m",    // default (no decoration)
    _            => "\x1b[0m",
};
