using TextAPI.Core;
using TextAPI.Core.Snippets;
using Xunit;
using FluentAssertions;

namespace TextAPI.Tests;

// ── Helper ──────────────────────────────────────────────────────────────────

file static class SnippetTestHelpers
{
    public static TextDocument Doc(string content = "")
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }
}

// ── SnippetParser tests ──────────────────────────────────────────────────────

public class SnippetParserTests
{
    [Fact]
    public void Parse_Literal()
    {
        // Verify via SnippetEngine behaviour — no tab stops, no exit stop, text preserved.
        var snippet = SnippetEngine.Parse("hello world");
        snippet.TabStopIndices.Should().BeEmpty();
        snippet.HasExitStop.Should().BeFalse();

        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void Parse_SimpleSingleTabStop()
    {
        var snippet = SnippetEngine.Parse("$1");
        snippet.TabStopIndices.Should().Equal(new[] { 1 });
        snippet.HasExitStop.Should().BeFalse();
    }

    [Fact]
    public void Parse_TabStopWithPlaceholder()
    {
        // "${1:name}" → TabStop index=1, text="name"
        // Verify by expanding and checking inserted text
        var doc = SnippetTestHelpers.Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:name}"), 0);
        doc.GetText().Should().Be("name");
        var ts = session.NextTabStop()!;
        ts.Index.Should().Be(1);
        ts.Length.Should().Be(4); // "name"
        doc.GetText(ts.Offset, ts.Length).Should().Be("name");
    }

    [Fact]
    public void Parse_TabStopWithoutPlaceholder()
    {
        // "${1}" → TabStop index=1, text=""
        var doc = SnippetTestHelpers.Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1}"), 0);
        doc.GetText().Should().Be("");
        var ts = session.NextTabStop()!;
        ts.Index.Should().Be(1);
        ts.Length.Should().Be(0);
    }

    [Fact]
    public void Parse_MultipleTabStops()
    {
        // "$1 and $2" → parts: TabStop(1), Literal(" and "), TabStop(2)
        var snippet = SnippetEngine.Parse("$1 and $2");
        snippet.TabStopIndices.Should().Equal(new[] { 1, 2 });

        var doc = SnippetTestHelpers.Doc();
        var session = SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be(" and ");

        var ts1 = session.NextTabStop()!;
        ts1.Index.Should().Be(1);
        ts1.Offset.Should().Be(0);

        var ts2 = session.NextTabStop()!;
        ts2.Index.Should().Be(2);
        ts2.Offset.Should().Be(5); // after " and "
    }

    [Fact]
    public void Parse_ExitStop()
    {
        var snippet = SnippetEngine.Parse("$0");
        snippet.HasExitStop.Should().BeTrue();
        snippet.TabStopIndices.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Variable_TM_FILENAME()
    {
        // Verify variable is expanded when BeginSnippet is called
        var doc = SnippetTestHelpers.Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$TM_FILENAME"), 0,
            filename: "test.cs");
        doc.GetText().Should().Be("test.cs");
    }

    [Fact]
    public void Parse_Variable_CLIPBOARD()
    {
        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$CLIPBOARD"), 0,
            clipboardText: "pasted");
        doc.GetText().Should().Be("pasted");
    }

    [Fact]
    public void Parse_MixedContent()
    {
        // "for ($1; $2; $3) {\n\t$0\n}" → correct parts
        var snippet = SnippetEngine.Parse("for ($1; $2; $3) {\n\t$0\n}");
        snippet.TabStopIndices.Should().Equal(new[] { 1, 2, 3 });
        snippet.HasExitStop.Should().BeTrue();

        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("for (; ; ) {\n\t\n}");
    }

    [Fact]
    public void Parse_EscapedDollar()
    {
        // "\\$1" → literal "$1" (no tab stop)
        var snippet = SnippetEngine.Parse("\\$1");
        snippet.TabStopIndices.Should().BeEmpty();

        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("$1");
    }

    [Fact]
    public void Parse_EmptyBody()
    {
        var snippet = SnippetEngine.Parse("");
        snippet.TabStopIndices.Should().BeEmpty();
        snippet.HasExitStop.Should().BeFalse();

        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("");
    }

    [Fact]
    public void Parse_LiteralOnly()
    {
        var snippet = SnippetEngine.Parse("no tabs here");
        snippet.TabStopIndices.Should().BeEmpty();
        snippet.HasExitStop.Should().BeFalse();

        var doc = SnippetTestHelpers.Doc();
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("no tabs here");
    }
}

// ── Snippet tests ────────────────────────────────────────────────────────────

public class SnippetObjectTests
{
    [Fact]
    public void Snippet_TabStopIndices_Sorted()
    {
        // "${3:c} ${1:a} ${2:b}" → indices [1,2,3]
        var snippet = SnippetEngine.Parse("${3:c} ${1:a} ${2:b}");
        snippet.TabStopIndices.Should().Equal(new[] { 1, 2, 3 });
    }

    [Fact]
    public void Snippet_HasExitStop_True()
    {
        var snippet = SnippetEngine.Parse("${1:x} $0");
        snippet.HasExitStop.Should().BeTrue();
    }

    [Fact]
    public void Snippet_HasExitStop_False()
    {
        var snippet = SnippetEngine.Parse("${1:x}");
        snippet.HasExitStop.Should().BeFalse();
    }

    [Fact]
    public void Snippet_TabStopIndices_ExcludesZero()
    {
        var snippet = SnippetEngine.Parse("$1 $0 $2");
        snippet.TabStopIndices.Should().Equal(new[] { 1, 2 });
        snippet.HasExitStop.Should().BeTrue();
    }
}

// ── SnippetEngine / SnippetSession tests ─────────────────────────────────────

public class SnippetEngineTests
{
    private static TextDocument Doc(string content = "")
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }

    // ── BeginSnippet ─────────────────────────────────────────────────────

    [Fact]
    public void BeginSnippet_InsertsText()
    {
        var doc = Doc("hello ");
        var snippet = SnippetEngine.Parse("${1:world}");
        SnippetEngine.BeginSnippet(doc, snippet, 6);
        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void BeginSnippet_EmptyDoc_InsertsAtStart()
    {
        var doc = Doc();
        var snippet = SnippetEngine.Parse("hello");
        SnippetEngine.BeginSnippet(doc, snippet, 0);
        doc.GetText().Should().Be("hello");
    }

    [Fact]
    public void BeginSnippet_TabStopOffsets_CorrectAfterInsert()
    {
        var doc = Doc("XX");
        var snippet = SnippetEngine.Parse("${1:ab}${2:cd}");
        var session = SnippetEngine.BeginSnippet(doc, snippet, 2);

        doc.GetText().Should().Be("XXabcd");

        var ts1 = session.NextTabStop()!;
        ts1.Offset.Should().Be(2);
        ts1.Length.Should().Be(2);

        var ts2 = session.NextTabStop()!;
        ts2.Offset.Should().Be(4);
        ts2.Length.Should().Be(2);
    }

    [Fact]
    public void BeginSnippet_ReturnsActiveSession()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$1"), 0);
        session.IsActive.Should().BeTrue();
    }

    // ── Navigation ───────────────────────────────────────────────────────

    [Fact]
    public void NextTabStop_ReturnsFirstStop()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}${2:b}"), 0);
        var ts = session.NextTabStop()!;
        ts.Index.Should().Be(1);
    }

    [Fact]
    public void NextTabStop_AdvancesSequentially()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}${2:b}${3:c}"), 0);
        session.NextTabStop()!.Index.Should().Be(1);
        session.NextTabStop()!.Index.Should().Be(2);
        session.NextTabStop()!.Index.Should().Be(3);
    }

    [Fact]
    public void NextTabStop_ExitStop_Last()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a} $0"), 0);
        session.NextTabStop()!.Index.Should().Be(1);
        session.NextTabStop()!.Index.Should().Be(0);
    }

    [Fact]
    public void NextTabStop_PastEnd_ReturnsNull_SessionInactive()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}"), 0);
        session.NextTabStop(); // $1
        var result = session.NextTabStop(); // past end
        result.Should().BeNull();
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void PrevTabStop_GoesBack()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}${2:b}"), 0);
        session.NextTabStop(); // at $1
        session.NextTabStop(); // at $2
        var ts = session.PrevTabStop()!;
        ts.Index.Should().Be(1);
    }

    [Fact]
    public void PrevTabStop_AtFirstStop_ReturnsNull()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}${2:b}"), 0);
        session.NextTabStop(); // at $1
        var result = session.PrevTabStop();
        result.Should().BeNull();
    }

    [Fact]
    public void PrevTabStop_AfterNext_CanGoBack()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:a}${2:b}${3:c}"), 0);
        session.NextTabStop(); // $1
        session.NextTabStop(); // $2
        session.NextTabStop(); // $3
        session.PrevTabStop()!.Index.Should().Be(2);
        session.PrevTabStop()!.Index.Should().Be(1);
    }

    // ── GetPrimary / GetTabStops ──────────────────────────────────────────

    [Fact]
    public void GetPrimary_ReturnsCorrectStop()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:hello}${2:world}"), 0);
        var primary = session.GetPrimary(2)!;
        primary.Index.Should().Be(2);
        primary.Offset.Should().Be(5); // after "hello"
    }

    [Fact]
    public void GetTabStops_Returns_AllMirrors()
    {
        // "$1 and $1" → two tab stops with index 1
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$1 and $1"), 0);
        var mirrors = session.GetTabStops(1);
        mirrors.Should().HaveCount(2);
        mirrors.All(t => t.Index == 1).Should().BeTrue();
    }

    // ── UpdateTabStop ─────────────────────────────────────────────────────

    [Fact]
    public void UpdateTabStop_ReplacesShorter()
    {
        // "${1:name}" → replace with "x", subsequent stops shift
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:name}${2:end}"), 0);
        session.NextTabStop(); // position at $1

        // Before: "nameend", $1 at offset 0 len 4, $2 at offset 4 len 3
        session.UpdateTabStop(1, "x");

        // After: "xend", $2 should be at offset 1
        doc.GetText().Should().Be("xend");
        var ts2 = session.GetPrimary(2)!;
        ts2.Offset.Should().Be(1); // 0 + 1 ("x") = 1
        ts2.Length.Should().Be(3);
    }

    [Fact]
    public void UpdateTabStop_ReplacesLonger()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:x}${2:end}"), 0);
        session.NextTabStop(); // at $1

        session.UpdateTabStop(1, "hello world");

        doc.GetText().Should().Be("hello worldend");
        var ts2 = session.GetPrimary(2)!;
        ts2.Offset.Should().Be(11); // "hello world".Length
        ts2.Length.Should().Be(3);
    }

    [Fact]
    public void UpdateTabStop_MirrorFieldsUpdated()
    {
        // "${1:x} and $1" → two mirrors of $1
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:x} and $1"), 0);
        // Initial: "x and "
        doc.GetText().Should().Be("x and ");

        session.UpdateTabStop(1, "hello");

        // Both mirrors replaced: "hello and hello"
        doc.GetText().Should().Be("hello and hello");
    }

    // ── Commit / Cancel ───────────────────────────────────────────────────

    [Fact]
    public void Commit_SessionInactive()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:x}"), 0);
        session.Commit();
        session.IsActive.Should().BeFalse();
        // Text remains
        doc.GetText().Should().Be("x");
    }

    [Fact]
    public void Cancel_UndoesInsertion()
    {
        var doc = Doc("original");
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:snippet}"), 8);
        doc.GetText().Should().Be("originalsnippet");
        session.Cancel();
        doc.GetText().Should().Be("original");
        session.IsActive.Should().BeFalse();
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void Session_NoTabStops_ImmediatelyDone()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("hello"), 0);
        var ts = session.NextTabStop();
        ts.Should().BeNull();
        session.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Session_OnlyExitStop()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("hello$0"), 0);
        var ts = session.NextTabStop()!;
        ts.Index.Should().Be(0);
        session.NextTabStop().Should().BeNull();
        session.IsActive.Should().BeFalse();
    }

    // ── Variable substitution ─────────────────────────────────────────────

    [Fact]
    public void Variable_TM_FILENAME_Substituted()
    {
        var doc = Doc();
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$TM_FILENAME"), 0,
            filename: "MyFile.cs");
        doc.GetText().Should().Be("MyFile.cs");
    }

    [Fact]
    public void Variable_CLIPBOARD_Substituted()
    {
        var doc = Doc();
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$CLIPBOARD"), 0,
            clipboardText: "copied text");
        doc.GetText().Should().Be("copied text");
    }

    [Fact]
    public void Variable_Unknown_FallsBackToPlaceholder()
    {
        var doc = Doc();
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${UNKNOWN_VAR:fallback}"), 0);
        doc.GetText().Should().Be("fallback");
    }

    // ── Mirror field expansion ────────────────────────────────────────────

    [Fact]
    public void ForSnippet_CorrectExpansion()
    {
        // Use "${1:i} and $1" pattern — first occurrence has placeholder, second is mirror
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc,
            SnippetEngine.Parse("${1:i} and $1"), 0);

        // Initial expansion: "i and "
        doc.GetText().Should().Be("i and ");

        // First $1 is at offset 0 len 1, second $1 is at offset 6 len 0
        var mirrors = session.GetTabStops(1);
        mirrors.Should().HaveCount(2);
        mirrors[0].Offset.Should().Be(0);
        mirrors[0].Length.Should().Be(1); // "i"
        mirrors[1].Offset.Should().Be(6);
        mirrors[1].Length.Should().Be(0); // empty

        // UpdateTabStop on $1 updates all 3 occurrences
        session.UpdateTabStop(1, "index");

        doc.GetText().Should().Be("index and index");
        mirrors[0].Length.Should().Be(5);
        mirrors[1].Length.Should().Be(5);
    }

    // ── TabStopWithPlaceholder ────────────────────────────────────────────

    [Fact]
    public void TabStopWithPlaceholder_PlaceholderInDocument()
    {
        var doc = Doc();
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("before ${1:middle} after"), 0);
        doc.GetText().Should().Be("before middle after");
    }

    [Fact]
    public void TabStopWithPlaceholder_LengthCorrect()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc,
            SnippetEngine.Parse("before ${1:middle} after"), 0);
        var ts = session.NextTabStop()!;
        ts.Offset.Should().Be(7); // "before " = 7 chars
        ts.Length.Should().Be(6); // "middle"
        doc.GetText(ts.Offset, ts.Length).Should().Be("middle");
    }

    // ── Integration tests ────────────────────────────────────────────────

    [Fact]
    public void InsertSnippetInMiddleOfLine()
    {
        var doc = Doc("hello world");
        var session = SnippetEngine.BeginSnippet(doc,
            SnippetEngine.Parse("[${1:X}]"), 5);
        doc.GetText().Should().Be("hello[X] world");

        var ts = session.NextTabStop()!;
        ts.Offset.Should().Be(6);
        ts.Length.Should().Be(1);
    }

    [Fact]
    public void SnippetAfterSnippet()
    {
        var doc = Doc();

        // First snippet
        var s1 = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("${1:first}"), 0);
        s1.NextTabStop();
        s1.Commit();
        doc.GetText().Should().Be("first");

        // Second snippet appended
        var s2 = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse(" ${1:second}"), 5);
        s2.NextTabStop()!.Index.Should().Be(1);
        s2.Commit();
        doc.GetText().Should().Be("first second");
    }

    // ── CurrentTabStopIndex ──────────────────────────────────────────────

    [Fact]
    public void CurrentTabStopIndex_BeforeNavigation_IsMinusOne()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$1 $2"), 0);
        session.CurrentTabStopIndex.Should().Be(-1);
    }

    [Fact]
    public void CurrentTabStopIndex_UpdatesOnNavigation()
    {
        var doc = Doc();
        var session = SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("$1 $2"), 0);
        session.NextTabStop();
        session.CurrentTabStopIndex.Should().Be(1);
        session.NextTabStop();
        session.CurrentTabStopIndex.Should().Be(2);
    }
}
