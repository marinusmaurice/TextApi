using System.Text;
using TextAPI.Core;
using TextAPI.Core.Encoding;

// Register Windows-1252, Latin-1, etc. for use with Encoding.GetEncoding(1252)
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

bool colour = !Console.IsOutputRedirected;

void Esc(string code) { if (colour) Console.Write($"\x1b[{code}m"); }
void Reset()          { if (colour) Console.Write("\x1b[0m"); }

void PrintHeader(string title)
{
    int width = colour ? Math.Max(40, Math.Min(Console.WindowWidth - 1, 72)) : 72;
    Esc("1;97");
    Console.WriteLine($"\n{new string('━', width)}");
    Console.WriteLine($"  {title}");
    Console.WriteLine(new string('━', width));
    Reset();
}

void PrintRow(string label, string value, string code = "32")
{
    Esc("90"); Console.Write($"  {label,-28}"); Reset();
    Esc(code);  Console.Write(value); Reset();
    Console.WriteLine();
}

void PrintDetection(string label, DetectedEncoding det)
{
    string conf  = det.Confidence.ToString();
    string bom   = det.HasBom ? $"yes (BomLength={det.BomLength})" : "no";
    string encName = det.Encoding.WebName;

    Esc("90"); Console.Write($"  {label,-30}"); Reset();
    Esc(det.Confidence == EncodingConfidence.Bom ? "1;32" : "33");
    Console.Write($"{encName,-14}");
    Reset();
    Esc("36"); Console.Write($"  bom={bom,-24}"); Reset();
    Esc("90"); Console.Write($"  confidence={conf}"); Reset();
    Console.WriteLine();
}

// ── Scenario 1: BOM detection ─────────────────────────────────────────────────

PrintHeader("Scenario 1 — BOM detection on raw byte sequences");
Console.WriteLine();

(string Label, byte[] Bytes)[] bomSamples =
[
    ("UTF-8 BOM",    [0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i']),
    ("UTF-16 LE BOM",[0xFF, 0xFE, 0x41, 0x00]),
    ("UTF-16 BE BOM",[0xFE, 0xFF, 0x00, 0x41]),
    ("UTF-32 LE BOM",[0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00]),
    ("UTF-32 BE BOM",[0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41]),
    ("No BOM (UTF-8)",[0x48, 0x65, 0x6C, 0x6C, 0x6F]),        // "Hello"
];

foreach (var (label, bytes) in bomSamples)
{
    var det = EncodingDetector.Detect(bytes.AsSpan());
    PrintDetection(label, det);
}

// ── Scenario 2: Heuristic sniffing ───────────────────────────────────────────

PrintHeader("Scenario 2 — Heuristic sniffing (no BOM)");
Console.WriteLine();

(string Label, byte[] Bytes)[] heuristicSamples =
[
    ("Pure ASCII",         "Hello World"u8.ToArray()),
    ("Valid UTF-8 (é, ü)", [0x68, 0xC3, 0xA9, 0x6C, 0x6C, 0x6F, 0x20, 0xC3, 0xBC]), // "héllo ü"
    ("Windows-1252 range", [(byte)'A', 0x80, (byte)'B']),          // 0x80 = Euro sign in Win-1252
    ("Latin-1 range",      [(byte)'A', 0xE9, (byte)'B']),          // 0xE9 = é in Latin-1
    ("Invalid UTF-8",      [(byte)'A', 0xC0, 0x80, (byte)'B']),    // overlong NUL
];

foreach (var (label, bytes) in heuristicSamples)
{
    var det = EncodingDetector.Detect(bytes.AsSpan());
    PrintDetection(label, det);
}

// ── Scenario 3: Load from temp files ─────────────────────────────────────────

PrintHeader("Scenario 3 — Load from temp files");
Console.WriteLine();

var testContent = "Héllo Wörld — encoding test";

(string Label, Encoding Enc, bool AddBom)[] fileTests =
[
    ("UTF-8 with BOM",    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), true),
    ("UTF-8 no BOM",      new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false),
    ("UTF-16 LE with BOM",Encoding.Unicode, true),
    ("Windows-1252",      Encoding.GetEncoding(1252), false),
];

var tempFiles = new List<string>();
try
{
    foreach (var (label, enc, _) in fileTests)
    {
        string path = Path.GetTempFileName();
        tempFiles.Add(path);

        // Write the file using the specified encoding (preamble is included automatically
        // when the Encoding object has byteOrderMark=true).
        byte[] preamble = enc.GetPreamble();
        byte[] content  = enc.GetBytes(testContent);
        await File.WriteAllBytesAsync(path, [..preamble, ..content]);

        var doc = new TextDocument();
        await doc.LoadFileAsync(path);

        Esc("1;97"); Console.Write($"\n  {label}"); Reset(); Console.WriteLine();
        PrintRow("Path",       Path.GetFileName(path), "90");
        PrintRow("Detected",   doc.DetectedEncoding?.Encoding.WebName ?? "(null)", "32");
        PrintRow("HasBom",     doc.HasBom.ToString(), doc.HasBom ? "32" : "33");
        PrintRow("Confidence", doc.DetectedEncoding?.Confidence.ToString() ?? "(null)", "36");
        PrintRow("Line 0",     $"\"{doc.GetLine(0)}\"", "33");
    }
}
finally
{
    foreach (var f in tempFiles)
        try { File.Delete(f); } catch { }
}

// ── Scenario 4: Round-trip — load then save preserves BOM ────────────────────

PrintHeader("Scenario 4 — Round-trip: load then save preserves BOM");
Console.WriteLine();

string bom8Path = Path.GetTempFileName();
string savedPath = Path.GetTempFileName();
try
{
    var bom8Enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    await File.WriteAllBytesAsync(bom8Path,
        [..bom8Enc.GetPreamble(), ..bom8Enc.GetBytes("Round-trip test")]);

    var doc = new TextDocument();
    await doc.LoadFileAsync(bom8Path);
    await doc.SaveFileAsync(savedPath);

    byte[] savedBytes = await File.ReadAllBytesAsync(savedPath);
    bool startsWithBom = savedBytes.Length >= 3 &&
        savedBytes[0] == 0xEF && savedBytes[1] == 0xBB && savedBytes[2] == 0xBF;

    Esc(startsWithBom ? "1;32" : "1;31");
    Console.WriteLine($"  BOM preserved in saved file: {(startsWithBom ? "✓ YES" : "✗ NO")}");
    Reset();
    PrintRow("Original encoding",  doc.DetectedEncoding?.Encoding.WebName ?? "(null)");
    PrintRow("Saved file size",    $"{savedBytes.Length} bytes");
    PrintRow("First 3 bytes",      string.Join(" ", savedBytes.Take(3).Select(b => $"{b:X2}")));
}
finally
{
    try { File.Delete(bom8Path);  } catch { }
    try { File.Delete(savedPath); } catch { }
}

// ── Scenario 5: SaveEncoding override ────────────────────────────────────────

PrintHeader("Scenario 5 — SaveEncoding override");
Console.WriteLine();

string origPath   = Path.GetTempFileName();
string overridePath = Path.GetTempFileName();
try
{
    // Load a UTF-8-BOM file
    var u8bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
    await File.WriteAllBytesAsync(origPath,
        [..u8bom.GetPreamble(), ..u8bom.GetBytes("ASCII only content")]);

    var doc = new TextDocument();
    await doc.LoadFileAsync(origPath);

    PrintRow("Detected encoding", doc.DetectedEncoding?.Encoding.WebName ?? "(null)");

    // Override: save as UTF-16 LE (no BOM — override disables BOM logic)
    doc.SaveEncoding = Encoding.Unicode;
    await doc.SaveFileAsync(overridePath);

    byte[] saved = await File.ReadAllBytesAsync(overridePath);
    // The first two bytes for UTF-16 LE content (without BOM) decode as "AS" in ASCII
    // Reload with auto-detect to see what we get
    var doc2 = new TextDocument();
    await doc2.LoadFileAsync(overridePath);

    PrintRow("SaveEncoding set to", "UTF-16 LE (Encoding.Unicode)");
    PrintRow("Reloaded encoding",   doc2.DetectedEncoding?.Encoding.WebName ?? "(null)");
    PrintRow("Reloaded line 0",     $"\"{doc2.GetLine(0)}\"", "33");
}
finally
{
    try { File.Delete(origPath);    } catch { }
    try { File.Delete(overridePath);} catch { }
}

Console.WriteLine();
