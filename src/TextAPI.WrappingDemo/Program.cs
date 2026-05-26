using TextAPI.Core;
using TextAPI.Core.WordWrap;

Console.OutputEncoding = System.Text.Encoding.UTF8;

const string prose = """
    The quick brown fox jumps over the lazy dog. Pack my box with five dozen liquor jugs. How vexingly quick daft zebras jump! The five boxing wizards jump quickly. Sphinx of black quartz, judge my vow.

    CJK test: 今日は良い天気ですね。東京は大きな都市です。日本語のテキストは全角文字を使います。

    Short lines are fine.
    A
    """;

var doc   = new TextDocument();
doc.Load(prose);

// ── Scenario 1: Render at 40 columns ──────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Prose at 40-column viewport");
Console.WriteLine("══════════════════════════════════════════════\n");
RenderWrapped(doc, 40);

// ── Scenario 2: Round-trip verification ───────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: ToDisplayRow <-> ToDocumentLine round-trip");
Console.WriteLine("══════════════════════════════════════════════\n");
var model40 = doc.GetWordWrapModel(40);
bool ok = true;
for (int i = 0; i < doc.LineCount; i++)
{
    int dr   = model40.ToDisplayRow(i);
    int back = model40.ToDocumentLine(dr);
    if (back != i) { Console.WriteLine($"  FAIL: line {i} -> displayRow {dr} -> line {back}"); ok = false; }
}
Console.WriteLine(ok ? "  All round-trips passed" : "  Round-trip FAILED");
Console.WriteLine();

// ── Scenario 3: Resize ────────────────────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Resize viewport -- row count changes");
Console.WriteLine("══════════════════════════════════════════════\n");
foreach (int width in new[] { 20, 40, 60, 80, 120 })
{
    model40.Resize(width);
    Console.WriteLine($"  Width={width,3}  DisplayRowCount={model40.DisplayRowCount}  (LineCount={doc.LineCount})");
}
Console.WriteLine();

// ── Scenario 4: CJK wide characters ──────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 4: CJK wide chars (each = 2 columns)");
Console.WriteLine("══════════════════════════════════════════════\n");
var cjkDoc = new TextDocument();
cjkDoc.Load("今日は良い天気ですね"); // 10 CJK chars = 20 display columns
var cjk = cjkDoc.GetWordWrapModel(10);
Console.WriteLine($"  10 CJK chars at width=10 (each char=2 cols): {cjk.WrappedRowCount(0)} rows");
cjk.Resize(20);
Console.WriteLine($"  10 CJK chars at width=20: {cjk.WrappedRowCount(0)} rows");
Console.WriteLine();

// ── Scenario 5: GetWrappedSegments ───────────────────────────────────────
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 5: GetWrappedSegments for first prose line");
Console.WriteLine("══════════════════════════════════════════════\n");
model40.Resize(40);
string firstLine = doc.GetLine(0);
Console.WriteLine($"  Line (length={firstLine.Length}): \"{firstLine[..Math.Min(50, firstLine.Length)]}...\"");
var segs = model40.GetWrappedSegments(0);
Console.WriteLine($"  Wraps into {segs.Count} segments at width=40:");
for (int s = 0; s < segs.Count; s++)
{
    var (start, end) = segs[s];
    string seg = firstLine[start..end];
    Console.WriteLine($"    [{s}] chars {start}-{end}: \"{seg}\"");
}
Console.WriteLine();

static void RenderWrapped(TextDocument doc, int width)
{
    var model = doc.GetWordWrapModel(width);
    model.Resize(width);
    Console.WriteLine($"  {"DocLine",7}  {"DispRow",7}  Content");
    Console.WriteLine($"  {"-------",7}  {"-------",7}  {new string('-', width)}");
    for (int line = 0; line < doc.LineCount; line++)
    {
        var    segs    = model.GetWrappedSegments(line);
        string text    = doc.GetLine(line);
        int    dispRow = model.ToDisplayRow(line);
        for (int s = 0; s < segs.Count; s++)
        {
            var (start, end) = segs[s];
            string prefix = s == 0
                ? $"  {line + 1,7}  {dispRow + s,7}  "
                : $"  {"wrap",7}  {dispRow + s,7}  ";
            Console.WriteLine(prefix + text[start..end]);
        }
    }
    Console.WriteLine($"\n  Total: {doc.LineCount} doc lines -> {model.DisplayRowCount} display rows at width={width}\n");
}
