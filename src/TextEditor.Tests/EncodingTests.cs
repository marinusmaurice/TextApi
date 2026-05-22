using System.Text;
using TextEditor.Core;
using TextEditor.Core.Encoding;
using FluentAssertions;
using Xunit;

namespace TextEditor.Tests;

// ── A: BOM detection ──────────────────────────────────────────────────────────

public class EncodingDetectorBomTests
{
    private static DetectedEncoding DetectBytes(params byte[] bytes)
        => EncodingDetector.Detect(new MemoryStream(bytes));

    [Fact]
    public void Detect_Utf8Bom_ReturnsUtf8WithBom()
    {
        var result = DetectBytes(0xEF, 0xBB, 0xBF, (byte)'H', (byte)'i');
        result.HasBom.Should().BeTrue();
        result.BomLength.Should().Be(3);
        result.Encoding.WebName.Should().Be("utf-8");
        result.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public void Detect_Utf16LeBom_ReturnsUtf16Le()
    {
        var result = DetectBytes(0xFF, 0xFE, 0x41, 0x00); // "A" in UTF-16 LE
        result.HasBom.Should().BeTrue();
        result.BomLength.Should().Be(2);
        result.Encoding.WebName.Should().Be("utf-16");
        result.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public void Detect_Utf16BeBom_ReturnsUtf16Be()
    {
        var result = DetectBytes(0xFE, 0xFF, 0x00, 0x41); // "A" in UTF-16 BE
        result.HasBom.Should().BeTrue();
        result.BomLength.Should().Be(2);
        result.Encoding.WebName.Should().Be("utf-16BE");
        result.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public void Detect_Utf32LeBom_ReturnsUtf32Le()
    {
        var result = DetectBytes(0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00);
        result.HasBom.Should().BeTrue();
        result.BomLength.Should().Be(4);
        result.Encoding.WebName.Should().Be("utf-32");
        result.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public void Detect_Utf32BeBom_ReturnsUtf32Be()
    {
        var result = DetectBytes(0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00, 0x00, 0x41);
        result.HasBom.Should().BeTrue();
        result.BomLength.Should().Be(4);
        result.Encoding.WebName.Should().Be("utf-32BE");
        result.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public void Detect_NoBom_HasBomIsFalse()
    {
        var result = DetectBytes((byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o');
        result.HasBom.Should().BeFalse();
        result.BomLength.Should().Be(0);
    }
}

// ── B: BOM disambiguation ─────────────────────────────────────────────────────

public class EncodingDetectorAmbiguityTests
{
    [Fact]
    public void Detect_Utf32Le_NotConfusedWithUtf16Le()
    {
        // 0xFF 0xFE 0x00 0x00 must resolve to UTF-32 LE (not UTF-16 LE)
        var bytes = new byte[] { 0xFF, 0xFE, 0x00, 0x00, 0x41, 0x00, 0x00, 0x00 };
        var result = EncodingDetector.Detect(bytes);
        result.BomLength.Should().Be(4, "UTF-32 LE BOM is 4 bytes, not 2");
        result.Encoding.WebName.Should().Be("utf-32");
    }

    [Fact]
    public void Detect_TruncatedUtf32Le_FallsBackToUtf16Le()
    {
        // Only 2 bytes of a potential UTF-32 LE BOM: must NOT claim UTF-32
        var bytes = new byte[] { 0xFF, 0xFE };
        var result = EncodingDetector.Detect(bytes);
        result.Encoding.WebName.Should().Be("utf-16");
        result.BomLength.Should().Be(2);
    }
}

// ── C: Heuristic (no BOM) ─────────────────────────────────────────────────────

public class EncodingDetectorHeuristicTests
{
    private static DetectedEncoding Detect(byte[] bytes)
        => EncodingDetector.Detect(bytes.AsSpan());

    [Fact]
    public void Detect_PureAscii_ReturnsFallbackUtf8()
    {
        var bytes = "Hello World"u8.ToArray();
        var result = Detect(bytes);
        result.Encoding.WebName.Should().Be("utf-8");
        result.HasBom.Should().BeFalse();
        result.Confidence.Should().Be(EncodingConfidence.Fallback);
    }

    [Fact]
    public void Detect_ValidUtf8MultiByte_ReturnsHighConfidenceUtf8()
    {
        // U+00E9 = é = 0xC3 0xA9 in UTF-8
        byte[] bytes = [(byte)'H', 0xC3, 0xA9, (byte)'l', (byte)'l', (byte)'o'];
        var result = Detect(bytes);
        result.Encoding.WebName.Should().Be("utf-8");
        result.Confidence.Should().Be(EncodingConfidence.HighConfidence);
        result.HasBom.Should().BeFalse();
    }

    [Fact]
    public void Detect_Windows1252Bytes_ReturnsWindows1252()
    {
        // 0x80 = Euro sign in Windows-1252; undefined in Latin-1
        byte[] bytes = [(byte)'A', 0x80, (byte)'B'];
        var result = Detect(bytes);
        result.Encoding.CodePage.Should().Be(1252);
        result.Confidence.Should().Be(EncodingConfidence.Heuristic);
    }

    [Fact]
    public void Detect_Latin1Bytes_ReturnsLatin1()
    {
        // 0xE9 = é in Latin-1; in range 0xA0-0xFF — no 0x80-0x9F bytes
        // Note: 0xE9 alone is not a valid UTF-8 multi-byte start, so isValidUtf8 = false
        byte[] bytes = [(byte)'A', 0xE9, (byte)'B'];
        var result = Detect(bytes);
        // 0xE9 alone in Latin-1 range (no 0x80-0x9F) → Latin-1
        result.Confidence.Should().Be(EncodingConfidence.Heuristic);
        result.Encoding.WebName.Should().BeOneOf("iso-8859-1", "windows-1252");
        // Latin-1 if no Win-1252-range bytes
        result.Encoding.CodePage.Should().NotBe(65001); // not UTF-8
    }

    [Fact]
    public void Detect_InvalidUtf8_ReturnsHeuristic()
    {
        // 0xC0 0x80 = overlong encoding of NUL — illegal UTF-8
        byte[] bytes = [(byte)'A', 0xC0, 0x80, (byte)'B'];
        var result = Detect(bytes);
        result.Confidence.Should().Be(EncodingConfidence.Heuristic);
    }

    [Fact]
    public void Detect_EmptySpan_ReturnsFallback()
    {
        var result = EncodingDetector.Detect(ReadOnlySpan<byte>.Empty);
        result.Encoding.WebName.Should().Be("utf-8");
        result.Confidence.Should().Be(EncodingConfidence.Fallback);
    }

    [Fact]
    public void Detect_Stream_PositionedPastBom_AfterDetect()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'A', (byte)'B' };
        var ms = new MemoryStream(bytes);
        var result = EncodingDetector.Detect(ms);
        ms.Position.Should().Be(3, "stream should be positioned past the 3-byte UTF-8 BOM");
        result.BomLength.Should().Be(3);
    }

    [Fact]
    public void Detect_Stream_NoBom_PositionedAtZero()
    {
        var bytes = "Hello"u8.ToArray();
        var ms = new MemoryStream(bytes);
        var result = EncodingDetector.Detect(ms);
        ms.Position.Should().Be(0, "no BOM means stream stays at position 0");
    }
}

// ── D: Round-trip load+save preserves BOM ─────────────────────────────────────

public class EncodingRoundTripTests
{
    private static async Task<byte[]> SaveToBytes(TextDocument doc,
        System.Text.Encoding? enc = null)
    {
        var ms = new MemoryStream();
        await doc.SaveAsync(ms, enc);
        return ms.ToArray();
    }

    [Fact]
    public async Task LoadSave_Utf8WithBom_PreservesBom()
    {
        // Build a UTF-8 BOM stream
        byte[] utf8Bom = [0xEF, 0xBB, 0xBF];
        byte[] content = System.Text.Encoding.UTF8.GetBytes("Hello");
        byte[] file    = [..utf8Bom, ..content];

        var doc = new TextDocument();
        await doc.LoadFileAsync(CreateTempFile(file));

        doc.HasBom.Should().BeTrue();
        doc.DetectedEncoding!.BomLength.Should().Be(3);

        byte[] saved = await SaveToBytes(doc);
        saved[..3].Should().Equal(utf8Bom, "BOM should be preserved");
        System.Text.Encoding.UTF8.GetString(saved[3..]).Should().Be("Hello");
    }

    [Fact]
    public async Task LoadSave_Utf16Le_PreservesBom()
    {
        byte[] file = System.Text.Encoding.Unicode.GetPreamble()
            .Concat(System.Text.Encoding.Unicode.GetBytes("Hello")).ToArray();

        var doc = new TextDocument();
        await doc.LoadFileAsync(CreateTempFile(file));

        doc.HasBom.Should().BeTrue();
        doc.DetectedEncoding!.Encoding.WebName.Should().Be("utf-16");

        byte[] saved = await SaveToBytes(doc);
        saved[0].Should().Be(0xFF);
        saved[1].Should().Be(0xFE);
    }

    [Fact]
    public async Task LoadSave_Utf8NoBom_OmitsBom()
    {
        byte[] file = System.Text.Encoding.UTF8.GetBytes("Hello World");

        var doc = new TextDocument();
        await doc.LoadFileAsync(CreateTempFile(file));

        doc.HasBom.Should().BeFalse();

        byte[] saved = await SaveToBytes(doc);
        // First 3 bytes must NOT be the UTF-8 BOM
        bool startsWithBom = saved.Length >= 3 &&
            saved[0] == 0xEF && saved[1] == 0xBB && saved[2] == 0xBF;
        startsWithBom.Should().BeFalse();
    }

    [Fact]
    public async Task LoadSave_ExplicitEncodingOverride_UsesOverride()
    {
        byte[] file = System.Text.Encoding.UTF8.GetBytes("Hello");
        var doc = new TextDocument();
        await doc.LoadFileAsync(CreateTempFile(file));

        // Override with UTF-16 LE (no BOM, caller's responsibility)
        byte[] saved = await SaveToBytes(doc, System.Text.Encoding.Unicode);
        // Without BOM written by our code (override bypasses BomWriter), content
        // should decode as UTF-16 LE
        string decoded = System.Text.Encoding.Unicode.GetString(saved);
        decoded.Should().Be("Hello");
    }

    [Fact]
    public async Task SaveEncoding_Property_ForcesEncoding()
    {
        byte[] file = System.Text.Encoding.UTF8.GetBytes("Hi");
        var doc = new TextDocument();
        await doc.LoadFileAsync(CreateTempFile(file));
        doc.SaveEncoding = System.Text.Encoding.Unicode; // UTF-16 LE

        byte[] saved = await SaveToBytes(doc); // no explicit arg — uses SaveEncoding
        string decoded = System.Text.Encoding.Unicode.GetString(saved);
        decoded.Should().Be("Hi");
    }

    private static string CreateTempFile(byte[] content)
    {
        string path = Path.GetTempFileName();
        File.WriteAllBytes(path, content);
        return path;
    }
}

// ── E: TextDocument surface area ──────────────────────────────────────────────

public class TextDocumentEncodingTests
{
    [Fact]
    public async Task LoadFileAsync_ExposesDetectedEncoding()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .."Test"u8.ToArray()]);

        var doc = new TextDocument();
        await doc.LoadFileAsync(path);

        doc.DetectedEncoding.Should().NotBeNull();
        doc.DetectedEncoding!.Confidence.Should().Be(EncodingConfidence.Bom);
    }

    [Fact]
    public async Task HasBom_True_WhenFileHasBom()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB, 0xBF, .."Test"u8.ToArray()]);

        var doc = new TextDocument();
        await doc.LoadFileAsync(path);

        doc.HasBom.Should().BeTrue();
    }

    [Fact]
    public async Task HasBom_False_WhenFileHasNoBom()
    {
        string path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, "Test"u8.ToArray());

        var doc = new TextDocument();
        await doc.LoadFileAsync(path);

        doc.HasBom.Should().BeFalse();
    }

    [Fact]
    public void DetectedEncoding_Null_WhenLoadedFromString()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.DetectedEncoding.Should().BeNull();
    }

    [Fact]
    public async Task LoadFileAsync_ExplicitEncoding_OverridesDetection()
    {
        // Write a UTF-16 LE file but ask for UTF-8 decoding (demonstrates override path)
        byte[] utf8Content = "ASCII only"u8.ToArray();
        string path = Path.GetTempFileName();
        await File.WriteAllBytesAsync(path, utf8Content);

        var doc = new TextDocument();
        await doc.LoadFileAsync(path, System.Text.Encoding.UTF8);

        // Detection still ran (for BOM tracking) but decoding used the supplied encoding
        doc.DetectedEncoding.Should().NotBeNull();
        doc.GetLine(0).Should().Be("ASCII only");
    }
}

// ── F: BomWriter ──────────────────────────────────────────────────────────────

public class BomWriterTests
{
    [Fact]
    public void WriteBom_Utf8_WritesThreeBytes()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.UTF8, HasBom: true, BomLength: 3, EncodingConfidence.Bom);
        var ms = new MemoryStream();
        BomWriter.WriteBom(ms, detected);
        ms.ToArray().Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void WriteBom_Utf16Le_WritesTwoBytes()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.Unicode, HasBom: true, BomLength: 2, EncodingConfidence.Bom);
        var ms = new MemoryStream();
        BomWriter.WriteBom(ms, detected);
        ms.ToArray().Should().Equal(0xFF, 0xFE);
    }

    [Fact]
    public void WriteBom_HasBomFalse_WritesNothing()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.UTF8, HasBom: false, BomLength: 0, EncodingConfidence.Fallback);
        var ms = new MemoryStream();
        BomWriter.WriteBom(ms, detected);
        ms.Length.Should().Be(0);
    }

    [Fact]
    public void GetBomBytes_Utf8_ThreeBytes()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.UTF8, HasBom: true, BomLength: 3, EncodingConfidence.Bom);
        BomWriter.GetBomBytes(detected).ToArray().Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public void GetBomBytes_Utf16Be_TwoBytes()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.BigEndianUnicode, HasBom: true, BomLength: 2, EncodingConfidence.Bom);
        BomWriter.GetBomBytes(detected).ToArray().Should().Equal(0xFE, 0xFF);
    }

    [Fact]
    public void GetBomBytes_NoBom_Empty()
    {
        var detected = new DetectedEncoding(
            System.Text.Encoding.UTF8, HasBom: false, BomLength: 0, EncodingConfidence.Fallback);
        BomWriter.GetBomBytes(detected).IsEmpty.Should().BeTrue();
    }
}
