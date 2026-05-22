using SysEncoding = System.Text.Encoding;

namespace TextEditor.Core.Encoding;

/// <summary>
/// Writes byte-order marks to a stream when saving a document whose original
/// encoding included a BOM.
/// </summary>
public static class BomWriter
{
    // Canonical BOM byte sequences keyed by WebName.
    // These are the exact bytes that should prefix the file content.
    private static readonly IReadOnlyDictionary<string, byte[]> BomByWebName =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["utf-8"]        = [0xEF, 0xBB, 0xBF],
            ["utf-16"]       = [0xFF, 0xFE],          // UTF-16 LE (WebName = "utf-16")
            ["utf-16BE"]     = [0xFE, 0xFF],
            ["utf-32"]       = [0xFF, 0xFE, 0x00, 0x00], // UTF-32 LE  (WebName = "utf-32")
            ["utf-32BE"]     = [0x00, 0x00, 0xFE, 0xFF], // UTF-32 BE  (WebName = "utf-32BE")
        };

    /// <summary>
    /// Write the BOM preamble for the encoding described by
    /// <paramref name="detected"/> to <paramref name="stream"/>.
    /// No-op when <c>detected.HasBom</c> is <see langword="false"/>.
    /// </summary>
    public static void WriteBom(Stream stream, DetectedEncoding detected)
    {
        var bom = GetBomBytes(detected);
        if (!bom.IsEmpty)
            stream.Write(bom);
    }

    /// <summary>
    /// Returns the raw BOM bytes for the encoding in <paramref name="detected"/>,
    /// or an empty span when <c>HasBom</c> is <see langword="false"/> or the
    /// encoding has no registered BOM.
    /// </summary>
    public static ReadOnlySpan<byte> GetBomBytes(DetectedEncoding detected)
    {
        if (!detected.HasBom) return ReadOnlySpan<byte>.Empty;

        string name = detected.Encoding.WebName;
        if (BomByWebName.TryGetValue(name, out byte[]? bom))
            return bom;

        // Fallback: ask the Encoding object for its own preamble
        // (handles any encoding not in our table, e.g. custom code-page encodings).
        byte[] preamble = detected.Encoding.GetPreamble();
        return preamble;
    }
}
