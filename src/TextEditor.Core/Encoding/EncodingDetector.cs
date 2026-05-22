using System.Buffers;
using SysEncoding = System.Text.Encoding;

namespace TextEditor.Core.Encoding;

/// <summary>
/// Detects the character encoding of a byte stream by inspecting BOMs and
/// applying byte-sequence heuristics when no BOM is present.
///
/// <para><b>BOM detection order</b> (checked before any heuristic):</para>
/// <list type="table">
///   <listheader><term>Bytes (hex)</term><description>Encoding</description></listheader>
///   <item><term><c>00 00 FE FF</c></term><description>UTF-32 BE</description></item>
///   <item><term><c>FF FE 00 00</c></term><description>UTF-32 LE</description></item>
///   <item><term><c>EF BB BF</c></term>  <description>UTF-8 with BOM</description></item>
///   <item><term><c>FE FF</c></term>     <description>UTF-16 BE</description></item>
///   <item><term><c>FF FE</c></term>     <description>UTF-16 LE</description></item>
/// </list>
/// UTF-32 patterns are tested before UTF-16 to avoid misidentifying
/// <c>FF FE 00 00</c> (UTF-32 LE) as <c>FF FE</c> (UTF-16 LE).
///
/// <para><b>Heuristic path (no BOM)</b>:</para>
/// <list type="number">
///   <item>All bytes 0x00–0x7F → pure ASCII → return UTF-8, <see cref="EncodingConfidence.Fallback"/>.</item>
///   <item>Byte sequence is valid UTF-8 multi-byte → return UTF-8, <see cref="EncodingConfidence.HighConfidence"/>.</item>
///   <item>Bytes in 0x80–0x9F present → return Windows-1252, <see cref="EncodingConfidence.Heuristic"/>.</item>
///   <item>Only bytes in 0xA0–0xFF → return Latin-1 (ISO-8859-1), <see cref="EncodingConfidence.Heuristic"/>.</item>
/// </list>
/// </summary>
public static class EncodingDetector
{
    // Peek at most this many bytes from the stream for heuristic analysis.
    private const int PeekSize = 4096;

    // Register code-page encodings (Windows-1252, Latin-1, etc.) so that
    // Encoding.GetEncoding(1252) works on .NET Core / .NET 5+.
    static EncodingDetector()
        => SysEncoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

    // ── BOM table ─────────────────────────────────────────────────────────

    private static readonly (byte[] Preamble, SysEncoding Encoding)[] BomTable =
    [
        // 4-byte BOMs checked BEFORE 2-byte to avoid UTF-32 LE / UTF-16 LE clash.
        (new byte[] { 0x00, 0x00, 0xFE, 0xFF }, new System.Text.UTF32Encoding(bigEndian: true,  byteOrderMark: true)),
        (new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, new System.Text.UTF32Encoding(bigEndian: false, byteOrderMark: true)),
        (new byte[] { 0xEF, 0xBB, 0xBF },       new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
        (new byte[] { 0xFE, 0xFF },              SysEncoding.BigEndianUnicode),  // UTF-16 BE
        (new byte[] { 0xFF, 0xFE },              SysEncoding.Unicode),           // UTF-16 LE
    ];

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Detect the encoding of <paramref name="stream"/>.
    /// On return the stream is positioned at <see cref="DetectedEncoding.BomLength"/>
    /// (i.e., at the first content byte, past any BOM).
    /// The stream must be seekable; for non-seekable streams use the
    /// <see cref="Detect(ReadOnlySpan{byte})"/> overload.
    /// </summary>
    public static DetectedEncoding Detect(Stream stream)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable.", nameof(stream));

        long start = stream.Position;

        int bufLen  = (int)Math.Min(PeekSize, stream.Length - start);
        byte[]? rented = null;
        Span<byte> buf = bufLen <= 512
            ? stackalloc byte[bufLen]
            : (rented = ArrayPool<byte>.Shared.Rent(bufLen)).AsSpan(0, bufLen);

        try
        {
            int read = stream.ReadAtLeast(buf, buf.Length, throwOnEndOfStream: false);
            var result = Detect(buf[..read]);
            // Position the stream past the BOM for the caller.
            stream.Seek(start + result.BomLength, SeekOrigin.Begin);
            return result;
        }
        finally
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Detect encoding from a raw byte span.
    /// Useful for unit tests and when the caller has already buffered the
    /// leading bytes of the stream.
    /// </summary>
    public static DetectedEncoding Detect(ReadOnlySpan<byte> bytes)
    {
        // ── 1. BOM scan ──────────────────────────────────────────────────
        foreach (var (preamble, encoding) in BomTable)
        {
            if (bytes.Length >= preamble.Length &&
                bytes[..preamble.Length].SequenceEqual(preamble))
            {
                return new DetectedEncoding(
                    encoding,
                    HasBom:    true,
                    BomLength: preamble.Length,
                    Confidence: EncodingConfidence.Bom);
            }
        }

        // ── 2. Heuristic (no BOM) ────────────────────────────────────────
        return DetectHeuristic(bytes);
    }

    // ── Heuristic implementation ──────────────────────────────────────────

    private static DetectedEncoding DetectHeuristic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            return Fallback();

        bool hasHighBytes    = false;
        bool hasWin1252Range = false; // bytes in 0x80–0x9F (undefined in Latin-1)
        bool isValidUtf8     = true;
        bool hasMultiByte    = false;

        int i = 0;
        while (i < bytes.Length)
        {
            byte b = bytes[i];

            if (b <= 0x7F)
            {
                // ASCII — valid in every encoding.
                i++;
                continue;
            }

            hasHighBytes = true;

            if (b >= 0x80 && b <= 0x9F)
                hasWin1252Range = true;

            // ── UTF-8 multi-byte sequence check ───────────────────────
            if (isValidUtf8)
            {
                int seqLen;
                if      ((b & 0xE0) == 0xC0) seqLen = 2;
                else if ((b & 0xF0) == 0xE0) seqLen = 3;
                else if ((b & 0xF8) == 0xF0) seqLen = 4;
                else { isValidUtf8 = false; i++; continue; }  // invalid leading byte

                if (i + seqLen > bytes.Length) { isValidUtf8 = false; break; }

                // Validate continuation bytes
                bool ok = true;
                for (int k = 1; k < seqLen; k++)
                {
                    if ((bytes[i + k] & 0xC0) != 0x80) { ok = false; break; }
                }
                if (!ok) { isValidUtf8 = false; i++; continue; }

                // Check for overlong and surrogate encodings
                if (!IsLegalUtf8Sequence(bytes.Slice(i, seqLen)))
                {
                    isValidUtf8 = false;
                    i++;
                    continue;
                }

                hasMultiByte = true;
                i += seqLen;
            }
            else
            {
                i++;
            }
        }

        if (!hasHighBytes)
        {
            // Pure 7-bit ASCII — valid UTF-8, use Fallback confidence (no multi-byte evidence).
            return Fallback();
        }

        if (isValidUtf8 && hasMultiByte)
        {
            return new DetectedEncoding(
                SysEncoding.UTF8,
                HasBom:    false,
                BomLength: 0,
                Confidence: EncodingConfidence.HighConfidence);
        }

        // High bytes exist but the sequence is not valid UTF-8 (or had no multi-byte).
        if (hasWin1252Range)
        {
            return new DetectedEncoding(
                SysEncoding.GetEncoding(1252),   // Windows-1252
                HasBom:    false,
                BomLength: 0,
                Confidence: EncodingConfidence.Heuristic);
        }

        return new DetectedEncoding(
            SysEncoding.Latin1,
            HasBom:    false,
            BomLength: 0,
            Confidence: EncodingConfidence.Heuristic);
    }

    private static DetectedEncoding Fallback() =>
        new(SysEncoding.UTF8, HasBom: false, BomLength: 0, EncodingConfidence.Fallback);

    /// <summary>
    /// Returns <see langword="false"/> for overlong encodings, surrogates, or
    /// code points above U+10FFFF.
    /// </summary>
    private static bool IsLegalUtf8Sequence(ReadOnlySpan<byte> seq)
    {
        // Decode the code point
        uint cp;
        switch (seq.Length)
        {
            case 2:
                cp = (uint)(seq[0] & 0x1F) << 6 | (uint)(seq[1] & 0x3F);
                if (cp < 0x80) return false;   // overlong
                break;
            case 3:
                cp = (uint)(seq[0] & 0x0F) << 12 | (uint)(seq[1] & 0x3F) << 6 | (uint)(seq[2] & 0x3F);
                if (cp < 0x800)               return false;   // overlong
                if (cp >= 0xD800 && cp <= 0xDFFF) return false; // surrogate
                break;
            case 4:
                cp = (uint)(seq[0] & 0x07) << 18 | (uint)(seq[1] & 0x3F) << 12
                   | (uint)(seq[2] & 0x3F) << 6  | (uint)(seq[3] & 0x3F);
                if (cp < 0x10000)  return false;   // overlong
                if (cp > 0x10FFFF) return false;   // above Unicode range
                break;
            default:
                return false;
        }
        return true;
    }
}
