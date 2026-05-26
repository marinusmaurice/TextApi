using TextAPI.Core;
using TextAPI.Core.Cursor;
using TextAPI.Core.Formatting;
using TextAPI.Core.Language;
using TextAPI.Core.Search;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Write(ConsoleColor.Cyan, "TextAPI  —  Utility API Demo\n" +
                         "════════════════════════════════\n");

var doc = new TextDocument();

// ── 1. GoTo + CursorHistory ───────────────────────────────────────────────
Section("1. GoTo + Cursor History");

doc.Load("first line\nsecond line\nthird line\nfourth line");
doc.GoTo(0, 0);
doc.GoTo(2, 0);
doc.GoTo(3, 0);

var hist = doc.GetCursorHistory();
Write(ConsoleColor.Green,
    $"  Current : line {doc.OffsetToPosition(hist.Current!.Value.Offset).Line}");
var back = hist.Back();
Write(ConsoleColor.Green,
    $"  Back    : line {doc.OffsetToPosition(back!.Value.Offset).Line}");
var fwd = hist.Forward();
Write(ConsoleColor.Green,
    $"  Forward : line {doc.OffsetToPosition(fwd!.Value.Offset).Line}");

// ── 2. Bookmarks ─────────────────────────────────────────────────────────
Section("2. Bookmarks");

doc.Load("line 0\nline 1\nline 2\nline 3\nline 4");
var bm = doc.GetBookmarkModel();
bm.Toggle(1); bm.Toggle(3);
Write(ConsoleColor.Green,
    $"  Bookmarks      : {string.Join(", ", bm.GetAll())}");
Write(ConsoleColor.Green,
    $"  Next after 1   : {bm.NextBookmark(1)}");
Write(ConsoleColor.Green,
    $"  Prev before 3  : {bm.PrevBookmark(3)}");

doc.Insert(doc.PositionToOffset(2, 0), "INSERTED\n");
Write(ConsoleColor.Green,
    $"  After insert at line 2 → bookmarks: {string.Join(", ", bm.GetAll())}");

// ── 3. Regex capture group ReplaceAll ────────────────────────────────────
Section("3. Regex Capture Group ReplaceAll");

doc.Load("John Smith\nJane Doe\nBob Jones");
int n = doc.ReplaceAll(@"(\w+) (\w+)", "$2, $1",
    new SearchOptions { UseRegex = true });
Write(ConsoleColor.Green, $"  Replaced {n} (Last, First format):");
for (int i = 0; i < doc.LineCount; i++)
    Write(ConsoleColor.Green, $"    {doc.GetLine(i)}");

// ── 4. IDocumentFormatter ────────────────────────────────────────────────
Section("4. IDocumentFormatter");

doc.Load("hello\nworld\nfoo");
doc.Format(new UpperCaseFormatter());
Write(ConsoleColor.Green,
    $"  After UpperCaseFormatter : {doc.GetLine(0)}, {doc.GetLine(1)}, {doc.GetLine(2)}");
doc.Undo();
Write(ConsoleColor.Green,
    $"  After undo               : {doc.GetLine(0)}, {doc.GetLine(1)}, {doc.GetLine(2)}");

doc.Format(new UpperCaseFormatter(), startLine: 1, endLine: 1);
Write(ConsoleColor.Green,
    $"  Range format (line 1)    : {doc.GetLine(0)}, {doc.GetLine(1)}, {doc.GetLine(2)}");

// ── 5. LineCommentToggle ─────────────────────────────────────────────────
Section("5. Line Comment Toggle");

doc.Load("if (x > 0) {\n    return x;\n}");
Write(ConsoleColor.DarkGray, "  Before:"); PrintDoc(doc);
LineCommentToggle.Toggle(doc, 0, doc.LineCount - 1);
Write(ConsoleColor.DarkGray, "  After comment:"); PrintDoc(doc);
LineCommentToggle.Toggle(doc, 0, doc.LineCount - 1);
Write(ConsoleColor.DarkGray, "  After uncomment:"); PrintDoc(doc);

// ── 6. DocumentCleanup ───────────────────────────────────────────────────
Section("6. DocumentCleanup — TrimTrailingWhitespace");

doc.Load("hello   \n  world  \nfoo");
Write(ConsoleColor.DarkGray, $"  Before : {Vis(doc.GetText())}");
int trimmed = DocumentCleanup.TrimTrailingWhitespace(doc);
Write(ConsoleColor.Green,
    $"  Trimmed {trimmed} line(s). After: {Vis(doc.GetText())}");

// ── 7. Column (box) selection ─────────────────────────────────────────────
Section("7. Column (Box) Selection");

doc.Load("aaaa\nbbbb\ncccc\ndddd");
var mc = new MultiCursor(doc);
mc.AddColumnSelection(0, 3, 0);
mc.InsertText("> ");
Write(ConsoleColor.Green, "  After '> ' inserted at column 0 on all 4 lines:");
PrintDoc(doc);

Write(ConsoleColor.Cyan, "\n  All features demonstrated.\n");

// ── Helpers ───────────────────────────────────────────────────────────────

static void Section(string title)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"\n── {title}");
    Console.ResetColor();
}

static void Write(ConsoleColor color, string msg)
{
    Console.ForegroundColor = color;
    Console.WriteLine(msg);
    Console.ResetColor();
}

static void PrintDoc(TextDocument d)
{
    Console.ForegroundColor = ConsoleColor.Green;
    for (int i = 0; i < d.LineCount; i++)
        Console.WriteLine($"    {d.GetLine(i)}");
    Console.ResetColor();
}

static string Vis(string s) =>
    s.Replace(" ", "·").Replace("\n", "↵\n").Replace("\t", "→");

// ── Formatter ─────────────────────────────────────────────────────────────

sealed class UpperCaseFormatter : IDocumentFormatter
{
    public string Format(string text) => text.ToUpperInvariant();
}
