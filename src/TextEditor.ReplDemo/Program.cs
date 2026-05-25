using TextEditor.Core;
using TextEditor.Core.Cursor;
using TextEditor.Repl;

// ─────────────────────────────────────────────────────────────────────────────
// TextEditor C# REPL
//
// Globals available in every script submission:
//   doc  — TextDocument     (load, edit, search, undo/redo, …)
//   mc   — MultiCursor      (move, select, insert at all cursors at once)
//   Print(x) / print(x)    — write to the output panel
//
// Built-in commands (prefix with .):
//   .help   — show this help
//   .reset  — clear session state (variables, using directives)
//   .doc    — print the current document content
//   .tour   — run the scripted feature tour
//   .exit   — quit
// ─────────────────────────────────────────────────────────────────────────────

Console.OutputEncoding = System.Text.Encoding.UTF8;

var document = new TextDocument();
var cursor   = new MultiCursor(document);
var host     = new CSharpScriptHost(document, cursor);

Banner();

// Run the tour immediately if launched without args, or skip if --no-tour passed.
if (!args.Contains("--no-tour"))
{
    await RunTourAsync(host, document, cursor);
    Console.WriteLine();
    Console.WriteLine("  Tour complete. You are now in interactive mode.");
    Console.WriteLine("  The document still has the content from the tour.");
    Console.WriteLine("  Type .help for available commands.\n");
}

// ── Interactive loop ──────────────────────────────────────────────────────────

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("> ");
    Console.ResetColor();

    string? line = Console.ReadLine();
    if (line is null) break;            // Ctrl+D / EOF

    string trimmed = line.Trim();
    if (trimmed.Length == 0) continue;

    // Built-in commands
    if (trimmed.StartsWith('.'))
    {
        switch (trimmed.ToLowerInvariant())
        {
            case ".exit":
            case ".quit":
                Console.WriteLine("Bye.");
                return;

            case ".help":
                PrintHelp();
                break;

            case ".reset":
                host.Reset();
                Ok("Session reset — variables cleared.");
                break;

            case ".doc":
                PrintDoc(document);
                break;

            case ".tour":
                host.Reset();
                document.Load("");
                await RunTourAsync(host, document, cursor);
                break;

            default:
                Warn($"Unknown command: {trimmed}");
                break;
        }
        continue;
    }

    // Collect multi-line input: if the line ends with { or the user types
    // a blank continuation line, keep reading.
    string code = await CollectInputAsync(line);
    if (string.IsNullOrWhiteSpace(code)) continue;

    var result = await host.ExecuteAsync(code);
    PrintResult(result);
}

// ─────────────────────────────────────────────────────────────────────────────
// Scripted tour
// ─────────────────────────────────────────────────────────────────────────────

static async Task RunTourAsync(CSharpScriptHost host, TextDocument doc, MultiCursor mc)
{
    host.Reset();

    var steps = new (string Title, string Code)[]
    {
        ("Load text into the document",
         """
         doc.Load("The quick brown fox\njumps over the lazy dog\nPack my box with five dozen liquor jugs");
         Print($"Lines: {doc.LineCount}  Length: {doc.Length}");
         """),

        ("Read a line and the full document",
         """
         Print("Line 0: " + doc.GetLine(0));
         Print("Line 1: " + doc.GetLine(1));
         """),

        ("Insert and delete text",
         """
         doc.Insert(4, " very");        // insert after "The"
         Print(doc.GetLine(0));         // → "The very quick brown fox"
         doc.Delete(4, 5);              // remove " very"
         Print(doc.GetLine(0));         // → "The quick brown fox"
         """),

        ("Undo / Redo",
         """
         doc.Insert(0, ">>> ");
         Print("After insert:  " + doc.GetLine(0));
         doc.Undo();
         Print("After undo:    " + doc.GetLine(0));
         doc.Redo();
         Print("After redo:    " + doc.GetLine(0));
         doc.Undo();   // clean up for next step
         """),

        ("Search: FindAll / FindNext",
         """
         var matches = doc.FindAll("o").ToList();
         Print($"Occurrences of 'o': {matches.Count}");
         Print($"First at offset {matches[0].Offset}, last at {matches[^1].Offset}");
         """),

        ("ReplaceAll — O(n) single-pass",
         """
         int n = doc.ReplaceAll("o", "0");
         Print($"Replaced {n} 'o' → '0'");
         Print(doc.GetLine(0));
         doc.Undo();   // restore
         """),

        ("Unicode grapheme cluster stats",
         """
         var stats = doc.GetStats();
         Print($"Lines:     {stats.LineCount}");
         Print($"Words:     {stats.WordCount}");
         Print($"Graphemes: {stats.GraphemeCount}");
         Print($"Code units:{stats.CodeUnitCount}");
         """),

        ("Multi-cursor insert",
         """
         // Put a cursor at the start of each line.
         mc.SetSingle(0);
         mc.AddCursor(doc.PositionToOffset(1, 0));
         mc.AddCursor(doc.PositionToOffset(2, 0));
         mc.InsertText(">> ");   // one undo step
         Print(doc.GetLine(0));
         Print(doc.GetLine(1));
         Print(doc.GetLine(2));
         doc.Undo();             // removes all three insertions at once
         """),

        ("Change tracking",
         """
         var tracker = doc.GetChangeTracker();
         tracker.SetBaseline();                    // snapshot current state

         doc.Insert(0, "CHANGED: ");               // mark line 0 modified/added
         var status = tracker.GetStatus(0);
         Print("Line 0 status: " + status);        // → Modified or Added

         doc.Undo();
         tracker.GetStatus(0).ToString();           // → Clean
         """),

        ("Cursor position history (Back / Forward)",
         """
         var history = doc.GetCursorHistory();
         history.Push(0,  "demo.cs");   // jump: top of file
         history.Push(doc.PositionToOffset(1, 0), "demo.cs");   // line 1
         history.Push(doc.PositionToOffset(2, 0), "demo.cs");   // line 2

         Print($"Current: line {doc.OffsetToPosition(history.Current!.Value.Offset).Line}");
         var back = history.Back();
         Print($"Back:    line {doc.OffsetToPosition(back!.Value.Offset).Line}");
         var fwd  = history.Forward();
         Print($"Forward: line {doc.OffsetToPosition(fwd!.Value.Offset).Line}");
         """),

        ("Syntax highlighting (C# tokeniser)",
         """
         doc.Load("int x = 42; // a comment\nstring s = \"hello\";");
         doc.SetTokeniser(new TextEditor.Core.Language.CSharpTokeniser());
         var tokens = doc.GetSyntaxTokens(0);
         foreach (var t in tokens)
             Print($"  [{t.Start,2}–{t.End,2}] {t.Type,-12} \"{doc.GetText(t.Start, t.End - t.Start)}\"");
         """),
    };

    Console.WriteLine("┌─────────────────────────────────────────────────────┐");
    Console.WriteLine("│              TextEditor API  —  feature tour         │");
    Console.WriteLine("└─────────────────────────────────────────────────────┘\n");

    for (int i = 0; i < steps.Length; i++)
    {
        var (title, code) = steps[i];

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"── Step {i + 1}/{steps.Length}: {title}");
        Console.ResetColor();

        // Print the code
        Console.ForegroundColor = ConsoleColor.DarkGray;
        foreach (var codeLine in code.Trim().Split('\n'))
            Console.WriteLine("   " + codeLine.TrimEnd());
        Console.ResetColor();

        var result = await host.ExecuteAsync(code.Trim());
        PrintResult(result, indent: "   ");
        Console.WriteLine();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Input collection — accumulates a multi-line block
// ─────────────────────────────────────────────────────────────────────────────

static async Task<string> CollectInputAsync(string firstLine)
{
    // If it doesn't look like an open block, submit immediately.
    if (!NeedsMoreInput(firstLine))
        return firstLine;

    var sb = new System.Text.StringBuilder(firstLine).AppendLine();

    while (true)
    {
        Console.ForegroundColor = ConsoleColor.DarkCyan;
        Console.Write("… ");
        Console.ResetColor();

        string? next = Console.ReadLine();
        if (next is null || next.Trim().Length == 0)
            break;              // blank line → submit

        sb.AppendLine(next);
    }

    return sb.ToString();
}

static bool NeedsMoreInput(string line)
{
    string t = line.TrimEnd();
    return t.EndsWith('{') || t.EndsWith('(');
}

// ─────────────────────────────────────────────────────────────────────────────
// Display helpers
// ─────────────────────────────────────────────────────────────────────────────

static void PrintResult(ScriptExecutionResult result, string indent = "")
{
    if (!result.Success)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        string kind = result.IsCompileError ? "compile error" : "runtime error";
        Console.WriteLine($"{indent}✗ [{kind}] {result.ErrorMessage}");
        Console.ResetColor();
        return;
    }

    string display = result.DisplayText;
    if (display.Length > 0)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        foreach (var line in display.TrimEnd().Split('\n'))
            Console.WriteLine($"{indent}{line}");
        Console.ResetColor();
    }
}

static void PrintDoc(TextDocument doc)
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine($"  [{doc.LineCount} line(s), {doc.Length} char(s)]");
    for (int i = 0; i < doc.LineCount; i++)
        Console.WriteLine($"  {i,3}: {doc.GetLine(i)}");
    Console.ResetColor();
}

static void Ok(string msg)
{
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  " + msg);
    Console.ResetColor();
}

static void Warn(string msg)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("  " + msg);
    Console.ResetColor();
}

static void Banner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("""
        ╔═══════════════════════════════════════════════════╗
        ║          TextEditor  C#  REPL                     ║
        ║  Globals:  doc (TextDocument)  mc (MultiCursor)   ║
        ║  Output:   Print(x) / print(x)                    ║
        ║  Commands: .help  .reset  .doc  .tour  .exit      ║
        ╚═══════════════════════════════════════════════════╝
        """);
    Console.ResetColor();
}

static void PrintHelp()
{
    Console.WriteLine("""

      Globals available in every script:
        doc          TextDocument  — load, edit, search, undo/redo, …
        mc           MultiCursor   — move, select, insert at multiple cursors
        Print(x)     write x to the output panel (Print / print both work)

      Built-in commands:
        .help        show this help
        .reset       clear session state (variables / types from earlier subs)
        .doc         print the current document content with line numbers
        .tour        rerun the scripted feature tour (resets session first)
        .exit        quit

      Tips:
        • Variables declared in one submission are available in the next.
        • End a line with { or ( to start a multi-line block;
          submit with a blank line.
        • Any expression whose value is non-null is printed automatically.

    """);
}
