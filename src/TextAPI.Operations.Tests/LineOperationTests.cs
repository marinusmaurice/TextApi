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

public class LineOperationTests
{
    // ── InsertLine ────────────────────────────────────────────────────────────

    [Fact]
    public void InsertLine_AtLineZero_InsertsBeforeFirstLine()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = new InsertLineOperation { LineIndex = 0, Text = "line0" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("line0");
        doc.GetLine(1).Should().Be("line1");
    }

    [Fact]
    public void InsertLine_AtMiddle_ShiftsSubsequentLines()
    {
        var doc = Helpers.Doc("line1\nline3");

        var result = new InsertLineOperation { LineIndex = 1, Text = "line2" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("line1");
        doc.GetLine(1).Should().Be("line2");
        doc.GetLine(2).Should().Be("line3");
    }

    [Fact]
    public void InsertLine_AtLineCount_AppendsAfterLastLine()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new InsertLineOperation { LineIndex = doc.LineCount, Text = "line3" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(doc.LineCount - 1).Should().Be("line3");
    }

    [Fact]
    public void InsertLine_NegativeIndex_ReturnsFail()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new InsertLineOperation { LineIndex = -1, Text = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void InsertLine_IndexBeyondLineCount_ReturnsFail()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new InsertLineOperation { LineIndex = 100, Text = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ── DeleteLine ────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteLine_FirstLine_RemovesFirstLine()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = new DeleteLineOperation { LineIndex = 0 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("line2");
        doc.LineCount.Should().Be(2);
    }

    [Fact]
    public void DeleteLine_LastLine_RemovesLastLine()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = new DeleteLineOperation { LineIndex = 2 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.LineCount.Should().Be(2);
        doc.GetLine(1).Should().Be("line2");
    }

    [Fact]
    public void DeleteLine_MiddleLine_PreservesAdjacentLines()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = new DeleteLineOperation { LineIndex = 1 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(0).Should().Be("line1");
        doc.GetLine(1).Should().Be("line3");
        doc.LineCount.Should().Be(2);
    }

    [Fact]
    public void DeleteLine_OutOfRangeIndex_ReturnsFail()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new DeleteLineOperation { LineIndex = 10 }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DeleteLine_ReportsCharsDeleted()
    {
        var doc = Helpers.Doc("abcde\nfghij");

        var result = new DeleteLineOperation { LineIndex = 0 }.Execute(doc);

        result.CharsDeleted.Should().BeGreaterThan(0);
    }

    // ── ReplaceLine ───────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceLine_HappyPath_ReplacesLineContent()
    {
        var doc = Helpers.Doc("line1\nold content\nline3");

        var result = new ReplaceLineOperation { LineIndex = 1, Text = "new content" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(1).Should().Be("new content");
    }

    [Fact]
    public void ReplaceLine_PreservesOtherLines()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        new ReplaceLineOperation { LineIndex = 1, Text = "replaced" }.Execute(doc);

        doc.GetLine(0).Should().Be("line1");
        doc.GetLine(2).Should().Be("line3");
    }

    [Fact]
    public void ReplaceLine_OutOfRangeIndex_ReturnsFail()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new ReplaceLineOperation { LineIndex = 99, Text = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
    }

    // ── InsertAfterLineMatch ──────────────────────────────────────────────────

    [Fact]
    public void InsertAfterLineMatch_PlainText_InsertsAfterMatchingLine()
    {
        var doc = Helpers.Doc("header\ndata\nfooter");

        var result = new InsertAfterLineMatchOperation { Pattern = "header", LineText = "subheader" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(1).Should().Be("subheader");
        doc.GetLine(2).Should().Be("data");
    }

    [Fact]
    public void InsertAfterLineMatch_RegexMatch_InsertsAfterMatchingLine()
    {
        var doc = Helpers.Doc("version: 1.0\nname: app\nenv: prod");

        var result = new InsertAfterLineMatchOperation
        {
            Pattern = @"version:\s*\d+\.\d+",
            LineText = "revision: 0",
            UseRegex = true
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(1).Should().Be("revision: 0");
    }

    [Fact]
    public void InsertAfterLineMatch_SecondOccurrence_FindsCorrectLine()
    {
        var doc = Helpers.Doc("section\ncontent\nsection\nmore content");

        var result = new InsertAfterLineMatchOperation
        {
            Pattern = "section",
            LineText = "inserted",
            Occurrence = 2
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetLine(3).Should().Be("inserted");
    }

    [Fact]
    public void InsertAfterLineMatch_NoMatch_ReturnsFail()
    {
        var doc = Helpers.Doc("line1\nline2");

        var result = new InsertAfterLineMatchOperation { Pattern = "missing", LineText = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("missing");
    }
}
