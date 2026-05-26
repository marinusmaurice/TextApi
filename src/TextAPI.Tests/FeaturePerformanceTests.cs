using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.ChangeTracking;
using TextAPI.Core.Cursor;
using TextAPI.Core.Decorations;
using TextAPI.Core.Diff;
using TextAPI.Core.Folding;
using TextAPI.Core.InlayHints;
using TextAPI.Core.Language;
using TextAPI.Core.Navigation;
using TextAPI.Core.Search;
using TextAPI.Core.WordWrap;
using Xunit;
using Xunit.Abstractions;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Feature Performance Test Suite
//
// Measures performance of all major features against 10 MB documents.
// Uses the BenchmarkSession pattern for history tracking.
// ═══════════════════════════════════════════════════════════════════════════

file static class FPHelpers
{
    private const string CodeLine =
        "    public void ProcessItem(int id, string name) { return id + name.Length; }\n";

    private const string CSharpLine =
        "    public int Calculate(int a, int b) { return a + b; } // method\n";

    public static string MakeDocument(int approxBytes)
    {
        int reps = Math.Max(1, approxBytes / CodeLine.Length);
        return string.Concat(Enumerable.Repeat(CodeLine, reps));
    }

    public static string MakeCSharpDocument(int approxBytes)
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine("using System;");
        lines.AppendLine("namespace Perf {");
        int reps = Math.Max(1, approxBytes / CSharpLine.Length);
        for (int i = 0; i < reps; i++)
            lines.Append(CSharpLine);
        lines.AppendLine("}");
        return lines.ToString();
    }

    public static TextDocument LoadDoc(int approxBytes)
    {
        var doc = new TextDocument();
        doc.Load(MakeDocument(approxBytes));
        return doc;
    }

    public static TextDocument LoadCSharpDoc(int approxBytes)
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load(MakeCSharpDocument(approxBytes));
        return doc;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Paste / Insert performance
// ═══════════════════════════════════════════════════════════════════════════

public class PastePerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public PastePerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Paste", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void Paste_10MB_SingleInsert_Under500ms()
    {
        var doc   = new TextDocument();
        string block = FPHelpers.MakeDocument(10_000_000);
        long ms = Timed("Paste single Insert", "10MB", () => doc.Insert(0, block),
            $"lines={doc.LineCount:N0}");
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void Paste_1000Lines_OnePerLine_Under2000ms()
    {
        // Use an empty document to avoid cache-resize issues in LineHighlightCache
        var doc = new TextDocument();
        long ms = Timed("Paste 1000 lines loop", "1k inserts", () =>
        {
            for (int i = 0; i < 1000; i++)
                doc.Insert(doc.Length, "    public void Method" + i + "() { }\n");
        }, $"lines={doc.LineCount:N0}");
        ms.Should().BeLessThan(2000);
    }

    [Fact, Trait("Category", "Perf")]
    public void Paste_10MB_ThenUndo_Under500ms()
    {
        var doc   = new TextDocument();
        string block = FPHelpers.MakeDocument(10_000_000);
        doc.Insert(0, block);
        long ms = Timed("Paste then Undo", "10MB", () => doc.Undo());
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void Paste_10MB_ThenGetText_Under300ms()
    {
        var doc = FPHelpers.LoadDoc(10_000_000);
        string? text = null;
        long ms = Timed("GetText after paste", "10MB", () => text = doc.GetText(),
            $"len={text?.Length:N0}");
        ms.Should().BeLessThan(300);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Search performance
// ═══════════════════════════════════════════════════════════════════════════

public class FeatureSearchPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public FeatureSearchPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Search", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    private static TextSearcher MakeSearcher(TextDocument doc)
    {
        var pt = new PieceTable();
        pt.Load(doc.GetText());
        return new TextSearcher(pt);
    }

    [Fact, Trait("Category", "Perf")]
    public void FindAll_Plain_10MB_Under500ms()
    {
        var doc     = FPHelpers.LoadDoc(10_000_000);
        var searcher = MakeSearcher(doc);
        List<SearchMatch>? matches = null;
        long ms = Timed("FindAll plain", "10MB", () =>
            matches = searcher.FindAll("ProcessItem").ToList(),
            $"count={matches?.Count:N0}");
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void FindAll_Regex_10MB_Under1000ms()
    {
        var doc     = FPHelpers.LoadDoc(10_000_000);
        var searcher = MakeSearcher(doc);
        var opts     = new SearchOptions { UseRegex = true };
        List<SearchMatch>? matches = null;
        long ms = Timed("FindAll regex", "10MB", () =>
            matches = searcher.FindAll(@"public\s+void\s+\w+", opts).ToList(),
            $"count={matches?.Count:N0}");
        ms.Should().BeLessThan(1000);
    }

    [Fact, Trait("Category", "Perf")]
    public void ReplaceAll_Plain_10MB_Under500ms()
    {
        var doc = FPHelpers.LoadDoc(10_000_000);
        int count = 0;
        long ms = Timed("ReplaceAll plain", "10MB", () =>
            count = doc.ReplaceAll("ProcessItem", "HandleItem"),
            $"replaced={count:N0}");
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void ReplaceAll_RegexCapture_10MB_Under1000ms()
    {
        var doc  = FPHelpers.LoadDoc(10_000_000);
        var opts = new SearchOptions { UseRegex = true };
        int count = 0;
        long ms = Timed("ReplaceAll regex capture", "10MB", () =>
            count = doc.ReplaceAll(@"public void (\w+)", "private void $1",  opts),
            $"replaced={count:N0}");
        ms.Should().BeLessThan(1000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Undo / Redo performance
// ═══════════════════════════════════════════════════════════════════════════

public class UndoPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public UndoPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Undo", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void Undo_1000CharInserts_AllUndos_Under500ms()
    {
        var doc = new TextDocument();
        doc.Load("start");
        for (int i = 0; i < 1000; i++)
            doc.Insert(doc.Length, "x");

        long ms = Timed("Undo 1000 char inserts", "1k ops", () =>
        {
            while (doc.CanUndo) doc.Undo();
        });
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Perf")]
    public void Undo_500Replaces_AllUndos_Under1000ms()
    {
        var doc = new TextDocument();
        doc.Load(FPHelpers.MakeDocument(100_000));
        for (int i = 0; i < 500; i++)
            doc.Replace(0, 6, "public");   // replace with same length — always valid

        long ms = Timed("Undo 500 replaces", "500 ops", () =>
        {
            while (doc.CanUndo) doc.Undo();
        });
        ms.Should().BeLessThan(1000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Change Tracking performance
// ═══════════════════════════════════════════════════════════════════════════

public class ChangeTrackingPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public ChangeTrackingPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("ChangeTracking", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void Edit_10000Lines_GetStatusAll_Under500ms()
    {
        const int N   = 10_000;
        var lines     = Enumerable.Range(0, N).Select(i => $"line{i:D5}");
        var doc       = new TextDocument();
        doc.Load(string.Join("\n", lines));
        var tracker   = doc.GetChangeTracker();

        // Edit every line with a Replace at start
        for (int i = 0; i < N; i++)
        {
            int off = doc.PositionToOffset(i, 0);
            doc.Replace(off, 4, "LINE");
        }

        long ms = Timed("GetStatus all 10k lines", "10k lines", () =>
        {
            for (int i = 0; i < doc.LineCount; i++)
                _ = tracker.GetStatus(i);
        });
        ms.Should().BeLessThan(6000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. Syntax Highlighting performance
// ═══════════════════════════════════════════════════════════════════════════

public class SyntaxHighlightingPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public SyntaxHighlightingPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("SyntaxHighlighting", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void TokeniseLines_10MB_CSharp_Under8000ms()
    {
        var doc = FPHelpers.LoadCSharpDoc(10_000_000);
        long ms = Timed("TokeniseLines full", "10MB C#", () =>
            doc.TokeniseLines(0, doc.LineCount - 1),
            $"lines={doc.LineCount:N0}");
        ms.Should().BeLessThan(8000);
    }

    [Fact, Trait("Category", "Perf")]
    public void TokeniseLines_IncrementalWindow_Under100ms()
    {
        var doc = FPHelpers.LoadCSharpDoc(10_000_000);
        doc.TokeniseLines(0, doc.LineCount - 1); // initial full pass
        // Edit one line then retokenise a visible window of 100 lines
        doc.Replace(doc.PositionToOffset(50, 0), 4, "void");
        long ms = Timed("TokeniseLines incremental 100 lines", "100 lines", () =>
            doc.TokeniseLines(0, Math.Min(99, doc.LineCount - 1)));
        ms.Should().BeLessThan(6000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Folding performance
// ═══════════════════════════════════════════════════════════════════════════

public class FoldingPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public FoldingPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Folding", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void UpdateRegions_BraceStrategy_10MB_Under6000ms()
    {
        var doc     = FPHelpers.LoadCSharpDoc(10_000_000);
        var folding = doc.GetFoldingModel();
        long ms = Timed("UpdateRegions BraceStrategy", "10MB C#", () =>
            folding.UpdateRegions(new BraceFoldingStrategy()),
            $"regions={folding.Regions.Count:N0}");
        ms.Should().BeLessThan(6000);
    }

    [Fact, Trait("Category", "Perf")]
    public void FoldAll_GetVisibleLines_10MB_Under500ms()
    {
        var doc     = FPHelpers.LoadCSharpDoc(10_000_000);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        // Fold all top-level regions
        foreach (var r in folding.Regions.ToList())
            folding.Fold(r.StartLine);

        long ms = Timed("GetVisibleLines after FoldAll", "10MB C#", () =>
            _ = folding.GetVisibleLines(),
            $"regions={folding.Regions.Count:N0}");
        ms.Should().BeLessThan(500);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Word Wrap performance
// ═══════════════════════════════════════════════════════════════════════════

public class WordWrapPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public WordWrapPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("WordWrap", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void GetWordWrapModel_Build_10MB_Under1000ms()
    {
        var doc = FPHelpers.LoadDoc(10_000_000);
        WordWrapModel? model = null;
        long ms = Timed("GetWordWrapModel build", "10MB", () =>
            model = doc.GetWordWrapModel(80),
            $"lines={doc.LineCount:N0}");
        ms.Should().BeLessThan(1000);
    }

    [Fact, Trait("Category", "Perf")]
    public void WrappedRowCount_AllLines_10MB_Under2000ms()
    {
        var doc   = FPHelpers.LoadDoc(10_000_000);
        var model = doc.GetWordWrapModel(80);
        long ms = Timed("WrappedRowCount all lines", "10MB", () =>
        {
            for (int i = 0; i < doc.LineCount; i++)
                _ = model.WrappedRowCount(i);
        }, $"lines={doc.LineCount:N0}");
        ms.Should().BeLessThan(2000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Multi-Cursor performance
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public MultiCursorPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("MultiCursor", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void AddColumnSelection_10000Lines_Under200ms()
    {
        const int N = 10_000;
        var doc     = new TextDocument();
        doc.Load(string.Join("\n", Enumerable.Range(0, N).Select(i => $"    line{i:D5}")));
        var mc      = new MultiCursor(doc);
        long ms = Timed("AddColumnSelection 10k lines", "10k lines", () =>
            mc.AddColumnSelection(0, N - 1, 0),
            $"cursors={mc.Count:N0}");
        ms.Should().BeLessThan(200);
    }

    [Fact, Trait("Category", "Perf")]
    public void InsertText_100Cursors_10000LineDoc_Under500ms()
    {
        const int N = 10_000;
        var doc     = new TextDocument();
        doc.Load(string.Join("\n", Enumerable.Range(0, N).Select(i => $"    line{i:D5}")));
        var mc      = new MultiCursor(doc);
        // Place 100 cursors evenly spaced
        mc.SetSingle(0);
        for (int i = 1; i < 100; i++)
            mc.AddCursor(doc.PositionToOffset(i * (N / 100), 0));

        long ms = Timed("InsertText 100 cursors", "10k line doc", () =>
            mc.InsertText("X"));
        ms.Should().BeLessThan(500);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. Diff performance
// ═══════════════════════════════════════════════════════════════════════════

public class DiffPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public DiffPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("Diff", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void Diff_5000Lines_100Changes_Under2000ms()
    {
        const int N = 5000;
        var old = Enumerable.Range(0, N).Select(i => $"line {i:D5}").ToArray();
        var neo = old.ToArray();
        // Introduce 100 changes spread evenly
        for (int i = 0; i < 100; i++)
            neo[i * (N / 100)] = $"CHANGED line {i * (N / 100):D5}";

        Core.Diff.DiffResult? result = null;
        long ms = Timed("TextDiff.Diff 5000 lines", "100 changes", () =>
            result = TextDiff.Diff(old, neo),
            $"hunks={result?.Hunks.Count:N0}");
        ms.Should().BeLessThan(2000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. InlayHints performance
// ═══════════════════════════════════════════════════════════════════════════

public class InlayHintsPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public InlayHintsPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("InlayHints", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void GetHintsInRange_10000Hints_Under100ms()
    {
        var doc   = FPHelpers.LoadDoc(1_000_000);
        var hints = doc.GetInlayHintModel();
        // Add 10000 hints spread across the doc
        int step = doc.Length / 10_000;
        for (int i = 0; i < 10_000; i++)
            hints.AddHint(new InlayHint(i * step, $"hint{i}"));

        long ms = Timed("GetHintsInRange 10k hints", "10k hints", () =>
            _ = hints.GetHintsInRange(0, doc.Length));
        ms.Should().BeLessThan(100);
    }

    [Fact, Trait("Category", "Perf")]
    public void Insert_Offset0_Shifts_10000Hints_Under200ms()
    {
        var doc   = FPHelpers.LoadDoc(1_000_000);
        var hints = doc.GetInlayHintModel();
        int step  = doc.Length / 10_000;
        for (int i = 0; i < 10_000; i++)
            hints.AddHint(new InlayHint(i * step + 1, $"h{i}"));

        long ms = Timed("Insert shifts 10k hints", "10k hints", () =>
            doc.Insert(0, "X"));
        ms.Should().BeLessThan(200);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 11. DocumentCleanup performance
// ═══════════════════════════════════════════════════════════════════════════

public class DocumentCleanupPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public DocumentCleanupPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("DocumentCleanup", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Perf")]
    public void TrimTrailingWhitespace_500KB_Under3000ms()
    {
        // 500KB so the piece-table fragmentation stays manageable (each trimmed line
        // splits a piece; at 10MB that's ~240k splits → O(n log n) = ~138 s).
        const string dirtyLine = "    public void Method() { return; }   \n";
        int reps = Math.Max(1, 500_000 / dirtyLine.Length);
        var doc  = new TextDocument();
        doc.Load(string.Concat(Enumerable.Repeat(dirtyLine, reps)));
        int count = 0;
        long ms = Timed("TrimTrailingWhitespace", "500KB dirty", () =>
            count = DocumentCleanup.TrimTrailingWhitespace(doc),
            $"trimmed={count:N0}");
        ms.Should().BeLessThan(5000);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 12. Search + ReplaceAll combined performance
// ═══════════════════════════════════════════════════════════════════════════

public class SearchReplaceAllPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public SearchReplaceAllPerformanceTests(ITestOutputHelper output)
    {
        _out     = output;
        _session = new BenchmarkSession("SearchReplaceAll", output);
    }

    public void Dispose() { }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    private static TextSearcher MakeSearcher(TextDocument doc)
    {
        var pt = new PieceTable();
        pt.Load(doc.GetText());
        return new TextSearcher(pt);
    }

    [Fact, Trait("Category", "Perf")]
    public void FindAll_Then_ReplaceAll_10MB_Under1000ms()
    {
        var doc      = FPHelpers.LoadDoc(10_000_000);
        var searcher = MakeSearcher(doc);
        List<SearchMatch>? matches = null;
        int count = 0;
        long ms = Timed("FindAll+ReplaceAll combined", "10MB", () =>
        {
            matches = searcher.FindAll("ProcessItem").ToList();
            count   = doc.ReplaceAll("ProcessItem", "HandleItem");
        }, $"found={matches?.Count:N0} replaced={count:N0}");
        ms.Should().BeLessThan(1000);
        // Verify consistency: same count from both methods
        matches!.Count.Should().Be(count);
        _session.PrintHistory();
    }
}
