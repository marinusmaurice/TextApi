using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.Buffer;
using Xunit;
using Xunit.Abstractions;

namespace TextEditor.Tests;

/// <summary>
/// Buffer performance tests.
/// Results are written to BenchmarkHistory.json after each run so you can
/// compare across sessions with the history table printed at the end.
/// </summary>
public class PerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public PerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Buffer", output);
    }

    public void Dispose() { }

    private static string MakeDocument(int approxBytes)
    {
        const string line = "    public void ProcessItem(int id, string name) { return id + name.Length; }\n";
        int reps = Math.Max(1, approxBytes / line.Length);
        return string.Concat(Enumerable.Repeat(line, reps));
    }

    private static PieceTable LoadedTable(int approxBytes)
    {
        var pt = new PieceTable(); pt.Load(MakeDocument(approxBytes)); return pt;
    }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    // ── Load ─────────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void Load_10MB_Under500ms()
    {
        var pt = new PieceTable();
        long ms = Timed("Load", "10MB", () => pt.Load(MakeDocument(10_000_000)),
            $"lines={pt.LineCount:N0}");
        ms.Should().BeLessThan(1000);
    }

    [Fact, Trait("Category", "Perf")]
    public void Load_100MB_Under3000ms()
    {
        var pt = new PieceTable();
        long ms = Timed("Load", "100MB", () => pt.Load(MakeDocument(100_000_000)),
            $"lines={pt.LineCount:N0}");
        ms.Should().BeLessThan(3000);
    }

    // ── Keystroke coalescing ──────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void TypeCharByChar_10000Keystrokes_Under100ms_And_1Piece()
    {
        var pt = new PieceTable(); pt.Load("Hello World");
        long ms = Timed("10k keystrokes (coalesce)", "end-of-doc", () =>
        {
            for (int i = 0; i < 10_000; i++) pt.Insert(pt.Length, "x");
        }, $"pieces={pt.PieceCount}");
        pt.PieceCount.Should().BeLessThanOrEqualTo(3, "coalescing keeps piece count near 1");
        ms.Should().BeLessThan(100);
    }

    // ── Random edits ──────────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void RandomEdits_10MB_1000Ops_Under500ms()
    {
        var pt = LoadedTable(10_000_000); var rng = new Random(42);
        long ms = Timed("1000 random edits", "10MB", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                int off = rng.Next(0, Math.Max(1, pt.Length - 100));
                if (i % 2 == 0) pt.Insert(off, "INSERTED_TEXT_HERE\n");
                else            pt.Delete(off, Math.Min(10, pt.Length - off));
            }
        }, $"pieces={pt.PieceCount}");
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void RandomEdits_100MB_1000Ops_Under2000ms()
    {
        var pt = LoadedTable(100_000_000); var rng = new Random(42);
        long ms = Timed("1000 random edits", "100MB", () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                int off = rng.Next(0, Math.Max(1, pt.Length - 100));
                if (i % 2 == 0) pt.Insert(off, "INSERTED_TEXT_HERE\n");
                else            pt.Delete(off, Math.Min(10, pt.Length - off));
            }
        }, $"pieces={pt.PieceCount}");
        ms.Should().BeLessThan(2000);
    }

    // ── GetLine ───────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void GetLine_Sequential_10MB_Under1000ms()
    {
        var pt = LoadedTable(10_000_000); int lines = pt.LineCount;
        long ms = Timed("GetLine sequential", "10MB", () =>
        {
            for (int i = 0; i < lines; i++) _ = pt.GetLine(i);
        }, $"lines={lines:N0}");
        ms.Should().BeLessThan(1000);
    }

    [Fact, Trait("Category", "Perf")]
    public void GetLine_Random_10MB_1000Lookups_Under50ms()
    {
        var pt = LoadedTable(10_000_000); var rng = new Random(99); _ = pt.GetLine(0);
        long ms = Timed("GetLine random ×1000", "10MB", () =>
        {
            for (int i = 0; i < 1000; i++) _ = pt.GetLine(rng.Next(0, pt.LineCount));
        });
        ms.Should().BeLessThan(200);
    }

    // ── OffsetToPosition ──────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void OffsetToPosition_1000_Calls_10MB_Under50ms()
    {
        var pt = LoadedTable(10_000_000); var rng = new Random(7); _ = pt.OffsetToPosition(0);
        long ms = Timed("OffsetToPosition ×1000", "10MB", () =>
        {
            for (int i = 0; i < 1000; i++) _ = pt.OffsetToPosition(rng.Next(0, pt.Length));
        });
        ms.Should().BeLessThan(300);
    }

    // ── GetText ───────────────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void GetText_10MB_Under300ms()
    {
        var pt = LoadedTable(10_000_000);
        long ms = Timed("GetText full", "10MB", () => _ = pt.GetText());
        ms.Should().BeLessThan(300);
    }

    [Fact, Trait("Category", "Perf")]
    public void GetText_100MB_Under2000ms()
    {
        var pt = LoadedTable(100_000_000);
        long ms = Timed("GetText full", "100MB", () => _ = pt.GetText());
        ms.Should().BeLessThan(2000);
        _session.PrintHistory();
    }

    // ── Delete large range ────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void DeleteLargeRange_10MB_Under100ms()
    {
        var pt = LoadedTable(10_000_000);
        long ms = Timed("Delete 5MB block", "10MB", () => pt.Delete(1_000_000, 5_000_000),
            $"remaining={pt.Length:N0}");
        ms.Should().BeLessThan(200);
    }

    // ── Correctness guards ────────────────────────────────────────────────

    [Fact, Trait("Category", "Perf")]
    public void Correctness_After_RandomEdits_ContentIntact()
    {
        var pt = new PieceTable(); pt.Load("ABCDEFGHIJ");
        pt.Insert(5, "12345"); pt.Delete(0, 3);
        pt.GetText().Should().Be("DE12345FGHIJ");
    }

    [Fact, Trait("Category", "Perf")]
    public void LineIndex_Correct_After_Edits()
    {
        var pt = new PieceTable(); pt.Load("Line0\nLine1\nLine2");
        pt.Insert(6, "INSERTED\n");
        pt.GetLine(0).Should().Be("Line0");
        pt.GetLine(1).Should().Be("INSERTED");
        pt.GetLine(2).Should().Be("Line1");
        pt.GetLine(3).Should().Be("Line2");
        pt.LineCount.Should().Be(4);
    }

    [Fact, Trait("Category", "Perf")]
    public void OffsetToPosition_Correct_After_Insert()
    {
        var pt = new PieceTable(); pt.Load("Hello\nWorld");
        pt.Insert(6, "Beautiful\n");
        var (line, col) = pt.OffsetToPosition(16);
        line.Should().Be(2); col.Should().Be(0);
    }

    [Fact, Trait("Category", "Perf")]
    public void Compact_TriggeredAutomatically_ContentPreserved()
    {
        var doc = new TextDocument(compactionThreshold: 5);
        doc.Load("Start");
        for (int i = 0; i < 6; i++) doc.Insert(doc.Length, $" {i}");
        doc.GetText().Should().Be("Start 0 1 2 3 4 5");
    }
}
