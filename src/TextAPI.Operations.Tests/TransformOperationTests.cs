using FluentAssertions;
using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using Xunit;

namespace TextAPI.Operations.Tests;

file static class Helpers
{
    public static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        d.Load(text);
        return d;
    }

    public static PipelineResult Run(TextDocument doc, params IDocumentOperation[] ops)
    {
        var p = new DocumentPipeline(doc);
        foreach (var op in ops) p.Add(op);
        return p.Execute();
    }
}

public class TransformOperationTests
{
    // ── NormaliseWhitespace ───────────────────────────────────────────────────

    [Fact]
    public void NormaliseWhitespace_MultipleSpaces_CollapsesToSingleSpace()
    {
        var doc = Helpers.Doc("hello   world");

        var result = new NormaliseWhitespaceOperation().Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void NormaliseWhitespace_TabsAndSpaces_CollapsesToSingleSpace()
    {
        var doc = Helpers.Doc("hello\t\t  world");

        var result = new NormaliseWhitespaceOperation().Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void NormaliseWhitespace_PreservesNewlines()
    {
        var doc = Helpers.Doc("line  one\nline  two");

        new NormaliseWhitespaceOperation().Execute(doc);

        doc.GetText().Should().Be("line one\nline two");
    }

    // ── TrimTrailingWhitespace ────────────────────────────────────────────────

    [Fact]
    public void TrimTrailingWhitespace_TrailingSpaces_RemovesThemFromEachLine()
    {
        var doc = Helpers.Doc("line  \nother   \nclean");

        var result = new TrimTrailingWhitespaceOperation().Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("line\nother\nclean");
    }

    [Fact]
    public void TrimTrailingWhitespace_TrailingTabs_RemovesTabs()
    {
        var doc = Helpers.Doc("line\t\t\nnext\t");

        new TrimTrailingWhitespaceOperation().Execute(doc);

        doc.GetText().Should().Be("line\nnext");
    }

    [Fact]
    public void TrimTrailingWhitespace_NoTrailingWhitespace_LeavesDocumentUnchanged()
    {
        var doc = Helpers.Doc("clean\nlines\nhere");

        new TrimTrailingWhitespaceOperation().Execute(doc);

        doc.GetText().Should().Be("clean\nlines\nhere");
    }

    // ── ConvertCase ───────────────────────────────────────────────────────────

    [Fact]
    public void ConvertCase_Upper_ConvertsEntireDocToUpperCase()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ConvertCaseOperation { Mode = CaseMode.Upper }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("HELLO WORLD");
    }

    [Fact]
    public void ConvertCase_Lower_ConvertsEntireDocToLowerCase()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ConvertCaseOperation { Mode = CaseMode.Lower }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void ConvertCase_TitleCase_ConvertsEntireDocToTitleCase()
    {
        var doc = Helpers.Doc("hello world");

        var result = new ConvertCaseOperation { Mode = CaseMode.TitleCase }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void ConvertCase_WithAnchors_ConvertsOnlySection()
    {
        var doc = Helpers.Doc("before [START] change me [END] after");

        var result = new ConvertCaseOperation
        {
            Mode = CaseMode.Upper,
            StartAnchor = "[START]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("before [START] CHANGE ME [END] after");
    }

    [Fact]
    public void ConvertCase_AnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ConvertCaseOperation
        {
            Mode = CaseMode.Upper,
            StartAnchor = "[MISSING]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
    }

    // ── SortLines ─────────────────────────────────────────────────────────────

    [Fact]
    public void SortLines_Ascending_SortsLinesAlphabetically()
    {
        var doc = Helpers.Doc("banana\napple\ncherry");

        var result = new SortLinesOperation().Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("apple");
        doc.GetLine(1).Should().Be("banana");
        doc.GetLine(2).Should().Be("cherry");
    }

    [Fact]
    public void SortLines_Descending_SortsLinesReverseAlphabetically()
    {
        var doc = Helpers.Doc("apple\nbanana\ncherry");

        var result = new SortLinesOperation { Descending = true }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("cherry");
        doc.GetLine(1).Should().Be("banana");
        doc.GetLine(2).Should().Be("apple");
    }

    [Fact]
    public void SortLines_WithRange_SortsOnlySpecifiedRange()
    {
        var doc = Helpers.Doc("header\nbanana\napple\nfooter");

        var result = new SortLinesOperation { StartLine = 1, EndLine = 2 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("header");
        doc.GetLine(1).Should().Be("apple");
        doc.GetLine(2).Should().Be("banana");
        doc.GetLine(3).Should().Be("footer");
    }

    // ── DeduplicateLines ──────────────────────────────────────────────────────

    [Fact]
    public void DeduplicateLines_AllDuplicates_RemovesDuplicates()
    {
        var doc = Helpers.Doc("alpha\nbeta\nalpha\ngamma\nbeta");

        var result = new DeduplicateLinesOperation().Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("alpha\nbeta\ngamma");
    }

    [Fact]
    public void DeduplicateLines_ConsecutiveOnly_RemovesOnlyConsecutiveDuplicates()
    {
        var doc = Helpers.Doc("alpha\nalpha\nbeta\nalpha");

        var result = new DeduplicateLinesOperation { ConsecutiveOnly = true }.Execute(doc);

        result.Success.Should().BeTrue();
        // Only the second "alpha" (consecutive) is removed; the last "alpha" is kept
        doc.GetText().Should().Be("alpha\nbeta\nalpha");
    }

    // ── Indent ────────────────────────────────────────────────────────────────

    [Fact]
    public void Indent_AddPrefix_InsertsIndentOnAllLines()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = new IndentOperation { StartLine = 0 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("    line1");
        doc.GetLine(1).Should().Be("    line2");
        doc.GetLine(2).Should().Be("    line3");
    }

    [Fact]
    public void Indent_Dedent_RemovesPrefixFromLines()
    {
        var doc = Helpers.Doc("    line1\n    line2");

        var result = new IndentOperation { StartLine = 0, Dedent = true }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("line1");
        doc.GetLine(1).Should().Be("line2");
    }

    [Fact]
    public void Indent_CustomPrefix_UsesSpecifiedPrefix()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new IndentOperation { StartLine = 0, Prefix = "\t" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("\tline1");
        doc.GetLine(1).Should().Be("\tline2");
    }

    // ── WrapLines ─────────────────────────────────────────────────────────────

    [Fact]
    public void WrapLines_LongLine_WrapsAtWordBoundary()
    {
        var doc = Helpers.Doc("This is a very long line that should be wrapped at eighty columns when processed");

        var result = new WrapLinesOperation { MaxColumns = 40 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.LineCount.Should().BeGreaterThan(1);
        foreach (var i in Enumerable.Range(0, doc.LineCount))
            doc.GetLine(i).Length.Should().BeLessOrEqualTo(40);
    }

    [Fact]
    public void WrapLines_ShortLine_LeavesLineUnchanged()
    {
        var doc = Helpers.Doc("Short line");

        new WrapLinesOperation { MaxColumns = 80 }.Execute(doc);

        doc.GetText().Should().Be("Short line");
    }

    [Fact]
    public void WrapLines_InvalidMaxColumns_ReturnsFail()
    {
        var doc = Helpers.Doc("Some text");

        var result = new WrapLinesOperation { MaxColumns = 0 }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
