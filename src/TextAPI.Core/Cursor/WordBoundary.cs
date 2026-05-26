using TextAPI.Core.Language;

namespace TextAPI.Core.Cursor;

/// <summary>
/// Standalone word-boundary helpers that work at the document level without requiring
/// a <see cref="TextCursor"/> instance.
///
/// These are the canonical implementations of all word-scanning logic in the engine.
/// <see cref="TextCursor"/> and <see cref="TextAPI.Core.Search.TextSearcher"/> both
/// delegate to this class so the predicate and algorithms stay in one place.
///
/// Algorithm summary
/// ─────────────────
///   WordLeft  : step left one char, skip non-word chars, then skip word chars.
///   WordRight : if on word char — skip word chars then skip non-word chars;
///               if on non-word char — skip non-word chars to the next word start.
///   GetWordAt : expand left and right through the same character class as the
///               char under offset (word chars, or non-word/non-newline chars).
///
/// Performance
/// ───────────
///   Public document methods read a small text window (initial 512 chars) via a
///   single <see cref="TextDocument.GetText"/> call, then scan the in-memory span.
///   The window doubles on the rare occasion that a word/group exceeds it.
///   The internal span-based overloads are used by <see cref="TextCursor"/> directly
///   when it has already materialised the relevant text.
/// </summary>
public static class WordBoundary
{
    // ── Character predicate ───────────────────────────────────────────────

    /// <summary>Returns true if <paramref name="c"/> is a word character (letter, digit, or underscore).</summary>
    public static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// Returns true if the grapheme cluster starting at <paramref name="offset"/> in
    /// <paramref name="text"/> is a word character.
    /// Correctly handles supplementary-plane code points (emoji, CJK Extension B, etc.)
    /// via <see cref="System.Text.Rune"/>.
    /// </summary>
    internal static bool IsWordCluster(ReadOnlySpan<char> text, int offset)
    {
        if ((uint)offset >= (uint)text.Length) return false;
        char c = text[offset];
        if ((uint)c < 0x80) return IsWordChar(c); // ASCII fast path

        // Decode the first code point of the cluster (handles surrogate pairs)
        if (System.Text.Rune.DecodeFromUtf16(text[offset..], out var rune, out _)
            == System.Buffers.OperationStatus.Done)
            return System.Text.Rune.IsLetterOrDigit(rune) || rune.Value == '_';

        return false;
    }

    // ── Span-based core (internal — used by TextCursor and GetWordAt) ─────
    // All scan methods step by grapheme clusters so that surrogate pairs, emoji,
    // and combining-mark sequences are treated as atomic units.

    /// <summary>
    /// Returns the start offset of the word or non-word group immediately to the left
    /// of <paramref name="offset"/> within <paramref name="text"/>.
    /// </summary>
    internal static int ScanLeft(ReadOnlySpan<char> text, int offset)
    {
        if (offset <= 0) return 0;

        // Step back one cluster to land on the character just before offset.
        offset = GraphemeHelper.PreviousCluster(text, offset);

        // Skip non-word clusters to the left.
        while (offset > 0 && !IsWordCluster(text, offset))
            offset = GraphemeHelper.PreviousCluster(text, offset);

        // Skip word clusters to the left (find the start of the word run).
        while (offset > 0)
        {
            int prev = GraphemeHelper.PreviousCluster(text, offset);
            if (!IsWordCluster(text, prev)) break;
            offset = prev;
        }

        return offset;
    }

    /// <summary>
    /// Returns the offset after the word or non-word group immediately to the right
    /// of <paramref name="offset"/> within <paramref name="text"/>.
    /// </summary>
    internal static int ScanRight(ReadOnlySpan<char> text, int offset)
    {
        int len = text.Length;
        if (offset >= len) return len;

        if (IsWordCluster(text, offset))
        {
            while (offset < len && IsWordCluster(text, offset))
                offset = GraphemeHelper.NextCluster(text, offset);  // skip word clusters
            while (offset < len && !IsWordCluster(text, offset))
                offset = GraphemeHelper.NextCluster(text, offset);  // skip trailing non-word
        }
        else
        {
            while (offset < len && !IsWordCluster(text, offset))
                offset = GraphemeHelper.NextCluster(text, offset);  // skip to next word start
        }
        return offset;
    }

    /// <summary>
    /// Returns the (start, end) of the word or adjacent non-word/non-newline group that
    /// contains <paramref name="pos"/> within <paramref name="text"/>.
    /// </summary>
    internal static (int Start, int End) ExpandAt(ReadOnlySpan<char> text, int pos)
    {
        if (text.IsEmpty) return (0, 0);

        // Snap pos to the nearest cluster start to avoid landing mid-surrogate.
        pos = GraphemeHelper.SnapToClusterStart(text, Math.Clamp(pos, 0, text.Length - 1));

        if (IsWordCluster(text, pos))
        {
            int s = pos;
            while (s > 0)
            {
                int prev = GraphemeHelper.PreviousCluster(text, s);
                if (!IsWordCluster(text, prev)) break;
                s = prev;
            }
            int e = GraphemeHelper.NextCluster(text, pos);
            while (e < text.Length && IsWordCluster(text, e))
                e = GraphemeHelper.NextCluster(text, e);
            return (s, e);
        }
        else
        {
            int s = pos;
            while (s > 0)
            {
                int prev = GraphemeHelper.PreviousCluster(text, s);
                if (IsWordCluster(text, prev) || text[prev] == '\n') break;
                s = prev;
            }
            int e = GraphemeHelper.NextCluster(text, pos);
            while (e < text.Length && !IsWordCluster(text, e) && text[e] != '\n')
                e = GraphemeHelper.NextCluster(text, e);
            return (s, e);
        }
    }

    // ── Public document-aware API ─────────────────────────────────────────

    /// <summary>
    /// Returns the offset of the start of the word or non-word group immediately to the
    /// left of <paramref name="offset"/> in <paramref name="doc"/>.
    /// Returns 0 when <paramref name="offset"/> is already at the start of the document.
    /// </summary>
    public static int GetWordBoundaryLeft(TextDocument doc, int offset)
    {
        offset = Math.Clamp(offset, 0, doc.Length);
        if (offset <= 0) return 0;

        // Read a growing window to the left.  512 chars covers almost every real word.
        // If the scan hits the window's left edge we expand — this handles pathological
        // cases like very long identifiers or comment lines.
        for (int window = 512; ; window *= 4)
        {
            int winStart = Math.Max(0, offset - window);
            int winLen   = offset - winStart;
            string text  = doc.GetText(winStart, winLen);
            int rel      = ScanLeft(text.AsSpan(), winLen);

            // rel == 0 with winStart > 0 means the scan hit the window edge — expand.
            if (rel > 0 || winStart == 0)
                return winStart + rel;
        }
    }

    /// <summary>
    /// Returns the offset after the word or non-word group immediately to the right of
    /// <paramref name="offset"/> in <paramref name="doc"/>.
    /// Returns <c>doc.Length</c> when <paramref name="offset"/> is at the end of the document.
    /// </summary>
    public static int GetWordBoundaryRight(TextDocument doc, int offset)
    {
        offset = Math.Clamp(offset, 0, doc.Length);
        if (offset >= doc.Length) return doc.Length;

        for (int window = 512; ; window *= 4)
        {
            int winEnd  = Math.Min(doc.Length, offset + window);
            int winLen  = winEnd - offset;
            string text = doc.GetText(offset, winLen);
            int rel     = ScanRight(text.AsSpan(), 0);

            // rel == winLen with winEnd < doc.Length means the scan hit the right edge.
            if (rel < winLen || winEnd == doc.Length)
                return offset + rel;
        }
    }

    /// <summary>
    /// Returns the <see cref="WordSpan"/> for the word (or adjacent non-word/non-newline group)
    /// that contains <paramref name="offset"/> in <paramref name="doc"/>.
    /// Returns <see cref="WordSpan.Empty"/> on an empty document.
    /// </summary>
    public static WordSpan GetWordAt(TextDocument doc, int offset)
    {
        if (doc.Length == 0) return WordSpan.Empty;
        offset = Math.Clamp(offset, 0, doc.Length - 1);

        // Read a window centred on offset.  ExpandAt only needs enough context to
        // reach the word/group edges on both sides, which is almost always < 512 chars.
        for (int wing = 256; ; wing *= 4)
        {
            int winStart = Math.Max(0, offset - wing);
            int winEnd   = Math.Min(doc.Length, offset + wing);
            int winLen   = winEnd - winStart;
            string text  = doc.GetText(winStart, winLen);
            int relPos   = offset - winStart;

            var (relStart, relEnd) = ExpandAt(text.AsSpan(), relPos);

            bool leftEdge  = relStart == 0    && winStart > 0;
            bool rightEdge = relEnd   == winLen && winEnd  < doc.Length;

            if (!leftEdge && !rightEdge)
            {
                int absStart = winStart + relStart;
                int absEnd   = winStart + relEnd;
                return new WordSpan(absStart, absEnd,
                    doc.GetText(absStart, absEnd - absStart));
            }
            // One or both edges of the window were hit — expand and retry.
        }
    }
}
