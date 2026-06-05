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

public class AnchorOperationTests
{
    // ── InsertAfter ───────────────────────────────────────────────────────────

    [Fact]
    public void InsertAfter_HappyPath_InsertsTextAfterAnchor()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertAfterOperation { Anchor = "Hello", Text = " Beautiful" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello Beautiful World");
    }

    [Fact]
    public void InsertAfter_AnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertAfterOperation { Anchor = "Missing", Text = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Missing");
    }

    [Fact]
    public void InsertAfter_SecondOccurrence_FindsCorrectInstance()
    {
        var doc = Helpers.Doc("cat and cat and dog");

        var result = new InsertAfterOperation { Anchor = "cat", Text = "(2)", Occurrence = 2 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("cat and cat(2) and dog");
    }

    [Fact]
    public void InsertAfter_CaseInsensitive_MatchesDespiteCaseDifference()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertAfterOperation { Anchor = "hello", Text = "!", CaseSensitive = false }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello! World");
    }

    [Fact]
    public void InsertAfter_ReportsCharsInserted()
    {
        var doc = Helpers.Doc("AB");

        var result = new InsertAfterOperation { Anchor = "A", Text = "XXX" }.Execute(doc);

        result.CharsInserted.Should().Be(3);
    }

    // ── InsertBefore ──────────────────────────────────────────────────────────

    [Fact]
    public void InsertBefore_HappyPath_InsertsTextBeforeAnchor()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertBeforeOperation { Anchor = "World", Text = "Beautiful " }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello Beautiful World");
    }

    [Fact]
    public void InsertBefore_AnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertBeforeOperation { Anchor = "Missing", Text = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Missing");
    }

    [Fact]
    public void InsertBefore_SecondOccurrence_FindsCorrectInstance()
    {
        var doc = Helpers.Doc("aaa bbb aaa ccc");

        var result = new InsertBeforeOperation { Anchor = "aaa", Text = "[2]", Occurrence = 2 }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("aaa bbb [2]aaa ccc");
    }

    [Fact]
    public void InsertBefore_CaseInsensitive_MatchesDespiteCaseDifference()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertBeforeOperation { Anchor = "WORLD", Text = "Beautiful ", CaseSensitive = false }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello Beautiful World");
    }

    // ── Append ────────────────────────────────────────────────────────────────

    [Fact]
    public void Append_HappyPath_AddsTextAtEnd()
    {
        var doc = Helpers.Doc("Hello");

        var result = new AppendOperation { Text = " World" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void Append_EmptyDocument_CreatesContent()
    {
        var doc = Helpers.Doc("");

        var result = new AppendOperation { Text = "New content" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("New content");
    }

    [Fact]
    public void Append_ReportsCharsInserted()
    {
        var doc = Helpers.Doc("Hi");

        var result = new AppendOperation { Text = "!!!" }.Execute(doc);

        result.CharsInserted.Should().Be(3);
    }

    // ── Prepend ───────────────────────────────────────────────────────────────

    [Fact]
    public void Prepend_HappyPath_AddsTextAtBeginning()
    {
        var doc = Helpers.Doc("World");

        var result = new PrependOperation { Text = "Hello " }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void Prepend_EmptyDocument_CreatesContent()
    {
        var doc = Helpers.Doc("");

        var result = new PrependOperation { Text = "Start" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Start");
    }

    // ── ReplaceSection ────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceSection_HappyPath_ReplacesContentBetweenAnchors()
    {
        var doc = Helpers.Doc("[START]old content[END]");

        var result = new ReplaceSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]",
            Text = "new content"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("[START]new content[END]");
    }

    [Fact]
    public void ReplaceSection_IncludeAnchors_ReplacesAnchorsToo()
    {
        var doc = Helpers.Doc("before [START]old content[END] after");

        var result = new ReplaceSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]",
            Text = "REPLACED",
            IncludeAnchors = true
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("before REPLACED after");
    }

    [Fact]
    public void ReplaceSection_ExcludeAnchors_PreservesAnchors()
    {
        var doc = Helpers.Doc("[START]old[END]");

        var result = new ReplaceSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]",
            Text = "new",
            IncludeAnchors = false
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("[START]new[END]");
    }

    [Fact]
    public void ReplaceSection_StartAnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[START]content[END]");

        var result = new ReplaceSectionOperation
        {
            StartAnchor = "[MISSING]",
            EndAnchor = "[END]",
            Text = "X"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MISSING");
    }

    [Fact]
    public void ReplaceSection_EndAnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[START]content[END]");

        var result = new ReplaceSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[MISSING]",
            Text = "X"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MISSING");
    }

    // ── DeleteSection ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSection_HappyPath_DeletesContentBetweenAnchors()
    {
        var doc = Helpers.Doc("[START]remove this[END]");

        var result = new DeleteSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("[START][END]");
    }

    [Fact]
    public void DeleteSection_IncludeAnchors_DeletesAnchorsToo()
    {
        var doc = Helpers.Doc("before [START]remove[END] after");

        var result = new DeleteSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]",
            IncludeAnchors = true
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("before  after");
    }

    [Fact]
    public void DeleteSection_AnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("some content");

        var result = new DeleteSectionOperation
        {
            StartAnchor = "[MISSING]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
    }
}
