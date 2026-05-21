namespace TextEditor.Core.Search;

public readonly struct SearchMatch
{
    public readonly int Offset;
    public readonly int Length;
    public SearchMatch(int offset, int length) { Offset = offset; Length = length; }
    public override string ToString() => $"[{Offset}..{Offset + Length})";
}

public sealed class SearchOptions
{
    public bool CaseSensitive { get; init; } = true;
    public bool WholeWord     { get; init; } = false;
    public bool UseRegex      { get; init; } = false;
    public int  MaxResults    { get; init; } = 0;   // 0 = unlimited
}

/// <summary>
/// High-performance text searcher over piece-table memory segments.
///
/// ALGORITHM:
///   Boyer-Moore-Horspool with piece-boundary bridging.
///   Streams ReadOnlyMemory[char] segments — never calls GetText().
///   Memory cost = O(pattern_length), not O(document_length).
///
/// NOTES ON C# ITERATORS + SPANS:
///   ReadOnlySpan[char] cannot be stored across a yield point (CLR restriction).
///   All span work is done in non-iterator helper methods that return List[int]
///   of match positions. The iterator yields from those lists.
///   The ReadOnlyMemory[char] from PieceSpans() is safely accessed via .Span
///   inside the non-iterator helpers.
/// </summary>
public sealed class TextSearcher
{
    private readonly Buffer.PieceTable _table;
    public TextSearcher(Buffer.PieceTable table) => _table = table;

    // ── Public API ────────────────────────────────────────────────────────

    public IEnumerable<SearchMatch> FindAll(string pattern, SearchOptions? opts = null)
    {
        opts ??= new SearchOptions();
        if (string.IsNullOrEmpty(pattern)) yield break;

        if (opts.UseRegex)
        {
            foreach (var m in RegexSearch(pattern, opts)) yield return m;
            yield break;
        }

        foreach (var m in BmhSearch(pattern, opts)) yield return m;
    }

    public SearchMatch? FindFirst(string pattern, SearchOptions? opts = null)
    {
        foreach (var m in FindAll(pattern, new SearchOptions {
            CaseSensitive = opts?.CaseSensitive ?? true,
            WholeWord     = opts?.WholeWord     ?? false,
            UseRegex      = opts?.UseRegex      ?? false,
            MaxResults    = 1 }))
            return m;
        return null;
    }

    public SearchMatch? FindNext(string pattern, int fromOffset, SearchOptions? opts = null)
    {
        foreach (var m in FindAll(pattern, opts))
            if (m.Offset >= fromOffset) return m;
        return null;
    }

    public SearchMatch? FindPrev(string pattern, int beforeOffset, SearchOptions? opts = null)
    {
        SearchMatch? last = null;
        foreach (var m in FindAll(pattern, opts))
        {
            if (m.Offset >= beforeOffset) break;
            last = m;
        }
        return last;
    }

    public int Count(string pattern, SearchOptions? opts = null)
    {
        int n = 0; foreach (var _ in FindAll(pattern, opts)) n++; return n;
    }

    // ── Boyer-Moore-Horspool piece-streaming iterator ─────────────────────

    private IEnumerable<SearchMatch> BmhSearch(string rawPattern, SearchOptions opts)
    {
        char[] pat = PreparePattern(rawPattern, opts.CaseSensitive);
        int    m   = pat.Length;

        if (m == 1)
        {
            foreach (var match in SingleCharSearch(pat[0], opts)) yield return match;
            yield break;
        }

        int[] skip     = BuildSkipTable(pat, opts.CaseSensitive);
        char[] bridge  = new char[(m - 1) * 2];
        int bridgeLen  = 0;
        int docOffset  = 0;
        int found      = 0;
        int limit      = opts.MaxResults;

        foreach (var mem in _table.PieceSpans())
        {
            // All span-touching work done in non-iterator helpers → returns List<int>
            var (seamHits, pieceHits, newBridgeLen) =
                ProcessPiece(mem, pat, skip, bridge, bridgeLen, docOffset, opts.CaseSensitive);

            int seamBase = docOffset - bridgeLen;

            foreach (int idx in seamHits)
            {
                int abs = seamBase + idx;
                if (!opts.WholeWord || IsWholeWord(abs, m))
                {
                    yield return new SearchMatch(abs, m);
                    if (limit > 0 && ++found >= limit) yield break;
                }
            }

            foreach (int idx in pieceHits)
            {
                int abs = docOffset + idx;
                if (!opts.WholeWord || IsWholeWord(abs, m))
                {
                    yield return new SearchMatch(abs, m);
                    if (limit > 0 && ++found >= limit) yield break;
                }
            }

            bridgeLen  = newBridgeLen;
            docOffset += mem.Length;
            if (limit > 0 && found >= limit) yield break;
        }
    }

    // ── ProcessPiece: all span work, no yield ─────────────────────────────

    private static (List<int> SeamHits, List<int> PieceHits, int NewBridgeLen)
        ProcessPiece(ReadOnlyMemory<char> mem, char[] pat, int[] skip,
                     char[] bridge, int bridgeLen, int docOffset, bool caseSensitive)
    {
        ReadOnlySpan<char> rawSpan = mem.Span;
        int m = pat.Length;

        // Fold case into rented buffer if needed
        char[]? foldBuf = null;
        ReadOnlySpan<char> span = rawSpan;
        if (!caseSensitive)
        {
            foldBuf = System.Buffers.ArrayPool<char>.Shared.Rent(rawSpan.Length);
            rawSpan.CopyTo(foldBuf);
            FoldUpper(foldBuf.AsSpan(0, rawSpan.Length));
            span = foldBuf.AsSpan(0, rawSpan.Length);
        }

        var seamHits  = new List<int>();
        var pieceHits = new List<int>();

        try
        {
            // ── Seam check ────────────────────────────────────────────────
            if (bridgeLen > 0 && span.Length > 0)
            {
                int seamRight = Math.Min(span.Length, m - 1);
                int seamLen   = bridgeLen + seamRight;
                char[] seam   = System.Buffers.ArrayPool<char>.Shared.Rent(seamLen);
                try
                {
                    bridge.AsSpan(0, bridgeLen).CopyTo(seam);
                    span[..seamRight].CopyTo(seam.AsSpan(bridgeLen));
                    BmhCollect(seam.AsSpan(0, seamLen), pat, skip, seamHits, maxIdx: bridgeLen - 1);
                }
                finally { System.Buffers.ArrayPool<char>.Shared.Return(seam); }
            }

            // ── Main piece search ─────────────────────────────────────────
            BmhCollect(span, pat, skip, pieceHits, maxIdx: span.Length);
        }
        finally
        {
            if (foldBuf != null) System.Buffers.ArrayPool<char>.Shared.Return(foldBuf);
        }

        // ── Update bridge ─────────────────────────────────────────────────
        int keep = m - 1;
        int newBridgeLen;
        if (span.Length >= keep)
        {
            span[^keep..].CopyTo(bridge.AsSpan(0, keep));
            newBridgeLen = keep;
        }
        else
        {
            int newLen = Math.Min(bridgeLen + span.Length, keep);
            int shift  = bridgeLen + span.Length - newLen;
            if (shift > 0)
                bridge.AsSpan(shift, bridgeLen - shift).CopyTo(bridge.AsSpan(0, bridgeLen - shift));
            int writeAt = Math.Max(0, newLen - span.Length);
            span.CopyTo(bridge.AsSpan(writeAt));
            newBridgeLen = newLen;
        }

        return (seamHits, pieceHits, newBridgeLen);
    }

    // ── BMH collect: fills a list of match indices within a span ─────────

    private static void BmhCollect(ReadOnlySpan<char> text, char[] pat, int[] skip,
                                   List<int> results, int maxIdx)
    {
        int n = text.Length, m = pat.Length;
        if (n < m) return;
        int i = 0;
        while (i <= n - m)
        {
            int j = m - 1;
            while (j >= 0 && pat[j] == text[i + j]) j--;
            if (j < 0)
            {
                if (i <= maxIdx) results.Add(i);
                i++;
            }
            else
            {
                char last = text[i + m - 1];
                i += last < skip.Length ? skip[last] : m;
            }
        }
    }

    // ── Single-char: vectorised IndexOf ──────────────────────────────────

    private IEnumerable<SearchMatch> SingleCharSearch(char target, SearchOptions opts)
    {
        int docOffset = 0, found = 0, limit = opts.MaxResults;
        // target is already folded to upper when !CaseSensitive (PreparePattern did it)
        // So we always search for the exact target char, and fold the span if needed.

        foreach (var mem in _table.PieceSpans())
        {
            var hits = CollectSingleChar(mem, target, opts.CaseSensitive);
            foreach (int pos in hits)
            {
                int abs = docOffset + pos;
                if (!opts.WholeWord || IsWholeWord(abs, 1))
                {
                    yield return new SearchMatch(abs, 1);
                    if (limit > 0 && ++found >= limit) yield break;
                }
            }
            docOffset += mem.Length;
            if (limit > 0 && found >= limit) yield break;
        }
    }

    private static List<int> CollectSingleChar(ReadOnlyMemory<char> mem, char target, bool caseSensitive)
    {
        ReadOnlySpan<char> span = mem.Span;
        var results = new List<int>(Math.Min(span.Length / 8, 1024)); // pre-size estimate

        if (caseSensitive)
        {
            // Hot path: direct vectorised IndexOf, no allocation
            int pos = 0;
            while (true)
            {
                int idx = span[pos..].IndexOf(target);
                if (idx < 0) break;
                results.Add(pos + idx);
                pos += idx + 1;
            }
            return results;
        }

        // Case-insensitive: fold into rented buffer
        char[]? buf = System.Buffers.ArrayPool<char>.Shared.Rent(span.Length);
        try
        {
            span.CopyTo(buf);
            FoldUpper(buf.AsSpan(0, span.Length));
            ReadOnlySpan<char> folded = buf.AsSpan(0, span.Length);
            char search = char.ToUpperInvariant(target);
            int pos = 0;
            while (true)
            {
                int idx = folded[pos..].IndexOf(search);
                if (idx < 0) break;
                results.Add(pos + idx);
                pos += idx + 1;
            }
        }
        finally { System.Buffers.ArrayPool<char>.Shared.Return(buf); }

        return results;
    }

    // ── Regex search ──────────────────────────────────────────────────────

    private IEnumerable<SearchMatch> RegexSearch(string pattern, SearchOptions opts)
    {
        var rxOpts = System.Text.RegularExpressions.RegexOptions.Compiled;
        if (!opts.CaseSensitive) rxOpts |= System.Text.RegularExpressions.RegexOptions.IgnoreCase;
        var rx    = new System.Text.RegularExpressions.Regex(pattern, rxOpts);
        int total = _table.Length;

        char[] buf = total > 85_000
            ? System.Buffers.ArrayPool<char>.Shared.Rent(total)
            : new char[total];
        string text;
        try   { _table.GetTextInto(0, total, buf); text = new string(buf, 0, total); }
        finally { if (total > 85_000) System.Buffers.ArrayPool<char>.Shared.Return(buf); }

        int found = 0, limit = opts.MaxResults;
        foreach (System.Text.RegularExpressions.Match m in rx.Matches(text))
        {
            if (!opts.WholeWord || IsWholeWord(m.Index, m.Length))
            {
                yield return new SearchMatch(m.Index, m.Length);
                if (limit > 0 && ++found >= limit) yield break;
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static int[] BuildSkipTable(char[] pat, bool caseSensitive)
    {
        int m    = pat.Length;
        var skip = new int[65536];
        Array.Fill(skip, m);
        for (int i = 0; i < m - 1; i++)
        {
            char c = caseSensitive ? pat[i] : char.ToUpperInvariant(pat[i]);
            skip[c] = m - 1 - i;
        }
        return skip;
    }

    private static char[] PreparePattern(string p, bool cs)
        => cs ? p.ToCharArray() : p.ToUpperInvariant().ToCharArray();

    private static void FoldUpper(Span<char> s)
    {
        for (int i = 0; i < s.Length; i++) s[i] = char.ToUpperInvariant(s[i]);
    }

    private bool IsWholeWord(int offset, int length)
    {
        if (offset > 0)
        {
            Span<char> c = stackalloc char[1];
            _table.GetTextInto(offset - 1, 1, c);
            if (char.IsLetterOrDigit(c[0]) || c[0] == '_') return false;
        }
        int end = offset + length;
        if (end < _table.Length)
        {
            Span<char> c = stackalloc char[1];
            _table.GetTextInto(end, 1, c);
            if (char.IsLetterOrDigit(c[0]) || c[0] == '_') return false;
        }
        return true;
    }
}
