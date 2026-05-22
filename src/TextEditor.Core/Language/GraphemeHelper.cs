using System.Globalization;
using System.Text;

namespace TextEditor.Core.Language;

/// <summary>
/// Unicode grapheme cluster utilities: boundary navigation, cluster counting, and display width.
///
/// Design
/// ──────
///   Offsets are always UTF-16 code-unit positions (same as everywhere else in the engine).
///   This layer only adds a cluster-boundary view on top — callers convert a raw char offset
///   to a cluster start/end, then hand the code-unit offset back to <see cref="TextDocument"/>.
///
/// Fast paths
/// ──────────
///   ASCII (U+0000–U+007F) is always a single-character, single-column grapheme cluster.
///   No combining marks exist in the ASCII range (they start at U+0300), so a byte < 0x80
///   is always both a cluster start AND a cluster end.
///
///   All other characters go through <see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/>
///   which implements the Unicode Grapheme Cluster Boundary algorithm (UAX #29), including:
///     • Surrogate pairs (supplementary-plane code points)
///     • Base + combining mark sequences (e + U+0301 → é)
///     • Regional indicator pairs (🇺🇸, 🇬🇧, …)
///     • ZWJ sequences (👨‍👩‍👧‍👦, 🧑‍💻, …)
///     • Emoji + skin-tone modifier (👋🏽, …)
///
/// East Asian Width
/// ────────────────
///   <see cref="DisplayWidth(ReadOnlySpan{char})"/> returns 1 for most characters and 2 for
///   East Asian Wide/Fullwidth (CJK, Hangul, most emoji). Combining marks return 0.
/// </summary>
public static class GraphemeHelper
{
    // ── Cluster navigation ────────────────────────────────────────────────

    /// <summary>
    /// Returns the code-unit offset of the start of the next grapheme cluster after
    /// <paramref name="offset"/> in <paramref name="text"/>.
    /// Returns <c>text.Length</c> when already at or past the end.
    /// </summary>
    public static int NextCluster(ReadOnlySpan<char> text, int offset)
    {
        if ((uint)offset >= (uint)text.Length) return text.Length;

        // ASCII fast path: U+0000–U+007F are always standalone clusters UNLESS followed
        // by a non-ASCII character that could be a combining mark.
        // Combining marks begin at U+0300, but to keep the check cheap we only apply the
        // fast path when the next character is also ASCII (or we are at the end).
        if ((uint)text[offset] < 0x80)
        {
            int next = offset + 1;
            if ((uint)next >= (uint)text.Length || (uint)text[next] < 0x80)
                return next;
            // Next char is non-ASCII: may be a combining mark (e.g. U+0301 after 'e').
            // Fall through to StringInfo for the authoritative answer.
        }

        // Unicode grapheme cluster segmentation via .NET runtime (UAX #29).
        int len = StringInfo.GetNextTextElementLength(text[offset..]);
        return offset + Math.Max(1, len); // Math.Max(1,…) guards against degenerate input
    }

    /// <summary>
    /// Returns the code-unit offset of the start of the grapheme cluster that ends at
    /// <paramref name="offset"/> (i.e., moves one cluster to the left).
    /// Returns 0 when already at or before the start.
    /// </summary>
    public static int PreviousCluster(ReadOnlySpan<char> text, int offset)
    {
        if (offset <= 0) return 0;

        // ASCII fast path: char immediately before is in U+0000–U+007F.
        // ASCII is never a combining mark or low surrogate, so it is always a cluster by itself.
        if ((uint)text[offset - 1] < 0x80) return offset - 1;

        // For non-ASCII we must scan forward from a safe boundary, because Unicode
        // cluster rules are inherently left-to-right.
        //
        // Strategy: look back far enough that the forward scan from that position will
        // correctly reassemble any cluster that contains our target offset.
        //
        // Budget: 512 code units handles:
        //   • ZWJ emoji sequences: max ~30 code units (👨‍👩‍👧‍👦 with skin tones ~25)
        //   • Zalgo text:          practical max ~300 combining marks = 300 code units
        //   • Any other combining  sequence in actual use
        int lookback = Math.Max(0, offset - 512);

        // Do not land in the middle of a surrogate pair — step back one more if needed.
        if (lookback > 0 && char.IsLowSurrogate(text[lookback]) && char.IsHighSurrogate(text[lookback - 1]))
            lookback--;

        // Forward-scan from lookback to find the last cluster starting before 'offset'.
        int pos       = lookback;
        int lastStart = lookback;
        while (pos < offset)
        {
            lastStart = pos;
            int len = StringInfo.GetNextTextElementLength(text[pos..]);
            if (len <= 0) break; // defensive: should not happen with valid UTF-16
            pos += len;
        }
        return lastStart;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="offset"/> is the start of a
    /// grapheme cluster (i.e., not in the middle of a surrogate pair or combining sequence).
    /// Offsets 0 and <c>text.Length</c> are always boundaries.
    /// </summary>
    public static bool IsClusterBoundary(ReadOnlySpan<char> text, int offset)
    {
        if (offset <= 0 || offset >= text.Length) return true;

        char cur  = text[offset];
        char prev = text[offset - 1];

        // Quick rejections:
        if (char.IsHighSurrogate(prev) && char.IsLowSurrogate(cur)) return false; // inside surrogate pair
        if ((uint)prev < 0x80 && (uint)cur < 0x80) return true;                  // both ASCII → always boundary

        // General: verify by forward scan
        int clusterStart = PreviousCluster(text, offset);
        return NextCluster(text, clusterStart) == offset;
    }

    /// <summary>
    /// If <paramref name="offset"/> is inside a grapheme cluster, snaps it backward to the
    /// cluster's start. If it already lies on a boundary the value is returned unchanged.
    /// </summary>
    public static int SnapToClusterStart(ReadOnlySpan<char> text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        if (IsClusterBoundary(text, offset)) return offset;
        return PreviousCluster(text, offset);
    }

    /// <summary>
    /// Returns the number of grapheme clusters in <paramref name="text"/>.
    /// This is the "user-perceived character count" — what VS Code shows in the status bar.
    /// </summary>
    public static int ClusterCount(ReadOnlySpan<char> text)
    {
        int count  = 0;
        int offset = 0;
        while (offset < text.Length)
        {
            offset = NextCluster(text, offset);
            count++;
        }
        return count;
    }

    // ── Display width ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the terminal/monospace-font column width of a single grapheme cluster.
    /// <list type="bullet">
    ///   <item>C0/C1 control characters → 0</item>
    ///   <item>Combining marks (Mn, Mc, Me) → 0</item>
    ///   <item>ASCII printable characters → 1</item>
    ///   <item>East Asian Wide / Fullwidth → 2</item>
    ///   <item>Most emoji (on supplementary plane) → 2</item>
    ///   <item>Everything else → 1</item>
    /// </list>
    /// Pass a single cluster (the slice returned between two consecutive <see cref="NextCluster"/> calls).
    /// </summary>
    public static int DisplayWidth(ReadOnlySpan<char> cluster)
    {
        if (cluster.IsEmpty) return 0;

        char first = cluster[0];

        // C0 controls (NUL–US) and DEL
        if (first < 0x20 || first == 0x7F) return 0;
        // Basic ASCII printable
        if ((uint)first < 0x80) return 1;
        // C1 controls
        if (first <= 0x9F) return 0;

        // Combining marks are zero-width (they stack on the preceding base character).
        var cat = CharUnicodeInfo.GetUnicodeCategory(first);
        if (cat is UnicodeCategory.NonSpacingMark or
                   UnicodeCategory.SpacingCombiningMark or
                   UnicodeCategory.EnclosingMark)
            return 0;

        // Zero-width joiner / non-joiner / etc. (Format chars that contribute no width alone)
        if (first is '\u200B' or '\u200C' or '\u200D' or '\uFEFF') return 0;

        // Surrogate pair → decode to full code point and check wide table
        if (char.IsHighSurrogate(first) && cluster.Length >= 2 && char.IsLowSurrogate(cluster[1]))
        {
            int cp = char.ConvertToUtf32(first, cluster[1]);
            return IsWideCodePoint(cp) ? 2 : 1;
        }

        // BMP wide characters
        return IsWideBmpChar(first) ? 2 : 1;
    }

    /// <summary>
    /// Returns the total display column width of <paramref name="text"/>
    /// (sum of <see cref="DisplayWidth"/> for every grapheme cluster).
    /// </summary>
    public static int TotalDisplayWidth(ReadOnlySpan<char> text)
    {
        int width  = 0;
        int offset = 0;
        while (offset < text.Length)
        {
            int next = NextCluster(text, offset);
            width   += DisplayWidth(text[offset..next]);
            offset   = next;
        }
        return width;
    }

    // ── East Asian Width tables ───────────────────────────────────────────
    // Based on Unicode Standard Annex #11 "East Asian Width" (UAX#11).
    // Categories W (Wide) and F (Fullwidth) receive width 2; everything else gets 1.

    private static bool IsWideBmpChar(char c)
    {
        int cp = c;
        return cp is
            // Hangul Jamo (Choseong)
            (>= 0x1100 and <= 0x115F) or
            // CJK bracketed pairs
            0x2329 or 0x232A or
            // CJK Radicals Supplement … CJK Symbols and Punctuation
            (>= 0x2E80 and <= 0x303E) or
            // Hiragana, Katakana, Bopomofo, Hangul Compatibility, Kanbun, Bopomofo Extended,
            // CJK Unified Extensions, CJK Compatibility
            (>= 0x3040 and <= 0x33FF) or
            // CJK Unified Ideographs Extension A
            (>= 0x3400 and <= 0x4DBF) or
            // CJK Unified Ideographs
            (>= 0x4E00 and <= 0x9FFF) or
            // Yi Syllables and Radicals
            (>= 0xA000 and <= 0xA4CF) or
            // Hangul Jamo Extended-A
            (>= 0xA960 and <= 0xA97C) or
            // Hangul Syllables
            (>= 0xAC00 and <= 0xD7AF) or
            // Hangul Jamo Extended-B
            (>= 0xD7B0 and <= 0xD7FB) or
            // CJK Compatibility Ideographs
            (>= 0xF900 and <= 0xFAFF) or
            // Vertical Forms
            (>= 0xFE10 and <= 0xFE19) or
            // CJK Compatibility Forms
            (>= 0xFE30 and <= 0xFE4F) or
            // Small Form Variants
            (>= 0xFE50 and <= 0xFE6F) or
            // Fullwidth Forms (excl. Halfwidth Katakana which is width 1)
            (>= 0xFF01 and <= 0xFF60) or
            // Fullwidth currency/signs
            (>= 0xFFE0 and <= 0xFFE6);
    }

    private static bool IsWideCodePoint(int cp)
    {
        return cp is
            // Mahjong Tiles, Domino Tiles, Playing Cards
            (>= 0x1F004 and <= 0x1F0CF) or
            // Enclosed Alphanumeric Supplement, Enclosed Ideographic Supplement
            (>= 0x1F100 and <= 0x1F2FF) or
            // Miscellaneous Symbols and Pictographs, Emoticons (Emoji), Transport & Map,
            // Geometric Shapes Extended, Supplemental Arrows-C, Supplemental Symbols
            (>= 0x1F300 and <= 0x1F9FF) or
            // Symbols and Pictographs Extended-A
            (>= 0x1FA00 and <= 0x1FAFF) or
            // CJK Unified Ideographs Extension B–G
            (>= 0x20000 and <= 0x2FFFD) or
            // CJK Compatibility Supplement
            (>= 0x30000 and <= 0x3FFFD);
    }
}
