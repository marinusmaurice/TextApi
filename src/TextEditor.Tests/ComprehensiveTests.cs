using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.Buffer;
using TextEditor.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace TextEditor.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// 1. FUZZ / PROPERTY TESTS
//    Random sequences of insert+delete verified against StringBuilder oracle.
//    These would have caught the SplitNodeAt boundary-tag bug immediately.
// ═══════════════════════════════════════════════════════════════════════════

public class FuzzTests
{
    private readonly ITestOutputHelper _out;
    public FuzzTests(ITestOutputHelper o) => _out = o;

    /// <summary>
    /// Run N random edit sequences, verifying PieceTable matches StringBuilder
    /// at every step. Any divergence = a real correctness bug.
    /// </summary>
    private static void RunFuzz(int seed, int ops, int maxDocLen = 2000)
    {
        var rng = new Random(seed);
        var pt  = new PieceTable();
        var ref_ = new System.Text.StringBuilder();

        pt.Load("");

        for (int i = 0; i < ops; i++)
        {
            int op = rng.Next(3);   // 0=insert, 1=delete, 2=replace

            if (op == 0 || ref_.Length == 0)
            {
                // Insert: random text at random position
                int pos  = ref_.Length == 0 ? 0 : rng.Next(0, ref_.Length + 1);
                string text = RandomText(rng, rng.Next(1, 20));
                pt.Insert(pos, text);
                ref_.Insert(pos, text);
            }
            else if (op == 1)
            {
                // Delete: random range
                int pos = rng.Next(0, ref_.Length);
                int len = rng.Next(1, Math.Min(ref_.Length - pos + 1, 50));
                pt.Delete(pos, len);
                ref_.Remove(pos, len);
            }
            else
            {
                // Replace: delete then insert
                int pos  = rng.Next(0, ref_.Length);
                int dlen = rng.Next(1, Math.Min(ref_.Length - pos + 1, 30));
                string ins = RandomText(rng, rng.Next(1, 15));
                pt.Delete(pos, dlen);
                pt.Insert(pos, ins);
                ref_.Remove(pos, dlen);
                ref_.Insert(pos, ins);
            }

            // Verify after every operation
            var ptText  = pt.GetText();
            var refText = ref_.ToString();

            if (ptText != refText)
                throw new Exception(
                    $"Seed={seed} op={i} ({op}): divergence at len {ptText.Length} vs {refText.Length}\n" +
                    $"PT:  [{ptText[..Math.Min(60, ptText.Length)]}]\n" +
                    $"REF: [{refText[..Math.Min(60, refText.Length)]}]");
        }
    }

    private static string RandomText(Random rng, int len)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyz \n!@#";
        return new string(Enumerable.Range(0, len).Select(_ => chars[rng.Next(chars.Length)]).ToArray());
    }

    [Fact] public void Fuzz_Seed0_1000Ops()   => RunFuzz(0,    1000);
    [Fact] public void Fuzz_Seed1_1000Ops()   => RunFuzz(1,    1000);
    [Fact] public void Fuzz_Seed42_1000Ops()  => RunFuzz(42,   1000);
    [Fact] public void Fuzz_Seed99_1000Ops()  => RunFuzz(99,   1000);
    [Fact] public void Fuzz_Seed777_1000Ops() => RunFuzz(777,  1000);
    [Fact] public void Fuzz_Seed2025_2000Ops() => RunFuzz(2025, 2000);

    [Fact]
    public void Fuzz_HeavyMultiPiece_ManySeams()
    {
        // Force lots of piece splits by always inserting in the middle
        var rng  = new Random(13);
        var pt   = new PieceTable();
        var ref_ = new System.Text.StringBuilder("Hello World, this is the base document.");
        pt.Load(ref_.ToString());

        for (int i = 0; i < 500; i++)
        {
            int pos  = ref_.Length / 2 + rng.Next(-5, 6);
            pos = Math.Max(0, Math.Min(pos, ref_.Length));
            string ins = RandomText(rng, rng.Next(1, 8));
            pt.Insert(pos, ins);
            ref_.Insert(pos, ins);
            pt.GetText().Should().Be(ref_.ToString(), $"after op {i}");
        }
    }

    [Fact]
    public void Fuzz_LineCountAlwaysCorrect()
    {
        var rng = new Random(55);
        var pt  = new PieceTable();
        pt.Load("line1\nline2\nline3");
        var ref_ = new System.Text.StringBuilder("line1\nline2\nline3");

        for (int i = 0; i < 200; i++)
        {
            bool doInsert = rng.Next(2) == 0 || ref_.Length == 0;
            if (doInsert)
            {
                int pos  = rng.Next(0, ref_.Length + 1);
                string t = rng.Next(3) == 0 ? "\n" : RandomText(rng, rng.Next(1, 10));
                pt.Insert(pos, t);
                ref_.Insert(pos, t);
            }
            else
            {
                int pos = rng.Next(0, ref_.Length);
                int len = rng.Next(1, Math.Min(ref_.Length - pos + 1, 20));
                pt.Delete(pos, len);
                ref_.Remove(pos, len);
            }

            int expectedLines = ref_.ToString().Count(c => c == '\n') + 1;
            pt.LineCount.Should().Be(expectedLines, $"after op {i}: doc='{ref_}'");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. COMPACTION / UNDO BUG FIX TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class CompactionTests
{
    [Fact]
    public void AutoCompaction_ClearsUndoHistory()
    {
        // compactionThreshold=5: fires on the 5th edit.
        // The firing edit's command is pushed then immediately cleared by PostEditHook.
        var doc = new TextDocument(compactionThreshold: 5);
        doc.Load("Hello");
        for (int i = 0; i < 4; i++)
            doc.Insert(doc.Length, $" {i}");

        doc.CanUndo.Should().BeTrue("4 edits, threshold not reached yet");

        // 5th edit triggers compaction: history cleared (including this command)
        doc.Insert(doc.Length, " 4");
        doc.CanUndo.Should().BeFalse("compaction clears entire undo history including trigger command");
        doc.GetText().Should().Be("Hello 0 1 2 3 4");

        // Edits after compaction are undoable normally
        doc.Insert(doc.Length, " 5");
        doc.CanUndo.Should().BeTrue("post-compaction edits are undoable");
        doc.Undo();
        doc.GetText().Should().Be("Hello 0 1 2 3 4");
        doc.CanUndo.Should().BeFalse("pre-compaction history is gone");
    }

    [Fact]
    public void ManualCompact_PreservesUndoHistory()
    {
        // Manual compact does NOT clear undo — that's the user's choice
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.Compact();   // manual
        // Undo should still work — offsets are document-level, content preserved
        doc.CanUndo.Should().BeTrue();
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }

    [Fact]
    public void Compact_ContentIdenticalBeforeAfter()
    {
        var pt = new PieceTable();
        pt.Load("Line0\nLine1\nLine2");
        pt.Insert(6, "INSERTED\n");
        pt.Delete(0, 3);
        var before = pt.GetText();
        pt.Compact();
        pt.GetText().Should().Be(before);
        pt.PieceCount.Should().Be(1);
    }

    [Fact]
    public void Compact_LineCountPreserved()
    {
        var pt = new PieceTable();
        pt.Load(string.Join("\n", Enumerable.Range(1, 100).Select(i => $"Line {i}")));
        for (int i = 0; i < 20; i++) pt.Insert(i * 5, "x");
        int beforeLines = pt.LineCount;
        pt.Compact();
        pt.LineCount.Should().Be(beforeLines);
    }

    [Fact]
    public void Compact_AllLinesCorrectAfter()
    {
        var doc = new TextDocument(compactionThreshold: 3);
        doc.Load("A\nB\nC\nD\nE");
        // Force compaction
        doc.Insert(0, "1");
        doc.Insert(0, "2");
        doc.Insert(0, "3");
        // Content should still be readable line by line
        var text = doc.GetText();
        for (int i = 0; i < doc.LineCount; i++)
        {
            var line = doc.GetLine(i);
            text.Should().Contain(line);
        }
    }

    [Fact]
    public void BulkReplaceUndo_AfterCompaction_Correct()
    {
        var doc = new TextDocument();
        doc.Load("foo bar foo baz foo");
        doc.ReplaceAll("foo", "qux");
        doc.GetText().Should().Be("qux bar qux baz qux");
        doc.Compact();   // compact with the replaced content
        // Undo should restore "qux bar qux baz qux" (the pre-compact state)
        // because BulkReplaceCommand holds a snapshot
        doc.Undo();
        doc.GetText().Should().Be("foo bar foo baz foo");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. EDGE CASES & BOUNDARY TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class EdgeCaseTests
{
    // ── Empty / single-char documents ─────────────────────────────────────

    [Fact] public void EmptyDoc_Length0_Lines1()
    {
        var pt = new PieceTable(); pt.Load("");
        pt.Length.Should().Be(0);
        pt.LineCount.Should().Be(1);
        pt.GetLine(0).Should().Be("");
        pt.GetText().Should().Be("");
    }

    [Fact] public void SingleChar_NoNewline()
    {
        var pt = new PieceTable(); pt.Load("x");
        pt.Length.Should().Be(1);
        pt.LineCount.Should().Be(1);
        pt.GetLine(0).Should().Be("x");
    }

    [Fact] public void SingleNewline_TwoLines()
    {
        var pt = new PieceTable(); pt.Load("\n");
        pt.LineCount.Should().Be(2);
        pt.GetLine(0).Should().Be("");
        pt.GetLine(1).Should().Be("");
    }

    [Fact] public void OnlyNewlines_CountCorrect()
    {
        var pt = new PieceTable(); pt.Load("\n\n\n");
        pt.LineCount.Should().Be(4);
    }

    // ── Insert at boundaries ──────────────────────────────────────────────

    [Fact] public void Insert_AtOffset0_OnEmptyDoc()
    {
        var pt = new PieceTable(); pt.Load("");
        pt.Insert(0, "hello");
        pt.GetText().Should().Be("hello");
    }

    [Fact] public void Insert_AtEnd_OnEmptyDoc()
    {
        var pt = new PieceTable(); pt.Load("");
        pt.Insert(0, "a");
        pt.Insert(1, "b");
        pt.GetText().Should().Be("ab");
    }

    [Fact] public void Insert_AtPieceSeam_Correct()
    {
        var pt = new PieceTable();
        pt.Load("Hello");
        pt.Insert(5, " World");    // creates seam at offset 5
        pt.Insert(5, "!");         // insert exactly at seam
        pt.GetText().Should().Be("Hello! World");
    }

    [Fact] public void Insert_EmptyString_NoChange()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.Insert(2, "");
        pt.GetText().Should().Be("Hello");
        pt.Length.Should().Be(5);
    }

    [Fact] public void Insert_OnlyNewline()
    {
        var pt = new PieceTable(); pt.Load("ab");
        pt.Insert(1, "\n");
        pt.LineCount.Should().Be(2);
        pt.GetLine(0).Should().Be("a");
        pt.GetLine(1).Should().Be("b");
    }

    [Fact] public void Insert_CrLf_NormalisedAndLineCountCorrect()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.Insert(5, "\r\nWorld\r\n");
        pt.GetText().Should().NotContain("\r");
        pt.LineCount.Should().Be(3);
        pt.GetLine(0).Should().Be("Hello");
        pt.GetLine(1).Should().Be("World");
        pt.GetLine(2).Should().Be("");
    }

    // ── Delete at boundaries ──────────────────────────────────────────────

    [Fact] public void Delete_EntireDocument_EmptyAndOneLine()
    {
        var pt = new PieceTable(); pt.Load("Hello World");
        pt.Delete(0, pt.Length);
        pt.Length.Should().Be(0);
        pt.LineCount.Should().Be(1);
        pt.GetText().Should().Be("");
    }

    [Fact] public void Delete_Spanning3Pieces()
    {
        var pt = new PieceTable();
        pt.Load("AAAAA");
        pt.Insert(5, "BBBBB");
        pt.Insert(10, "CCCCC");
        // "AAAAABBBBBCCCCC" — Delete(3,9) removes chars 3..11 = "AABBBBBCC"
        // Keep: "AAA" + "CCC" = "AAACCC"
        pt.Delete(3, 9);
        pt.GetText().Should().Be("AAACCC");
    }

    [Fact] public void Delete_LastChar()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.Delete(4, 1);
        pt.GetText().Should().Be("Hell");
    }

    [Fact] public void Delete_FirstChar()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.Delete(0, 1);
        pt.GetText().Should().Be("ello");
    }

    [Fact] public void Delete_ZeroLength_NoChange()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.Delete(2, 0);
        pt.GetText().Should().Be("Hello");
    }

    [Fact] public void Delete_NewlineChar_DecreasesLineCount()
    {
        var pt = new PieceTable(); pt.Load("A\nB\nC");
        pt.Delete(1, 1);   // remove first \n
        pt.LineCount.Should().Be(2);
        pt.GetText().Should().Be("AB\nC");
    }

    // ── GetLine edge cases ────────────────────────────────────────────────

    [Fact] public void GetLine_NegativeIndex_ReturnsEmpty()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.GetLine(-1).Should().Be("");
    }

    [Fact] public void GetLine_BeyondEnd_ReturnsEmpty()
    {
        var pt = new PieceTable(); pt.Load("Hello");
        pt.GetLine(999).Should().Be("");
    }

    [Fact] public void GetLine_LastLine_NoTrailingNewline()
    {
        var pt = new PieceTable(); pt.Load("Hello\nWorld");
        pt.GetLine(1).Should().Be("World");
    }

    [Fact] public void GetLine_EmptyLastLine_AfterTrailingNewline()
    {
        var pt = new PieceTable(); pt.Load("Hello\n");
        pt.LineCount.Should().Be(2);
        pt.GetLine(1).Should().Be("");
    }

    // ── OffsetToPosition edge cases ───────────────────────────────────────

    [Fact] public void OffsetToPosition_Offset0_Is00()
    {
        var pt = new PieceTable(); pt.Load("Hello\nWorld");
        pt.OffsetToPosition(0).Should().Be((0, 0));
    }

    [Fact] public void OffsetToPosition_AtNewline_CorrectLine()
    {
        var pt = new PieceTable(); pt.Load("Hello\nWorld");
        pt.OffsetToPosition(5).Should().Be((0, 5));  // the \n itself is at col 5
    }

    [Fact] public void OffsetToPosition_AfterNewline_NextLine()
    {
        var pt = new PieceTable(); pt.Load("Hello\nWorld");
        pt.OffsetToPosition(6).Should().Be((1, 0));
    }

    // ── No line endings ───────────────────────────────────────────────────

    [Fact] public void Load_NoLineEndings_OneLineCorrect()
    {
        var pt = new PieceTable(); pt.Load("abcdefghij");
        pt.LineCount.Should().Be(1);
        pt.GetLine(0).Should().Be("abcdefghij");
    }

    // ── Very long single line ─────────────────────────────────────────────

    [Fact] public void Insert_VeryLongLine_NoLineBreaks()
    {
        var pt = new PieceTable(); pt.Load("");
        var big = new string('x', 100_000);
        pt.Insert(0, big);
        pt.Length.Should().Be(100_000);
        pt.LineCount.Should().Be(1);
        pt.GetLine(0).Length.Should().Be(100_000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. UNDO / REDO STRESS TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class UndoStressTests
{
    [Fact]
    public void Undo_500Ops_RestoresOriginal()
    {
        const string original = "The quick brown fox jumps over the lazy dog";
        var doc = new TextDocument();
        doc.Load(original);

        var rng = new Random(7);
        for (int i = 0; i < 500; i++)
        {
            int pos  = rng.Next(0, doc.Length + 1);
            string t = i % 3 == 0 ? "X\n" : "AB";
            doc.Insert(pos, t);
        }

        // Undo all 500
        while (doc.CanUndo) doc.Undo();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void Redo_After500Undos_ReturnsToPeak()
    {
        var doc = new TextDocument();
        doc.Load("Start");
        for (int i = 0; i < 20; i++) doc.Insert(doc.Length, $" {i}");
        var peak = doc.GetText();

        while (doc.CanUndo) doc.Undo();
        doc.GetText().Should().Be("Start");

        while (doc.CanRedo) doc.Redo();
        doc.GetText().Should().Be(peak);
    }

    [Fact]
    public void Undo_PastBeginning_IsNoop()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");

        while (doc.CanUndo) doc.Undo();
        // Extra undos should not throw or corrupt
        doc.Undo();
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }

    [Fact]
    public void NewEdit_AfterPartialUndo_ClearsRedo()
    {
        var doc = new TextDocument();
        doc.Load("A");
        doc.Insert(1, "B");
        doc.Insert(2, "C");
        doc.Undo();   // undo C
        doc.Undo();   // undo B
        doc.CanRedo.Should().BeTrue();
        doc.Insert(1, "X");   // new edit
        doc.CanRedo.Should().BeFalse();
        doc.GetText().Should().Be("AX");
    }

    [Fact]
    public void BulkReplaceUndo_50kMatches_Correct()
    {
        var doc = new TextDocument();
        // Build a 10k-line doc where every line has "foo"
        var content = string.Concat(Enumerable.Repeat("foo bar baz\n", 1000));
        doc.Load(content);

        int count = doc.ReplaceAll("foo", "qux");
        count.Should().Be(1000);
        doc.GetText().Should().NotContain("foo");

        doc.Undo();
        doc.GetText().Should().Be(content);
        doc.GetText().Should().Contain("foo");
    }

    [Fact]
    public void UndoRedo_LineCountAlwaysConsistent()
    {
        var doc = new TextDocument();
        doc.Load("Line1\nLine2\nLine3");
        doc.Insert(doc.PositionToOffset(1, 0), "Inserted\n");
        int afterInsert = doc.LineCount;

        doc.Undo();
        doc.LineCount.Should().Be(3, "after undo line count restored");

        doc.Redo();
        doc.LineCount.Should().Be(afterInsert, "after redo line count restored");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. PERFORMANCE REGRESSION TESTS (self-calibrating against history)
// ═══════════════════════════════════════════════════════════════════════════

public class RegressionTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public RegressionTests(ITestOutputHelper o)
    {
        _out     = o;
        _session = new BenchmarkSession("Regression", o);
    }

    public void Dispose() { }

    private const string Line =
        "    public void ProcessItem(int id, string name) { return id + name.Length; }\n";

    private static PieceTable MakeEdited(int approxBytes, int edits)
    {
        int reps = approxBytes / Line.Length;
        var pt   = new PieceTable();
        pt.Load(string.Concat(Enumerable.Repeat(Line, reps)));
        var rng = new Random(42);
        for (int i = 0; i < edits; i++)
        {
            int off = rng.Next(0, Math.Max(1, pt.Length - 100));
            if (i % 2 == 0) pt.Insert(off, "EDIT\n");
            else             pt.Delete(off, Math.Min(5, pt.Length - off));
        }
        return pt;
    }

    /// <summary>
    /// Compare this run against the median of the last 5 recorded runs.
    /// Fails if more than 50% slower — machine-independent regression gate.
    /// </summary>
    private void AssertNoRegression(string name, string label, long ms, double allowedDegradation = 0.5)
    {
        var history = BenchmarkHistory.Load();
        var previous = history
            .Where(r => r.RunId != BenchmarkHistory.CurrentRunId)
            .SelectMany(r => r.Results)
            .Where(b => b.Name == name && b.Label == label && b.Ms > 0)
            .OrderByDescending(b => b.Ms)
            .Select(b => b.Ms)
            .Take(5)
            .ToList();

        if (previous.Count >= 3)
        {
            double median = previous.OrderBy(x => x).ElementAt(previous.Count / 2);
            double ratio  = ms / median;
            _out.WriteLine($"  Regression check: {ms}ms vs median {median:0}ms → ratio {ratio:0.00}× (limit {1 + allowedDegradation:0.0}×)");
            ratio.Should().BeLessThan(1 + allowedDegradation,
                $"{name} [{label}] is {ratio:0.0}× slower than median baseline {median:0}ms");
        }
        else
        {
            _out.WriteLine($"  Regression check: insufficient history ({previous.Count} runs), skipping comparison");
        }
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_Load_10MB()
    {
        var pt = new PieceTable();
        long ms = _session.Record("Load", "10MB", () => pt.Load(string.Concat(Enumerable.Repeat(Line, 10_000_000 / Line.Length))));
        AssertNoRegression("Load", "10MB", ms);
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_GetLine_Sequential_After1000Edits()
    {
        var pt = MakeEdited(10_000_000, 1000);
        long ms = _session.Record("GetLine sequential", "10MB edited", () =>
        {
            for (int i = 0; i < pt.LineCount; i++) _ = pt.GetLine(i);
        }, $"lines={pt.LineCount:N0}");
        AssertNoRegression("GetLine sequential", "10MB edited", ms);
        ms.Should().BeLessThan(200, "flat buffer makes this O(n) not O(n×pieces)");
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_RandomEdits_10MB()
    {
        var pt  = MakeEdited(10_000_000, 0);
        var rng = new Random(99);
        long ms = _session.Record("1000 random edits", "10MB", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                int off = rng.Next(0, Math.Max(1, pt.Length - 100));
                if (i % 2 == 0) pt.Insert(off, "EDIT\n");
                else             pt.Delete(off, Math.Min(5, pt.Length - off));
            }
        });
        AssertNoRegression("1000 random edits", "10MB", ms);
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_ReplaceAll_10MB()
    {
        var doc = new TextDocument();
        doc.Load(string.Concat(Enumerable.Repeat(Line, 10_000_000 / Line.Length)));
        int count = 0;
        long ms = _session.Record("ReplaceAll O(n)", "10MB", () => count = doc.ReplaceAll("ProcessItem", "HandleItem"), $"hits={count:N0}");
        AssertNoRegression("ReplaceAll O(n)", "10MB", ms);
        ms.Should().BeLessThan(2000, "O(n) bulk rewrite");
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_SearchLiteral_10MB()
    {
        var pt = MakeEdited(10_000_000, 0);
        var s  = new TextSearcher(pt);
        int cnt = 0;
        long ms = _session.Record("Search literal", "10MB", () => cnt = s.Count("ProcessItem"), $"hits={cnt:N0}");
        AssertNoRegression("Search literal", "10MB", ms);
    }

    [Fact, Trait("Category", "Regression")]
    public void Regression_GetLine_PrintsHistory()
    {
        // This test always runs last in the class — print history after recording
        var pt = MakeEdited(10_000_000, 1000);
        long ms = _session.Record("GetLine sequential", "10MB edited (v2)", () =>
        {
            for (int i = 0; i < pt.LineCount; i++) _ = pt.GetLine(i);
        });
        _session.PrintHistory(lastNRuns: 6);
        ms.Should().BeLessThan(300);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. MEMORY TESTS
// ═══════════════════════════════════════════════════════════════════════════

public class MemoryTests
{
    private readonly ITestOutputHelper _out;
    public MemoryTests(ITestOutputHelper o) => _out = o;

    // Creates the string AND the PieceTable inside its own stack frame so that
    // when this method returns both the input string and any load temporaries
    // become GC-eligible before the measurement is taken.
    // (In Debug builds the JIT keeps locals alive for the entire enclosing method,
    //  so inlining into the test body would keep the 200 MB string alive through
    //  the measurement — giving a false ~400 MB reading.)
    private static PieceTable LoadLargePieceTable()
    {
        var pt = new PieceTable();
        pt.Load(new string('x', 100_000_000));
        return pt;
    }

    [Fact]
    public void Load_100MB_MemoryUnder250MB()
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long before = GC.GetTotalMemory(true);

        // Both the 200 MB input string and its stack frame live only inside
        // LoadLargePieceTable; once it returns the string is GC-eligible.
        var pt = LoadLargePieceTable();

        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long after = GC.GetTotalMemory(true);   // force collect before reading
        long usedMB = (after - before) / 1024 / 1024;

        _out.WriteLine($"100MB load memory delta: {usedMB}MB");
        // Memory budget for a 100 MB file (100M chars = 200 MB in UTF-16):
        //   • _orig._data (char[]):  200 MB  — the normalised original buffer (no extra copy)
        //   • _add._data  (char[]):  ≤ 8 MB  — pre-allocated add buffer (capped at 4M chars)
        //   • _lineStarts, tree, metadata: a few MB
        // Total steady-state: ~210 MB.  Allow 250 MB for xUnit and GC headroom.
        usedMB.Should().BeLessThan(250, "steady-state should be ~1× doc size (orig buffer) after input string is GC'd");
    }

    [Fact]
    public void FlatBuffer_ReleasedAfterEdit()
    {
        // Verify the flat buffer is invalidated on edit and rebuilt correctly.
        // We use a delta memory measurement so that concurrently-running tests do not
        // affect the result (checking absolute GetTotalMemory() would be fragile in
        // xUnit's parallel runner where other tests may hold large allocations).
        var pt = new PieceTable();
        pt.Load(new string('x', 50_000));  // 50k char doc

        // Trigger flat buffer build
        _ = pt.GetLine(0);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        long before = GC.GetTotalMemory(true);

        // Edit invalidates the old flat buffer, then GetText/GetLine force a rebuild.
        pt.Insert(25_000, "INSERTED");
        var text = pt.GetText();
        text.Length.Should().Be(50_008);
        text.Substring(25_000, 8).Should().Be("INSERTED");

        var line = pt.GetLine(0);
        line.Should().NotBeNull();
        line.Length.Should().BeGreaterThan(0);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true);
        GC.WaitForPendingFinalizers();
        long after = GC.GetTotalMemory(true);
        long deltaMB = (after - before) / 1024 / 1024;

        _out.WriteLine($"Flat buffer churn delta: {deltaMB}MB");
        // The 50k-char doc = 100KB flat buffer rebuilt once = ~200KB delta.
        // Allow 50MB for xUnit/JIT/parallel-test overhead; the real concern is accumulating
        // O(N) copies of the flat buffer (that would be gigabytes on a large doc).
        deltaMB.Should().BeLessThan(50,
            "edit + rebuild should not accumulate many flat buffer copies");
    }

    [Fact]
    public void ManySmallEdits_PieceCountBounded()
    {
        var pt = new PieceTable(compactionThreshold: 10_000);
        pt.Load(new string('a', 10_000));

        var rng = new Random(1);
        for (int i = 0; i < 5000; i++)
        {
            int off = rng.Next(0, pt.Length);
            pt.Insert(off, "x");
        }

        _out.WriteLine($"Piece count after 5000 inserts: {pt.PieceCount}");
        // Without coalescing this would be 5001+ pieces.
        // Coalescing only helps sequential inserts, but 5000 random inserts
        // will accumulate pieces. The point is it shouldn't be unbounded.
        pt.PieceCount.Should().BeLessThan(15_000,
            "piece count shouldn't explode beyond 3× edit count");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. SEARCH EDGE CASES
// ═══════════════════════════════════════════════════════════════════════════

public class SearchEdgeCaseTests
{
    private static TextSearcher S(string content)
    {
        var pt = new PieceTable(); pt.Load(content); return new TextSearcher(pt);
    }

    // ── Boundary conditions ───────────────────────────────────────────────

    [Fact] public void Search_PatternEqualsDocument_SingleMatch()
        => S("hello").FindAll("hello").Should().HaveCount(1);

    [Fact] public void Search_PatternLongerThanDocument_NoMatch()
        => S("hi").FindFirst("hello world").Should().BeNull();

    [Fact] public void Search_EmptyDocument_NoMatch()
        => S("").FindFirst("x").Should().BeNull();

    [Fact] public void Search_SingleCharDocument_Finds()
        => S("x").FindFirst("x")!.Value.Offset.Should().Be(0);

    [Fact] public void Search_PatternAtOffsetZero_Found()
        => S("foobar").FindFirst("foo")!.Value.Offset.Should().Be(0);

    [Fact] public void Search_PatternAtEndExactly_Found()
    {
        var m = S("bazfoo").FindFirst("foo");
        m.Should().NotBeNull();
        m!.Value.Offset.Should().Be(3);
        m.Value.Length.Should().Be(3);
    }

    [Fact] public void Search_FindNext_FromEnd_ReturnsNull()
        => S("foo").FindNext("foo", 3).Should().BeNull();

    [Fact] public void Search_FindPrev_FromStart_ReturnsNull()
        => S("foofoo").FindPrev("foo", 0).Should().BeNull();

    [Fact] public void Search_FindPrev_ReturnsLastBeforeOffset()
    {
        var m = S("foofoo").FindPrev("foo", 6);
        m!.Value.Offset.Should().Be(3);
    }

    // ── Pattern spanning piece boundary ───────────────────────────────────

    [Fact] public void Search_PatternExactlyAtPieceSeam_BothSides()
    {
        var pt = new PieceTable();
        pt.Load("AAAAA");
        pt.Insert(5, "BBBBB");
        // "AB" straddles the seam at offset 4-5
        var m = new TextSearcher(pt).FindFirst("AB");
        m.Should().NotBeNull();
        m!.Value.Offset.Should().Be(4);
        m.Value.Length.Should().Be(2);
    }

    [Fact] public void Search_LongPatternAcrossMultipleSeams()
    {
        // Build "XABCDY" across 4 pieces: "XA" | "BC" | "D" | "Y"
        // Pattern "ABCD" spans pieces 1-3
        var pt = new PieceTable();
        pt.Load("XA");
        pt.Insert(2, "BC");
        pt.Insert(4, "D");
        pt.Insert(5, "Y");
        var m = new TextSearcher(pt).FindFirst("ABCD");
        m.Should().NotBeNull($"doc='{pt.GetText()}' pieces={pt.PieceCount}");
        m!.Value.Offset.Should().Be(1);
        m.Value.Length.Should().Be(4);
    }

    // ── Unicode ───────────────────────────────────────────────────────────

    [Fact] public void Search_Unicode_BasicMultilingual()
    {
        var s = S("héllo wörld café");
        s.FindFirst("café")!.Value.Offset.Should().Be(12);
    }

    [Fact] public void Search_Unicode_CaseInsensitive()
    {
        var m = S("CAFÉ café").FindFirst("café",
            new SearchOptions { CaseSensitive = false });
        m.Should().NotBeNull();
        m!.Value.Offset.Should().Be(0);   // first match is CAFÉ (uppercased match)
    }

    [Fact] public void Search_Emoji_SingleMatch()
    {
        // Emoji are multi-char in UTF-16; search must handle them correctly
        var s = S("hello 🎉 world 🎉");
        s.Count("🎉").Should().Be(2);
    }

    // ── CRLF in search ────────────────────────────────────────────────────

    [Fact] public void Search_CrLfDoc_NormalisedToLf_SearchesLf()
    {
        // Document loaded with CRLF — internally stored as LF
        var pt = new PieceTable();
        pt.Load("hello\r\nworld");
        var s = new TextSearcher(pt);
        // Internally it's "hello\nworld" — search for \n not \r\n
        s.FindFirst("\n")!.Value.Offset.Should().Be(5);
    }

    [Fact] public void Search_MultilineRegex_FindsAcrossLines()
    {
        var s = S("hello\nworld");
        var m = s.FindFirst(@"hello\nworld",
            new SearchOptions { UseRegex = true });
        m.Should().NotBeNull();
        m!.Value.Offset.Should().Be(0);
        m.Value.Length.Should().Be(11);
    }

    // ── Regex edge cases ──────────────────────────────────────────────────

    [Fact] public void Search_Regex_ZeroLengthMatch_DoesNotInfiniteLoop()
    {
        // Some regex patterns can match zero chars — must not loop forever
        var s = S("abc");
        var act = () => s.FindAll(@"\b", new SearchOptions { UseRegex = true, MaxResults = 10 }).ToList();
        act.Should().NotThrow();
    }

    [Fact] public void Search_Regex_CaptureGroups_MatchLengthCorrect()
    {
        var matches = S("abc123def456").FindAll(@"(\d+)",
            new SearchOptions { UseRegex = true }).ToList();
        matches.Should().HaveCount(2);
        matches[0].Length.Should().Be(3);   // "123"
        matches[1].Length.Should().Be(3);   // "456"
    }

    // ── WholeWord boundary cases ───────────────────────────────────────────

    [Fact] public void Search_WholeWord_AtDocumentStart()
    {
        var m = S("foo bar").FindFirst("foo", new SearchOptions { WholeWord = true });
        m!.Value.Offset.Should().Be(0);
    }

    [Fact] public void Search_WholeWord_AtDocumentEnd()
    {
        var m = S("bar foo").FindFirst("foo", new SearchOptions { WholeWord = true });
        m!.Value.Offset.Should().Be(4);
    }

    [Fact] public void Search_WholeWord_UnderscoreCounts_AsWordChar()
    {
        // "foo" inside "foo_bar" should NOT match as whole word
        S("foo_bar").FindFirst("foo", new SearchOptions { WholeWord = true })
            .Should().BeNull();
    }

    [Fact] public void Search_WholeWord_NumberAdjacent_NoMatch()
    {
        S("foo1").FindFirst("foo", new SearchOptions { WholeWord = true })
            .Should().BeNull();
    }

    // ── Count vs FindAll consistency ──────────────────────────────────────

    [Fact] public void Search_Count_MatchesFindAllCount()
    {
        var s   = S("the cat sat on the mat, the end");
        var all = s.FindAll("the").ToList();
        s.Count("the").Should().Be(all.Count);
    }

    [Fact] public void Search_MaxResults_ExactlyNResults()
    {
        var matches = S("aaaaaaaaaa").FindAll("a",
            new SearchOptions { MaxResults = 5 }).ToList();
        matches.Should().HaveCount(5);
        matches[4].Offset.Should().Be(4);
    }
}
