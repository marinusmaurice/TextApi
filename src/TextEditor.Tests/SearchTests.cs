using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.Buffer;
using TextEditor.Core.Search;
using Xunit;
using Xunit.Abstractions;

namespace TextEditor.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Correctness
// ═══════════════════════════════════════════════════════════════════════════

public class SearchCorrectnessTests
{
    private static TextSearcher S(string content)
    {
        var pt = new PieceTable(); pt.Load(content); return new TextSearcher(pt);
    }

    [Fact] public void FindFirst_SimpleMatch()
        => S("Hello World").FindFirst("World")!.Value.Offset.Should().Be(6);

    [Fact] public void FindFirst_NotFound_ReturnsNull()
        => S("Hello World").FindFirst("xyz").Should().BeNull();

    [Fact] public void FindAll_MultipleMatches_CorrectOffsets()
    {
        var m = S("abcabcabc").FindAll("abc").ToList();
        m.Should().HaveCount(3);
        m[0].Offset.Should().Be(0); m[1].Offset.Should().Be(3); m[2].Offset.Should().Be(6);
    }

    [Fact] public void FindAll_OverlappingMatches()
        => S("aaaa").FindAll("aa").Should().HaveCount(3);

    [Fact] public void FindNext_StartsAtOffset()
        => S("abcabc").FindNext("abc", 1)!.Value.Offset.Should().Be(3);

    [Fact] public void FindPrev_LastBeforeOffset()
        => S("abcabc").FindPrev("abc", 5)!.Value.Offset.Should().Be(3);

    [Fact] public void Count_CorrectCount()
        => S("the cat sat on the mat").Count("the").Should().Be(2);

    [Fact] public void FindFirst_CaseInsensitive()
    {
        var m = S("Hello World").FindFirst("hello", new SearchOptions { CaseSensitive = false });
        m!.Value.Offset.Should().Be(0); m.Value.Length.Should().Be(5);
    }

    [Fact] public void FindAll_CaseInsensitive_MixedCase()
        => S("Cat CAT cat CaT").FindAll("cat", new SearchOptions { CaseSensitive = false })
            .Should().HaveCount(4);

    [Fact] public void FindAll_WholeWord_SkipsPartialMatches()
    {
        var m = S("theather the").FindAll("the", new SearchOptions { WholeWord = true }).ToList();
        m.Should().HaveCount(1);
        m[0].Offset.Should().Be(9);
    }

    [Fact] public void FindAll_MatchSpanningPieceBoundary()
    {
        var pt = new PieceTable(); pt.Load("Hello"); pt.Insert(5, " World");
        var m = new TextSearcher(pt).FindFirst("o W");
        m.Should().NotBeNull();
        m!.Value.Offset.Should().Be(4); m.Value.Length.Should().Be(3);
    }

    [Fact] public void FindAll_LongPatternAcrossManyPieces()
    {
        var pt = new PieceTable(); pt.Load("START"); pt.Insert(5, "_MID"); pt.Insert(9, "_END");
        new TextSearcher(pt).FindFirst("START_MID_END")!.Value.Offset.Should().Be(0);
    }

    [Fact] public void FindAll_SingleChar()
    {
        var m = S("abacaba").FindAll("a").ToList();
        m.Should().HaveCount(4);
        m.Select(x => x.Offset).Should().Equal(0, 2, 4, 6);
    }

    [Fact] public void FindAll_MaxResults_StopsEarly()
        => S("aaaaaaa").FindAll("a", new SearchOptions { MaxResults = 3 }).Should().HaveCount(3);

    [Fact] public void FindAll_Regex_BasicPattern()
    {
        var m = S("foo123 bar456").FindAll(@"\d+", new SearchOptions { UseRegex = true }).ToList();
        m.Should().HaveCount(2); m[0].Offset.Should().Be(3); m[1].Offset.Should().Be(10);
    }

    [Fact] public void FindAll_Regex_CaseInsensitive()
        => S("Hello HELLO hello").FindAll("hello",
            new SearchOptions { UseRegex = true, CaseSensitive = false }).Should().HaveCount(3);

    [Fact] public void FindFirst_EmptyPattern_ReturnsNothing()
        => S("Hello").FindFirst("").Should().BeNull();

    [Fact] public void FindFirst_PatternLongerThanDoc_ReturnsNull()
        => S("Hi").FindFirst("Hello World").Should().BeNull();

    [Fact] public void FindFirst_EmptyDocument_ReturnsNull()
        => S("").FindFirst("abc").Should().BeNull();

    [Fact] public void FindAll_PatternAtStart()
        => S("abcdef").FindFirst("abc")!.Value.Offset.Should().Be(0);

    [Fact] public void FindAll_PatternAtEnd()
        => S("abcdef").FindFirst("def")!.Value.Offset.Should().Be(3);

    [Fact] public void MatchLength_IsCorrect()
        => S("Hello World").FindFirst("World")!.Value.Length.Should().Be(5);

    [Fact] public void BoundaryBridge_ManyPieces_AllMatchesFound()
    {
        var pt = new PieceTable(); pt.Load("X");
        int pos = 1; foreach (char c in "TARGET") pt.Insert(pos++, c.ToString());
        pt.Insert(pos, "X");
        var m = new TextSearcher(pt).FindFirst("TARGET");
        m.Should().NotBeNull($"doc='{pt.GetText()}' pieces={pt.PieceCount}");
        m!.Value.Offset.Should().Be(1); m.Value.Length.Should().Be(6);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// TextDocument integration
// ═══════════════════════════════════════════════════════════════════════════

public class SearchIntegrationTests
{
    [Fact] public void TextDocument_FindAll_WorksEndToEnd()
    {
        var doc = new TextDocument(); doc.Load("the cat sat on the mat");
        doc.FindAll("the").Should().HaveCount(2);
    }

    [Fact] public void TextDocument_ReplaceAll_ReplacesCorrectly()
    {
        var doc = new TextDocument(); doc.Load("foo bar foo baz foo");
        doc.ReplaceAll("foo", "qux").Should().Be(3);
        doc.GetText().Should().Be("qux bar qux baz qux");
    }

    [Fact] public void TextDocument_ReplaceAll_IsUndoable()
    {
        var doc = new TextDocument(); doc.Load("foo foo foo");
        doc.ReplaceAll("foo", "bar");
        doc.GetText().Should().Be("bar bar bar");
        doc.Undo();
        doc.GetText().Should().Be("foo foo foo");
    }

    [Fact] public void TextDocument_ReplaceAll_DifferentLengths_Correct()
    {
        var doc = new TextDocument(); doc.Load("hi there hi");
        doc.ReplaceAll("hi", "hello");
        doc.GetText().Should().Be("hello there hello");
    }

    [Fact] public void TextDocument_ReplaceAll_ShorterReplacement_Correct()
    {
        var doc = new TextDocument(); doc.Load("foo bar foo");
        doc.ReplaceAll("foo", "f");
        doc.GetText().Should().Be("f bar f");
    }

    [Fact] public void TextDocument_ReplaceAll_NoMatches_ReturnsZero()
    {
        var doc = new TextDocument(); doc.Load("hello world");
        doc.ReplaceAll("xyz", "abc").Should().Be(0);
        doc.GetText().Should().Be("hello world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Performance / profiling — with history tracking
// ═══════════════════════════════════════════════════════════════════════════

public class SearchPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly BenchmarkSession  _session;

    public SearchPerformanceTests(ITestOutputHelper out_)
    {
        _out     = out_;
        _session = new BenchmarkSession("Search", out_);
    }

    public void Dispose() { }

    private const string Line =
        "    public void ProcessItem(int id, string name) { return id + name.Length; }\n";

    private static PieceTable MakeTable(int approxBytes)
    {
        int reps = Math.Max(1, approxBytes / Line.Length);
        var pt   = new PieceTable();
        pt.Load(string.Concat(Enumerable.Repeat(Line, reps)));
        return pt;
    }

    private long Timed(string name, string label, Action a, string extra = "")
        => _session.Record(name, label, a, extra);

    [Fact, Trait("Category", "Search")]
    public void Profile_Literal_10MB()
    {
        var pt = MakeTable(10_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search literal", "10MB", () => cnt = s.Count("ProcessItem"), $"hits={cnt:N0}");
        cnt.Should().BeGreaterThan(0); ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_Literal_100MB()
    {
        var pt = MakeTable(100_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search literal", "100MB", () => cnt = s.Count("ProcessItem"), $"hits={cnt:N0}");
        cnt.Should().BeGreaterThan(0); ms.Should().BeLessThan(2000);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_SingleChar_10MB()
    {
        var pt = MakeTable(10_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search single-char", "10MB", () => cnt = s.Count("i"), $"hits={cnt:N0}");
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_RareWord_100MB()
    {
        var pt = MakeTable(100_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search absent pattern", "100MB",
            () => cnt = s.Count("NOTFOUND_XYZ_QQQ"), $"hits={cnt}");
        ms.Should().BeLessThan(2000);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_CaseInsensitive_10MB()
    {
        var pt = MakeTable(10_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search case-insensitive", "10MB",
            () => cnt = s.Count("processitem", new SearchOptions { CaseSensitive = false }),
            $"hits={cnt:N0}");
        cnt.Should().BeGreaterThan(0); ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_Regex_10MB()
    {
        var pt = MakeTable(10_000_000); var s = new TextSearcher(pt); int cnt = 0;
        long ms = Timed("Search regex \\bint\\b", "10MB",
            () => cnt = s.Count(@"\bint\b", new SearchOptions { UseRegex = true }), $"hits={cnt:N0}");
        cnt.Should().BeGreaterThan(0); ms.Should().BeLessThan(1500);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_FindFirst_100MB_EarlyExit()
    {
        var pt = MakeTable(100_000_000); var s = new TextSearcher(pt);
        _ = s.FindFirst("ProcessItem"); _ = s.FindFirst("ProcessItem"); // warm JIT
        SearchMatch? result = null;
        long ms = Timed("FindFirst early-exit", "100MB",
            () => result = s.FindFirst("ProcessItem"), $"offset={result?.Offset}");
        result.Should().NotBeNull();
        ms.Should().BeLessThan(500);
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_ReplaceAll_10MB()
    {
        var doc = new TextDocument();
        doc.Load(string.Concat(Enumerable.Repeat(Line, 10_000_000 / Line.Length)));
        int count = 0;
        long ms = Timed("ReplaceAll O(n)", "10MB",
            () => count = doc.ReplaceAll("ProcessItem", "HandleItem"),
            $"replacements={count:N0}");
        count.Should().BeGreaterThan(0);
        doc.GetText().Should().NotContain("ProcessItem");
        doc.GetText().Should().Contain("HandleItem");
        ms.Should().BeLessThan(2000, "O(n) bulk rewrite — not O(n log n)");
    }

    [Fact, Trait("Category", "Search")]
    public void Profile_ReplaceAll_DifferentLength_10MB()
    {
        // Replace shorter → longer (output buffer grows)
        var doc = new TextDocument();
        doc.Load(string.Concat(Enumerable.Repeat(Line, 10_000_000 / Line.Length)));
        int count = 0;
        long ms = Timed("ReplaceAll O(n) grow", "10MB",
            () => count = doc.ReplaceAll("id", "identifier"),
            $"replacements={count:N0}");
        count.Should().BeGreaterThan(0);
        ms.Should().BeLessThan(3000);
        _session.PrintHistory();
    }
}
