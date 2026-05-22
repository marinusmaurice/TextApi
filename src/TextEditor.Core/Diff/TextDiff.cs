namespace TextEditor.Core.Diff;

/// <summary>Options that control how <see cref="TextDiff"/> compares lines.</summary>
public sealed class DiffOptions
{
    /// <summary>Default options — ordinal, case-sensitive, whitespace-sensitive comparison.</summary>
    public static readonly DiffOptions Default = new();

    /// <summary>Ignore letter case when comparing lines.</summary>
    public bool IgnoreCase { get; init; } = false;

    /// <summary>Trim and collapse internal whitespace before comparing lines.</summary>
    public bool IgnoreWhitespace { get; init; } = false;

    /// <summary>
    /// Abort and return a coarse Delete+Insert result if the edit distance
    /// exceeds this limit.  Use <see cref="int.MaxValue"/> (the default) for
    /// no limit.
    /// </summary>
    public int MaxEditDistance { get; init; } = int.MaxValue;
}

/// <summary>
/// Line-level and character-level diff using the Myers O(ND) algorithm.
///
/// The algorithm computes the shortest edit script (SES) — the minimum
/// number of inserted and deleted lines (or characters) needed to transform
/// the old sequence into the new one.
///
/// References:
///   E.W. Myers, "An O(ND) Difference Algorithm and Its Variations",
///   Algorithmica 1(2), 1986.
/// </summary>
public static class TextDiff
{
    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Diff two arrays of lines.</summary>
    public static DiffResult Diff(
        string[] oldLines, string[] newLines, DiffOptions? options = null)
    {
        options ??= DiffOptions.Default;
        var eq      = BuildEqualityFunc(options);
        var rawOps  = ComputeMyers(oldLines, newLines, eq, options.MaxEditDistance);
        return BuildResult(rawOps, oldLines, newLines);
    }

    /// <summary>
    /// Diff two text strings by splitting them into lines.
    /// Lines are split on <c>\n</c> (CRLF is normalised first).
    /// </summary>
    public static DiffResult Diff(
        string oldText, string newText, DiffOptions? options = null)
        => Diff(SplitLines(oldText), SplitLines(newText), options);

    /// <summary>Diff two <see cref="TextDocument"/> instances.</summary>
    public static DiffResult Diff(
        TextDocument oldDoc, TextDocument newDoc, DiffOptions? options = null)
        => Diff(ExtractLines(oldDoc), ExtractLines(newDoc), options);

    /// <summary>
    /// Character-level diff of two strings.  Returns a sequence of
    /// <see cref="DiffSpan"/> values that reproduce <paramref name="newText"/>
    /// from <paramref name="oldText"/>.
    /// Useful for intra-line highlighting of changed words/characters.
    /// </summary>
    public static IReadOnlyList<DiffSpan> DiffChars(string oldText, string newText)
    {
        if (oldText == newText)
            return [new DiffSpan(DiffKind.Equal, oldText)];

        // Treat each char as a one-char "line" and run the same algorithm.
        var a   = CharLines(oldText);
        var b   = CharLines(newText);
        var ops = ComputeMyers(a, b, string.Equals, int.MaxValue);

        var result = new List<DiffSpan>(ops.Count);
        foreach (var (kind, os, oc, ns, nc) in ops)
        {
            result.Add(kind switch
            {
                DiffKind.Equal  => new DiffSpan(DiffKind.Equal,  oldText.Substring(os, oc)),
                DiffKind.Delete => new DiffSpan(DiffKind.Delete, oldText.Substring(os, oc)),
                _               => new DiffSpan(DiffKind.Insert, newText.Substring(ns, nc)),
            });
        }
        return result;
    }

    // ── Myers O(ND) algorithm ─────────────────────────────────────────────

    // Raw op: (kind, oldStart, oldCount, newStart, newCount)
    private static List<(DiffKind Kind, int OS, int OC, int NS, int NC)>
        ComputeMyers(
            string[] a, string[] b,
            Func<string, string, bool> eq,
            int maxDiff)
    {
        int n = a.Length, m = b.Length;

        if (n == 0 && m == 0) return [];
        if (n == 0)           return [(DiffKind.Insert, 0, 0, 0, m)];
        if (m == 0)           return [(DiffKind.Delete, 0, n, 0, 0)];

        int maxD   = Math.Min(n + m, maxDiff == int.MaxValue ? n + m : maxDiff);
        int offset = n + m + 1;            // centre index for diagonal array
        int size   = 2 * (n + m) + 3;
        var v      = new int[size];
        v[offset + 1] = 0;

        var trace = new List<int[]>(maxD + 1);

        for (int d = 0; d <= maxD; d++)
        {
            // Save V *before* extending diagonals at depth d.
            trace.Add((int[])v.Clone());

            for (int k = -d; k <= d; k += 2)
            {
                // Decide whether to come from diagonal k+1 (down/insert)
                // or diagonal k-1 (right/delete).
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                    x = v[offset + k + 1];       // down
                else
                    x = v[offset + k - 1] + 1;   // right

                int y = x - k;

                // Extend along the snake (diagonal of equal elements).
                while (x < n && y < m && eq(a[x], b[y]))
                { x++; y++; }

                v[offset + k] = x;

                if (x >= n && y >= m)
                    return Backtrack(trace, a, b, offset, eq);
            }
        }

        // maxDiff exceeded — return coarse Delete+Insert.
        return [(DiffKind.Delete, 0, n, 0, 0), (DiffKind.Insert, 0, 0, 0, m)];
    }

    private static List<(DiffKind, int, int, int, int)>
        Backtrack(
            List<int[]> trace, string[] a, string[] b,
            int offset, Func<string, string, bool> eq)
    {
        var ops = new List<(DiffKind, int, int, int, int)>();
        int x = a.Length, y = b.Length;

        for (int d = trace.Count - 1; d > 0; d--)
        {
            int[] vd = trace[d];
            int k = x - y;

            // Determine which diagonal we came from.
            int kPrev;
            if (k == -d || (k != d && vd[offset + k - 1] < vd[offset + k + 1]))
                kPrev = k + 1;   // came down  (insert)
            else
                kPrev = k - 1;   // came right (delete)

            int xPrev = vd[offset + kPrev];
            int yPrev = xPrev - kPrev;

            bool isInsert = kPrev == k + 1;
            int xMid = isInsert ? xPrev     : xPrev + 1;
            int yMid = isInsert ? yPrev + 1 : yPrev;

            // Snake (equal region) from (xMid, yMid) to (x, y).
            if (xMid < x)
                ops.Insert(0, (DiffKind.Equal, xMid, x - xMid, yMid, y - yMid));

            // The one edit step.
            if (isInsert)
                ops.Insert(0, (DiffKind.Insert, xPrev, 0, yPrev, 1));
            else
                ops.Insert(0, (DiffKind.Delete, xPrev, 1, yPrev, 0));

            x = xPrev;
            y = yPrev;
        }

        // Leading equal region (if any).
        if (x > 0)
            ops.Insert(0, (DiffKind.Equal, 0, x, 0, y));

        return Merge(ops);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Merge consecutive ops of the same kind so callers receive one hunk
    /// per contiguous region rather than one hunk per line.
    /// </summary>
    private static List<(DiffKind Kind, int OS, int OC, int NS, int NC)>
        Merge(List<(DiffKind Kind, int OS, int OC, int NS, int NC)> ops)
    {
        var result = new List<(DiffKind Kind, int OS, int OC, int NS, int NC)>(ops.Count);
        foreach (var op in ops)
        {
            if (result.Count > 0)
            {
                var last = result[^1];
                if (last.Kind == op.Kind)
                {
                    result[^1] = (last.Kind,
                                  last.OS, last.OC + op.OC,
                                  last.NS, last.NC + op.NC);
                    continue;
                }
            }
            result.Add(op);
        }
        return result;
    }

    private static DiffResult BuildResult(
        List<(DiffKind Kind, int OS, int OC, int NS, int NC)> ops,
        string[] oldLines, string[] newLines)
    {
        var hunks   = new List<DiffHunk>(ops.Count);
        int added   = 0, deleted = 0;

        foreach (var (kind, os, oc, ns, nc) in ops)
        {
            string[] content = kind == DiffKind.Insert
                ? newLines[ns..(ns + nc)]
                : oldLines[os..(os + oc)];

            hunks.Add(new DiffHunk(kind, os, oc, ns, nc, content));

            if      (kind == DiffKind.Insert) added   += nc;
            else if (kind == DiffKind.Delete) deleted += oc;
        }

        return new DiffResult(hunks, added, deleted);
    }

    private static Func<string, string, bool> BuildEqualityFunc(DiffOptions opts)
    {
        if (opts.IgnoreWhitespace && opts.IgnoreCase)
            return (a, b) => NormaliseWs(a).Equals(NormaliseWs(b), StringComparison.OrdinalIgnoreCase);
        if (opts.IgnoreWhitespace)
            return (a, b) => NormaliseWs(a) == NormaliseWs(b);
        if (opts.IgnoreCase)
            return (a, b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return string.Equals;
    }

    private static string NormaliseWs(string s)
    {
        var span = s.AsSpan().Trim();
        // Collapse internal runs of whitespace to a single space.
        var sb = new System.Text.StringBuilder(span.Length);
        bool inWs = false;
        foreach (char c in span)
        {
            if (char.IsWhiteSpace(c)) { if (!inWs) { sb.Append(' '); inWs = true; } }
            else                      { sb.Append(c); inWs = false; }
        }
        return sb.ToString();
    }

    private static string[] SplitLines(string text)
    {
        if (text.Length == 0) return [];
        // Normalise line endings then split.
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }

    private static string[] ExtractLines(TextDocument doc)
    {
        var lines = new string[doc.LineCount];
        for (int i = 0; i < doc.LineCount; i++) lines[i] = doc.GetLine(i);
        return lines;
    }

    // For DiffChars: treat each character as a one-character "line".
    private static string[] CharLines(string s)
    {
        var arr = new string[s.Length];
        for (int i = 0; i < s.Length; i++) arr[i] = s[i].ToString();
        return arr;
    }
}
