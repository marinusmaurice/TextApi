namespace TextAPI.Core.Encoding;

/// <summary>
/// How confident the detector is in the result.
/// </summary>
public enum EncodingConfidence
{
    /// <summary>A recognised byte-order mark was present. Encoding is certain.</summary>
    Bom,

    /// <summary>
    /// No BOM, but the byte sequence was verified as valid UTF-8 multi-byte sequences.
    /// Practically certain.
    /// </summary>
    HighConfidence,

    /// <summary>
    /// High bytes were found and we chose between Latin-1 and Windows-1252 by
    /// inspecting the 0x80–0x9F range.
    /// </summary>
    Heuristic,

    /// <summary>
    /// Nothing useful detected; UTF-8 returned as a safe default
    /// (file was pure 7-bit ASCII, or the peeked region was too short).
    /// </summary>
    Fallback
}

/// <summary>
/// The result of encoding detection performed by <see cref="EncodingDetector"/>.
/// </summary>
/// <param name="Encoding">The detected (or defaulted) encoding.</param>
/// <param name="HasBom">
/// <see langword="true"/> when the stream began with a recognised BOM.
/// </param>
/// <param name="BomLength">
/// Number of BOM bytes (0, 2, 3, or 4).  The stream is positioned immediately
/// after the BOM when <see cref="EncodingDetector.Detect(System.IO.Stream)"/>
/// returns.
/// </param>
/// <param name="Confidence">How reliable the detection result is.</param>
public sealed record DetectedEncoding(
    System.Text.Encoding Encoding,
    bool                 HasBom,
    int                  BomLength,
    EncodingConfidence   Confidence
);
