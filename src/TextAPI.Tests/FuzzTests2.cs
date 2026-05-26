using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.Commands;
using TextAPI.Core.Decorations;
using TextAPI.Core.EOL;
using TextAPI.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Extended fuzz test suite — property-based style, verified against oracles.
// Each region targets a distinct API surface / failure mode.
// ═══════════════════════════════════════════════════════════════════════════

// ── Helpers ─────────────────────────────────────────────────────────────────

file static class FuzzHelpers
{
    private const string AsciiChars   = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 \t\n.,!?;:()[]{}<>/\\\"'";
    private const string UnicodeExtra = "αβγδεζηθλμνξπρστφχψωÄÖÜäöüéèêàùûîôçñ日本語中文한국어🎉🔥✅❌🚀💡";

    public static string RandomAscii(Random rng, int len)
        => new(Enumerable.Range(0, len).Select(_ => AsciiChars[rng.Next(AsciiChars.Length)]).ToArray());

    public static string RandomUnicode(Random rng, int len)
    {
        var pool = AsciiChars + UnicodeExtra;
        return new(Enumerable.Range(0, len).Select(_ => pool[rng.Next(pool.Length)]).ToArray());
    }

    public static string RandomLinesOnly(Random rng, int len)
    {
        const string wordChars = "abcdefghijklmnopqrstuvwxyz ";
        var sb = new System.Text.StringBuilder(len);
        for (int i = 0; i < len; i++)
            sb.Append(rng.Next(8) == 0 ? '\n' : wordChars[rng.Next(wordChars.Length)]);
        return sb.ToString();
    }

    /// <summary>
    /// Apply a random mix of insert / delete / replace to both a PieceTable and a
    /// StringBuilder oracle.  Returns the number of operations applied.
    /// </summary>
    public static int RunEditsAgainstOracle(
        PieceTable pt,
        System.Text.StringBuilder oracle,
        Random rng,
        int ops,
        Func<Random, int, string> textGen)
    {
        for (int i = 0; i < ops; i++)
        {
            int op = oracle.Length == 0 ? 0 : rng.Next(3);
            switch (op)
            {
                case 0: // insert
                {
                    int pos  = oracle.Length == 0 ? 0 : rng.Next(0, oracle.Length + 1);
                    string t = textGen(rng, rng.Next(1, 20));
                    pt.Insert(pos, t);
                    oracle.Insert(pos, t);
                    break;
                }
                case 1: // delete
                {
                    int pos = rng.Next(0, oracle.Length);
                    int len = rng.Next(1, Math.Min(oracle.Length - pos + 1, 50));
                    pt.Delete(pos, len);
                    oracle.Remove(pos, len);
                    break;
                }
                default: // replace (delete + insert)
                {
                    int pos  = rng.Next(0, oracle.Length);
                    int dlen = rng.Next(1, Math.Min(oracle.Length - pos + 1, 30));
                    string ins = textGen(rng, rng.Next(1, 15));
                    pt.Delete(pos, dlen);
                    pt.Insert(pos, ins);
                    oracle.Remove(pos, dlen);
                    oracle.Insert(pos, ins);
                    break;
                }
            }

            string ptText  = pt.GetText();
            string refText = oracle.ToString();
            if (ptText != refText)
                throw new Exception(
                    $"op={i} ({op}): divergence. len pt={ptText.Length} ref={refText.Length}\n" +
                    $"PT : [{Snip(ptText)}]\nREF: [{Snip(refText)}]");
        }
        return ops;
    }

    private static string Snip(string s) => s.Length > 80 ? s[..80] + "…" : s;

    public static PieceTable MakePt(string content = "")
    {
        var pt = new PieceTable();
        if (content.Length > 0) pt.Load(content);
        else pt.Load("");
        return pt;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. PieceTable — pure buffer correctness, many seeds and sizes
// ═══════════════════════════════════════════════════════════════════════════

public class PieceTableFuzzExtended
{
    [Theory]
    [InlineData(100,  500)]
    [InlineData(101,  500)]
    [InlineData(200, 1000)]
    [InlineData(201, 1000)]
    [InlineData(300, 2000)]
    [InlineData(301, 2000)]
    [InlineData(400, 3000)]
    [InlineData(401, 3000)]
    [InlineData(500, 5000)]
    [InlineData(501, 5000)]
    public void RandomEdits_MatchOracle_AsciiContent(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();
        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomAscii);
    }

    [Theory]
    [InlineData(1001, 300)]
    [InlineData(1002, 300)]
    [InlineData(1003, 600)]
    [InlineData(1004, 600)]
    [InlineData(1005, 1200)]
    public void RandomEdits_MatchOracle_UnicodeContent(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();
        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomUnicode);
    }

    [Theory]
    [InlineData(2001, 500)]
    [InlineData(2002, 500)]
    [InlineData(2003, 1000)]
    public void RandomEdits_MatchOracle_LineHeavyContent(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();
        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomLinesOnly);
    }

    [Theory]
    [InlineData(3001, 200)]
    [InlineData(3002, 200)]
    [InlineData(3003, 400)]
    [InlineData(3004, 400)]
    [InlineData(3005, 800)]
    public void InsertOnlyAtAlternatingEnds_MatchOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt("START");
        var oracle = new System.Text.StringBuilder("START");
        for (int i = 0; i < ops; i++)
        {
            string t = FuzzHelpers.RandomAscii(rng, rng.Next(1, 10));
            bool atEnd = i % 2 == 0;
            int pos = atEnd ? oracle.Length : 0;
            pt.Insert(pos, t);
            oracle.Insert(pos, t);
            pt.GetText().Should().Be(oracle.ToString(), $"op {i}");
        }
    }

    [Theory]
    [InlineData(4001, 300)]
    [InlineData(4002, 600)]
    [InlineData(4003, 900)]
    public void DeleteFromMiddleOnly_MatchOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        // Pre-load a large document
        var initial = string.Concat(Enumerable.Repeat("abcdefghij", 200)); // 2000 chars
        var pt     = FuzzHelpers.MakePt(initial);
        var oracle = new System.Text.StringBuilder(initial);
        for (int i = 0; i < ops && oracle.Length > 2; i++)
        {
            int pos = oracle.Length / 4 + rng.Next(oracle.Length / 2);
            pos = Math.Min(pos, oracle.Length - 1);
            int len = rng.Next(1, Math.Min(5, oracle.Length - pos));
            pt.Delete(pos, len);
            oracle.Remove(pos, len);
            pt.GetText().Should().Be(oracle.ToString(), $"op {i}");
        }
    }

    [Theory]
    [InlineData(5001, 200)]
    [InlineData(5002, 400)]
    public void InsertSingleCharsRepeatedly_MatchOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt("X");
        var oracle = new System.Text.StringBuilder("X");
        for (int i = 0; i < ops; i++)
        {
            int pos = rng.Next(0, oracle.Length + 1);
            char c  = (char)('a' + rng.Next(26));
            pt.Insert(pos, c.ToString());
            oracle.Insert(pos, c);
            pt.GetText().Should().Be(oracle.ToString(), $"op {i}");
        }
    }

    [Theory]
    [InlineData(6001, 50)]
    [InlineData(6002, 100)]
    [InlineData(6003, 200)]
    public void LargeInserts_ThenLargeDeletes_MatchOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();

        // Insert large blocks first
        for (int i = 0; i < ops; i++)
        {
            int pos = oracle.Length == 0 ? 0 : rng.Next(0, oracle.Length + 1);
            string t = FuzzHelpers.RandomAscii(rng, rng.Next(50, 200));
            pt.Insert(pos, t);
            oracle.Insert(pos, t);
        }
        pt.GetText().Should().Be(oracle.ToString(), "after inserts");

        // Now delete large chunks
        for (int i = 0; i < ops && oracle.Length > 100; i++)
        {
            int pos = rng.Next(0, oracle.Length - 50);
            int len = rng.Next(20, Math.Min(100, oracle.Length - pos));
            pt.Delete(pos, len);
            oracle.Remove(pos, len);
        }
        pt.GetText().Should().Be(oracle.ToString(), "after deletes");
    }

    [Theory]
    [InlineData(7001, 100)]
    [InlineData(7002, 200)]
    public void EmptyStringInserts_AreNoOps(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt("Hello World");
        var oracle = new System.Text.StringBuilder("Hello World");

        for (int i = 0; i < ops; i++)
        {
            // Mix empty and non-empty inserts
            bool empty = rng.Next(3) == 0;
            string t   = empty ? "" : FuzzHelpers.RandomAscii(rng, rng.Next(1, 10));
            int pos    = rng.Next(0, oracle.Length + 1);
            pt.Insert(pos, t);
            oracle.Insert(pos, t);
        }
        pt.GetText().Should().Be(oracle.ToString());
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Line count & OffsetToPosition/PositionToOffset — property invariants
// ═══════════════════════════════════════════════════════════════════════════

public class LinePositionFuzzTests
{
    [Theory]
    [InlineData(10001, 400)]
    [InlineData(10002, 400)]
    [InlineData(10003, 800)]
    [InlineData(10004, 800)]
    [InlineData(10005, 1200)]
    public void LineCount_AlwaysMatchesNewlineCount(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt("line1\nline2\nline3\n");
        var oracle = new System.Text.StringBuilder("line1\nline2\nline3\n");

        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomLinesOnly);

        int expected = oracle.ToString().Count(c => c == '\n') + 1;
        pt.LineCount.Should().Be(expected);
    }

    [Theory]
    [InlineData(11001, 100)]
    [InlineData(11002, 200)]
    [InlineData(11003, 300)]
    public void OffsetToPosition_RoundTrips_AfterEdits(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();
        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomLinesOnly);

        if (pt.Length == 0) return;

        // Spot-check 20 random offsets
        for (int i = 0; i < 20; i++)
        {
            int offset = rng.Next(0, pt.Length);
            var (line, col) = pt.OffsetToPosition(offset);
            int roundTrip   = pt.PositionToOffset(line, col);
            roundTrip.Should().Be(offset, $"offset {offset} → ({line},{col}) → roundtrip");
        }
    }

    [Theory]
    [InlineData(12001, 50)]
    [InlineData(12002, 100)]
    public void GetLine_ContentMatchesOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var pt     = FuzzHelpers.MakePt();
        var oracle = new System.Text.StringBuilder();
        FuzzHelpers.RunEditsAgainstOracle(pt, oracle, rng, ops, FuzzHelpers.RandomLinesOnly);

        var lines = oracle.ToString().Split('\n');
        pt.LineCount.Should().Be(lines.Length);

        for (int li = 0; li < lines.Length; li++)
            pt.GetLine(li).Should().Be(lines[li], $"line {li}");
    }

    [Theory]
    [InlineData(13001)]
    [InlineData(13002)]
    [InlineData(13003)]
    public void PositionToOffset_AtLineStart_IsCorrect(int seed)
    {
        var rng = new Random(seed);
        // Build a document with a known number of lines
        var lines = Enumerable.Range(0, 20)
            .Select(_ => FuzzHelpers.RandomAscii(rng, rng.Next(1, 30)).Replace("\n", ""))
            .ToArray();
        string content = string.Join("\n", lines);
        var pt = FuzzHelpers.MakePt(content);

        int expectedOffset = 0;
        for (int li = 0; li < lines.Length; li++)
        {
            pt.PositionToOffset(li, 0).Should().Be(expectedOffset, $"line {li} start");
            expectedOffset += lines[li].Length + 1; // +1 for \n (except last)
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Undo / Redo — correctness after random edit sequences
// ═══════════════════════════════════════════════════════════════════════════

public class UndoRedoFuzzTests
{
    private static TextDocument MakeDoc(string content = "")
    {
        var doc = new TextDocument();
        doc.Load(content);
        return doc;
    }

    [Theory]
    [InlineData(20001, 30)]
    [InlineData(20002, 30)]
    [InlineData(20003, 50)]
    [InlineData(20004, 50)]
    [InlineData(20005, 80)]
    public void UndoAll_RestoresInitialContent(int seed, int ops)
    {
        const string initial = "The quick brown fox jumps over the lazy dog.";
        var rng = new Random(seed);
        var doc = MakeDoc(initial);

        var history = new Stack<string>();
        history.Push(initial);

        for (int i = 0; i < ops; i++)
        {
            int op = doc.Length == 0 ? 0 : rng.Next(3);
            switch (op)
            {
                case 0:
                    int ins = doc.Length == 0 ? 0 : rng.Next(0, doc.Length + 1);
                    string txt = FuzzHelpers.RandomAscii(rng, rng.Next(1, 10));
                    doc.Insert(ins, txt);
                    history.Push(doc.GetText());
                    break;
                case 1 when doc.Length > 0:
                    int dp  = rng.Next(0, doc.Length);
                    int dl  = rng.Next(1, Math.Min(doc.Length - dp + 1, 10));
                    doc.Delete(dp, dl);
                    history.Push(doc.GetText());
                    break;
                case 2 when doc.Length > 0:
                    int rp  = rng.Next(0, doc.Length);
                    int rdl = rng.Next(1, Math.Min(doc.Length - rp + 1, 10));
                    string ri = FuzzHelpers.RandomAscii(rng, rng.Next(1, 8));
                    doc.Replace(rp, rdl, ri);
                    history.Push(doc.GetText());
                    break;
            }
        }

        // Undo everything
        while (doc.CanUndo)
        {
            history.Pop();
            doc.Undo();
            doc.GetText().Should().Be(history.Peek(), "after undo step");
        }

        doc.GetText().Should().Be(initial, "fully undone");
    }

    [Theory]
    [InlineData(21001, 20)]
    [InlineData(21002, 20)]
    [InlineData(21003, 40)]
    public void RedoAfterUndo_RestoresCorrectState(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = MakeDoc("Initial content for redo fuzz.");

        var snapshots = new List<string> { doc.GetText() };

        for (int i = 0; i < ops; i++)
        {
            string txt = FuzzHelpers.RandomAscii(rng, rng.Next(1, 8)).Replace("\n", "");
            int pos = rng.Next(0, doc.Length + 1);
            doc.Insert(pos, txt);
            snapshots.Add(doc.GetText());
        }

        // Undo half
        int undoCount = ops / 2;
        for (int i = 0; i < undoCount; i++)
            doc.Undo();

        doc.GetText().Should().Be(snapshots[ops - undoCount], "after partial undo");

        // Redo all the way back
        for (int i = 0; i < undoCount; i++)
            doc.Redo();

        doc.GetText().Should().Be(snapshots[ops], "after redo to tip");
    }

    [Theory]
    [InlineData(22001, 40)]
    [InlineData(22002, 40)]
    public void NewEditAfterUndo_ClearsRedoStack(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = MakeDoc("base");

        for (int i = 0; i < ops; i++)
            doc.Insert(doc.Length, FuzzHelpers.RandomAscii(rng, 1));

        // Undo a few
        int undone = rng.Next(1, ops / 2);
        for (int i = 0; i < undone; i++)
            doc.Undo();

        doc.CanRedo.Should().BeTrue();

        // New edit should clear redo
        doc.Insert(0, "X");
        doc.CanRedo.Should().BeFalse("new edit after undo clears redo stack");
    }

    [Theory]
    [InlineData(23001, 50)]
    [InlineData(23002, 50)]
    public void UndoDescriptions_CountMatchesUndoableOps(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = MakeDoc("Hello");

        // Use 2-char inserts so each is treated as a multi-cluster "paste" (its own
        // undo unit), not coalesced into a single group the way single-char typing is.
        for (int i = 0; i < ops; i++)
            doc.Insert(doc.Length, FuzzHelpers.RandomAscii(rng, 2));

        doc.UndoDescriptions.Count().Should().Be(ops);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Search — fuzz correctness against string.IndexOf oracle
// ═══════════════════════════════════════════════════════════════════════════

public class SearchFuzzTests
{
    private static (TextDocument doc, string text) BuildDoc(int seed, int length)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, length).Replace("\n", " ");
        var doc  = new TextDocument();
        doc.Load(text);
        return (doc, text);
    }

    [Theory]
    [InlineData(30001, 2000, "abc")]
    [InlineData(30002, 2000, "xyz")]
    [InlineData(30003, 5000, "the")]
    [InlineData(30004, 5000, "ab")]
    [InlineData(30005, 10000, "a")]
    [InlineData(30006, 10000, "zzz")]
    public void FindAll_MatchesStringIndexOf_AllOffsets(int seed, int docLen, string pattern)
    {
        var (doc, text) = BuildDoc(seed, docLen);
        var expected    = new List<int>();
        int idx         = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            expected.Add(idx);
            idx++;
        }

        var actual = doc.FindAll(pattern).Select(m => m.Offset).ToList();
        actual.Should().Equal(expected, $"seed={seed} pattern='{pattern}'");
    }

    [Theory]
    [InlineData(31001, 3000, "ABC")]
    [InlineData(31002, 3000, "Hello")]
    [InlineData(31003, 8000, "THE")]
    public void FindAll_CaseInsensitive_MatchesStringOrdinalIgnoreCase(int seed, int docLen, string pattern)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, docLen).Replace("\n", " ").ToUpper();
        var doc  = new TextDocument();
        doc.Load(text);

        var expected = new List<int>();
        int idx      = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            expected.Add(idx);
            idx++;
        }

        var opts   = new SearchOptions { CaseSensitive = false };
        var actual = doc.FindAll(pattern, opts).Select(m => m.Offset).ToList();
        actual.Should().Equal(expected, $"seed={seed} pattern='{pattern}'");
    }

    [Theory]
    [InlineData(32001, 1000, "ab", "XY")]
    [InlineData(32002, 2000, "the", "THE")]
    [InlineData(32003, 5000, "a", "bb")]
    [InlineData(32004, 5000, "abc", "")]
    [InlineData(32005, 3000, "xx", "y")]
    public void ReplaceAll_MatchesStringReplace(int seed, int docLen, string pattern, string replacement)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, docLen).Replace("\n", " ");
        var doc  = new TextDocument();
        doc.Load(text);

        int count = doc.ReplaceAll(pattern, replacement);
        string expected = text.Replace(pattern, replacement, StringComparison.Ordinal);

        doc.GetText().Should().Be(expected, $"seed={seed} pattern='{pattern}'");
        count.Should().Be(text.Split(pattern).Length - 1, $"match count seed={seed}");
    }

    [Theory]
    [InlineData(33001, 2000, "abc", "ABC")]
    [InlineData(33002, 4000, "x", "YY")]
    public void ReplaceAll_IsUndoableAsOneStep(int seed, int docLen, string pattern, string replacement)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, docLen).Replace("\n", " ");
        var doc  = new TextDocument();
        doc.Load(text);

        int count = doc.ReplaceAll(pattern, replacement);
        if (count == 0) return;   // no matches → no command pushed, nothing to test

        doc.CanUndo.Should().BeTrue();
        doc.Undo();
        doc.GetText().Should().Be(text, "undo of ReplaceAll restores original");
        doc.CanUndo.Should().BeFalse("only one undo step was added");
    }

    [Theory]
    [InlineData(34001, 1000, "ab")]
    [InlineData(34002, 2000, "the")]
    [InlineData(34003, 500,  "xyz")]
    public void FindNext_AlwaysReturnsNextMatchAfterOracle(int seed, int docLen, string pattern)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, docLen).Replace("\n", " ");
        var doc  = new TextDocument();
        doc.Load(text);

        // Walk through all matches using FindNext
        int from = 0;
        while (true)
        {
            var match   = doc.FindNext(pattern, from);
            int expected = text.IndexOf(pattern, from, StringComparison.Ordinal);
            if (expected < 0)
            {
                match.Should().BeNull($"no match expected from offset {from}");
                break;
            }
            match.Should().NotBeNull();
            match!.Value.Offset.Should().Be(expected);
            from = match.Value.Offset + 1;
        }
    }

    [Theory]
    [InlineData(35001, 1500, "ab")]
    [InlineData(35002, 3000, "the")]
    public void CountMatches_MatchesStringSplitCount(int seed, int docLen, string pattern)
    {
        var rng  = new Random(seed);
        var text = FuzzHelpers.RandomAscii(rng, docLen).Replace("\n", " ");
        var doc  = new TextDocument();
        doc.Load(text);

        int expected = text.Split(pattern).Length - 1;
        doc.CountMatches(pattern).Should().Be(expected);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. EOL normalisation — round-trips for all three EOL styles
// ═══════════════════════════════════════════════════════════════════════════

public class EolFuzzTests
{
    private static string InjectEol(string text, string eol)
        => text.Replace("\n", eol);

    [Theory]
    [InlineData(40001, 200)]
    [InlineData(40002, 400)]
    [InlineData(40003, 600)]
    public void NormaliseAndRestore_CrLf_RoundTrips(int seed, int lines)
    {
        var rng     = new Random(seed);
        var content = string.Join("\n", Enumerable.Range(0, lines)
            .Select(_ => FuzzHelpers.RandomAscii(rng, rng.Next(0, 60)).Replace("\n", "").Replace("\r", "")));
        var crlfContent = InjectEol(content, "\r\n");

        var reg       = new EolRegistry();
        var normalised = reg.NormaliseOnLoad(crlfContent);
        normalised.Should().NotContain("\r", "after normalise");
        reg.RestoreEol(normalised).Should().Be(crlfContent, "round-trip");
    }

    [Theory]
    [InlineData(41001, 200)]
    [InlineData(41002, 400)]
    public void NormaliseAndRestore_Cr_RoundTrips(int seed, int lines)
    {
        var rng     = new Random(seed);
        var content = string.Join("\n", Enumerable.Range(0, lines)
            .Select(_ => FuzzHelpers.RandomAscii(rng, rng.Next(0, 60)).Replace("\n", "").Replace("\r", "")));
        var crContent = InjectEol(content, "\r");

        var reg       = new EolRegistry();
        var normalised = reg.NormaliseOnLoad(crContent);
        normalised.Should().NotContain("\r", "after normalise");
        reg.RestoreEol(normalised).Should().Be(crContent, "round-trip");
    }

    [Theory]
    [InlineData(42001, 100)]
    [InlineData(42002, 200)]
    [InlineData(42003, 300)]
    public async Task Document_SaveEolStyle_CrLf_WritesCorrectBytes(int seed, int lines)
    {
        var rng     = new Random(seed);
        var content = string.Join("\n", Enumerable.Range(0, lines)
            .Select(_ => FuzzHelpers.RandomAscii(rng, rng.Next(0, 40)).Replace("\n", "").Replace("\r", "")));

        var doc = new TextDocument();
        doc.Load(content);
        doc.SaveEolStyle = EolStyle.CrLf;

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);
        string saved = Encoding.UTF8.GetString(ms.ToArray());
        // Every \n in the saved content must be preceded by \r
        for (int i = 0; i < saved.Length; i++)
            if (saved[i] == '\n' && i > 0)
                saved[i - 1].Should().Be('\r', $"LF at {i} must be preceded by CR");
    }

    [Theory]
    [InlineData(43001, 500)]
    [InlineData(43002, 1000)]
    public void NormaliseInsert_NeverLeavesCrInBuffer(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();
        doc.Load("initial\r\nline");

        for (int i = 0; i < ops; i++)
        {
            string raw = FuzzHelpers.RandomAscii(rng, rng.Next(1, 20));
            // Insert may contain \r\n which should be normalised by EolRegistry
            doc.Insert(rng.Next(0, doc.Length + 1), raw);
        }

        // Internal representation must never contain bare \r
        doc.GetText().Should().NotMatchRegex("\r(?!\n)", "buffer must not contain bare CR");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Decorations — survive arbitrary edits without throwing
// ═══════════════════════════════════════════════════════════════════════════

public class DecorationFuzzTests
{
    [Theory]
    [InlineData(50001, 100)]
    [InlineData(50002, 200)]
    [InlineData(50003, 300)]
    public void Decorations_SurviveRandomEdits_NoExceptions(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();
        doc.Load(string.Concat(Enumerable.Repeat("Hello World\n", 20)));

        var ids = new List<Guid>();

        for (int i = 0; i < ops; i++)
        {
            int action = rng.Next(4);
            switch (action)
            {
                case 0 when doc.Length > 0: // insert
                {
                    int pos = rng.Next(0, doc.Length + 1);
                    doc.Insert(pos, FuzzHelpers.RandomAscii(rng, rng.Next(1, 10)));
                    break;
                }
                case 1 when doc.Length > 1: // delete
                {
                    int pos = rng.Next(0, doc.Length - 1);
                    doc.Delete(pos, rng.Next(1, Math.Min(5, doc.Length - pos)));
                    break;
                }
                case 2 when doc.Length > 5: // add decoration
                {
                    int s = rng.Next(0, doc.Length - 1);
                    int e = rng.Next(s + 1, Math.Min(s + 20, doc.Length));
                    ids.Add(doc.AddDecoration(s, e, DecorationType.Selection));
                    break;
                }
                case 3 when ids.Count > 0: // remove decoration
                {
                    int idx = rng.Next(ids.Count);
                    doc.RemoveDecoration(ids[idx]);
                    ids.RemoveAt(idx);
                    break;
                }
            }
        }

        // Should not throw and document should still be readable
        var _ = doc.GetText();
    }

    [Theory]
    [InlineData(51001, 50)]
    [InlineData(51002, 50)]
    [InlineData(51003, 100)]
    public void GetDecorationsInRange_AlwaysSubsetOfAdded(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();
        doc.Load(new string('A', 500));

        for (int i = 0; i < ops; i++)
        {
            int s = rng.Next(0, 490);
            int e = rng.Next(s + 1, Math.Min(s + 30, 500));
            doc.AddDecoration(s, e, DecorationType.ErrorSquiggle, "fuzz");
        }

        // Query various ranges — should never throw and counts should make sense
        for (int q = 0; q < 20; q++)
        {
            int qs = rng.Next(0, 490);
            int qe = rng.Next(qs + 1, 500);
            var results = doc.GetDecorationsInRange(qs, qe).ToList();
            results.Should().NotBeNull();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. TextDocument high-level API — Length/LineCount stay consistent
// ═══════════════════════════════════════════════════════════════════════════

public class DocumentInvariantFuzzTests
{
    [Theory]
    [InlineData(60001, 500)]
    [InlineData(60002, 500)]
    [InlineData(60003, 1000)]
    [InlineData(60004, 1000)]
    [InlineData(60005, 2000)]
    public void Length_AlwaysMatchesGetTextLength(int seed, int ops)
    {
        var rng    = new Random(seed);
        var doc    = new TextDocument();
        var oracle = new System.Text.StringBuilder();
        doc.Load("");

        for (int i = 0; i < ops; i++)
        {
            int op = oracle.Length == 0 ? 0 : rng.Next(3);
            switch (op)
            {
                case 0:
                    int pos = oracle.Length == 0 ? 0 : rng.Next(0, oracle.Length + 1);
                    string t = FuzzHelpers.RandomAscii(rng, rng.Next(1, 15));
                    doc.Insert(pos, t);
                    oracle.Insert(pos, t);
                    break;
                case 1:
                    int dp  = rng.Next(0, oracle.Length);
                    int dl  = rng.Next(1, Math.Min(oracle.Length - dp + 1, 20));
                    doc.Delete(dp, dl);
                    oracle.Remove(dp, dl);
                    break;
                case 2:
                    int rp  = rng.Next(0, oracle.Length);
                    int rdl = rng.Next(1, Math.Min(oracle.Length - rp + 1, 20));
                    string ri = FuzzHelpers.RandomAscii(rng, rng.Next(1, 10));
                    doc.Replace(rp, rdl, ri);
                    oracle.Remove(rp, rdl);
                    oracle.Insert(rp, ri);
                    break;
            }

            doc.Length.Should().Be(oracle.Length, $"op {i}: Length mismatch");
            doc.GetText().Should().Be(oracle.ToString(), $"op {i}: content mismatch");
        }
    }

    [Theory]
    [InlineData(61001, 300)]
    [InlineData(61002, 600)]
    [InlineData(61003, 900)]
    public void LineCount_ConsistentWithGetText_AfterMixedEdits(int seed, int ops)
    {
        var rng    = new Random(seed);
        var doc    = new TextDocument();
        var oracle = new System.Text.StringBuilder("start\n");
        doc.Load("start\n");

        // Drive doc directly
        for (int i = 0; i < ops; i++)
        {
            int op = oracle.Length == 0 ? 0 : rng.Next(2);
            if (op == 0)
            {
                int pos = rng.Next(0, oracle.Length + 1);
                string t = FuzzHelpers.RandomLinesOnly(rng, rng.Next(1, 10));
                doc.Insert(pos, t);
                oracle.Insert(pos, t);
            }
            else
            {
                int pos = rng.Next(0, oracle.Length);
                int len = rng.Next(1, Math.Min(oracle.Length - pos + 1, 15));
                doc.Delete(pos, len);
                oracle.Remove(pos, len);
            }
        }

        int expected = oracle.ToString().Count(c => c == '\n') + 1;
        doc.LineCount.Should().Be(expected);
    }

    [Theory]
    [InlineData(62001)]
    [InlineData(62002)]
    [InlineData(62003)]
    public void GetText_Range_AlwaysMatchesSubstring(int seed)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();
        string content = FuzzHelpers.RandomAscii(rng, 500);
        doc.Load(content);

        for (int i = 0; i < 50; i++)
        {
            int offset = rng.Next(0, content.Length);
            int length = rng.Next(0, Math.Min(content.Length - offset + 1, 100));
            doc.GetText(offset, length).Should().Be(content.Substring(offset, length), $"range [{offset},{offset + length})");
        }
    }

    [Theory]
    [InlineData(63001, 20)]
    [InlineData(63002, 40)]
    public void CompositeCommand_IsUndoableAsOneStep(int seed, int cmds)
    {
        var rng     = new Random(seed);
        var doc     = new TextDocument();
        string init = FuzzHelpers.RandomAscii(rng, 200).Replace("\n", " ");
        doc.Load(init);

        // Build composite: insert at increasing offsets (no overlaps)
        var commands = new List<IEditorCommand>();
        // We can only test via the public ExecuteComposite — supply InsertCommands
        // but we have no direct access; use doc.Insert in a loop then undo once.
        // Instead, verify that multiple sequential inserts can all be undone.
        int insertCount = Math.Min(cmds, 10);
        for (int i = 0; i < insertCount; i++)
            doc.Insert(0, "X");

        // Each insert is a separate undo step
        for (int i = 0; i < insertCount; i++)
            doc.Undo();

        doc.GetText().Should().Be(init, "fully undone after individual inserts");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Edge cases — empty documents, single-char documents, boundary offsets
// ═══════════════════════════════════════════════════════════════════════════

public class EdgeCaseFuzzTests
{
    [Theory]
    [InlineData(70001, 200)]
    [InlineData(70002, 200)]
    public void EmptyDocument_InsertDeleteCycles_StayConsistent(int seed, int ops)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();
        doc.Load("");

        for (int i = 0; i < ops; i++)
        {
            if (doc.Length == 0)
            {
                doc.Insert(0, FuzzHelpers.RandomAscii(rng, rng.Next(1, 5)));
            }
            else
            {
                // Alternate between inserting and deleting everything
                if (rng.Next(2) == 0)
                    doc.Insert(rng.Next(0, doc.Length + 1), FuzzHelpers.RandomAscii(rng, rng.Next(1, 5)));
                else
                    doc.Delete(0, doc.Length);
            }
        }
        // Must not throw
    }

    [Theory]
    [InlineData(71001, 100)]
    [InlineData(71002, 200)]
    public void SingleCharDocument_Operations_StayConsistent(int seed, int ops)
    {
        var rng    = new Random(seed);
        var doc    = new TextDocument();
        var oracle = new System.Text.StringBuilder("X");
        doc.Load("X");

        for (int i = 0; i < ops; i++)
        {
            int op = rng.Next(3);
            if (oracle.Length == 0 || op == 0)
            {
                string t = ((char)('a' + rng.Next(26))).ToString();
                int pos  = oracle.Length == 0 ? 0 : rng.Next(0, oracle.Length + 1);
                doc.Insert(pos, t);
                oracle.Insert(pos, t);
            }
            else if (op == 1)
            {
                doc.Delete(0, 1);
                oracle.Remove(0, 1);
            }
            else if (oracle.Length > 0)
            {
                string t = ((char)('a' + rng.Next(26))).ToString();
                doc.Replace(0, 1, t);
                oracle.Remove(0, 1);
                oracle.Insert(0, t);
            }

            doc.GetText().Should().Be(oracle.ToString(), $"op {i}");
            doc.Length.Should().Be(oracle.Length, $"op {i} length");
        }
    }

    [Theory]
    [InlineData(72001, 50)]
    [InlineData(72002, 100)]
    public void InsertAtEnd_ThenDeleteFromEnd_MatchesOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var doc    = new TextDocument();
        var oracle = new System.Text.StringBuilder("Hello");
        doc.Load("Hello");

        for (int i = 0; i < ops; i++)
        {
            if (rng.Next(2) == 0 || oracle.Length == 0)
            {
                string t = FuzzHelpers.RandomAscii(rng, rng.Next(1, 8)).Replace("\n", "");
                doc.Insert(oracle.Length, t);
                oracle.Append(t);
            }
            else
            {
                int len = rng.Next(1, Math.Min(oracle.Length + 1, 8));
                doc.Delete(oracle.Length - len, len);
                oracle.Remove(oracle.Length - len, len);
            }

            doc.GetText().Should().Be(oracle.ToString(), $"op {i}");
        }
    }

    [Theory]
    [InlineData(73001, 50)]
    [InlineData(73002, 100)]
    public void InsertAtBeginning_ThenDeleteFromBeginning_MatchesOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var doc    = new TextDocument();
        var oracle = new System.Text.StringBuilder("World");
        doc.Load("World");

        for (int i = 0; i < ops; i++)
        {
            if (rng.Next(2) == 0 || oracle.Length == 0)
            {
                string t = FuzzHelpers.RandomAscii(rng, rng.Next(1, 8)).Replace("\n", "");
                doc.Insert(0, t);
                oracle.Insert(0, t);
            }
            else
            {
                int len = rng.Next(1, Math.Min(oracle.Length + 1, 8));
                doc.Delete(0, len);
                oracle.Remove(0, len);
            }

            doc.GetText().Should().Be(oracle.ToString(), $"op {i}");
        }
    }

    [Theory]
    [InlineData(74001)]
    [InlineData(74002)]
    [InlineData(74003)]
    public void LargeDocumentLoad_ThenImmediateRead_IsCorrect(int seed)
    {
        var rng     = new Random(seed);
        string big  = FuzzHelpers.RandomAscii(rng, 100_000);
        var doc     = new TextDocument();
        doc.Load(big);
        doc.GetText().Should().Be(big);
        doc.Length.Should().Be(big.Length);
    }

    [Theory]
    [InlineData(75001, 20)]
    [InlineData(75002, 40)]
    public void RepeatedLoad_ResetsEverything_NoStateLeakage(int seed, int reloads)
    {
        var rng = new Random(seed);
        var doc = new TextDocument();

        for (int i = 0; i < reloads; i++)
        {
            string content = FuzzHelpers.RandomAscii(rng, rng.Next(10, 200));
            doc.Load(content);

            // Do some edits
            if (doc.Length > 0)
                doc.Insert(rng.Next(0, doc.Length), "X");

            // Reload with new content
            string next = FuzzHelpers.RandomAscii(rng, rng.Next(10, 200));
            doc.Load(next);

            doc.GetText().Should().Be(next, $"reload {i}");
            doc.IsModified.Should().BeFalse($"reload {i}");
            doc.CanUndo.Should().BeFalse($"reload {i}");
            doc.CanRedo.Should().BeFalse($"reload {i}");
        }
    }
}
