namespace TextEditor.Core.Cursor;

/// <summary>
/// Standalone word-boundary helpers that work at the document level without requiring
/// a <see cref="TextCursor"/> instance.
///
/// These are the canonical implementations of all word-scanning logic in the engine.
/// <see cref="TextCursor"/> and <see cref="TextEditor.Core.Search.TextSearcher"/> both
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

    // ── Span-based core (internal — used by TextCursor and GetWordAt) ─────

    /// <summary>
    /// Returns the start offset of the word or non-word group immediately to the left
    /// of <paramref name="offset"/> within <paramref name="text"/>.
    /// </summary>
    internal static int ScanLeft(ReadOnlySpan<char> text, int offset)
    {
        if (offset <= 0) return 0;
        offset--;
        while (offset > 0 && !IsWordChar(text[offset])) offset--;       // skip non-word
        while (offset > 0 && IsWordChar(text[offset - 1])) offset--;    // skip word
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
        if (IsWordChar(text[offset]))
        {
            while (offset < len && IsWordChar(text[offset]))  offset++;   // skip word
            while (offset < len && !IsWordChar(text[offset])) offset++;   // skip trailing non-word
        }
        else
        {
            while (offset < len && !IsWordChar(text[offset])) offset++;   // skip to next word start
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
        pos = Math.Clamp(pos, 0, text.Length - 1);
        if (IsWordChar(text[pos]))
        {
            int s = pos;
            while (s > 0 && IsWordChar(text[s - 1])) s--;
            int e = pos + 1;
            while (e < text.Length && IsWordChar(text[e])) e++;
            return (s, e);
        }
        else
        {
            int s = pos;
            while (s > 0 && !IsWordChar(text[s - 1]) && text[s - 1] != '\n') s--;
            int e = pos + 1;
            while (e < text.Length && !IsWordChar(text[e]) && text[e] != '\n') e++;
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
