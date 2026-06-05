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

public class QueryOperationTests
{
    // ── FindAll ───────────────────────────────────────────────────────────────

    [Fact]
    public void FindAll_LiteralPattern_ReturnsAllMatchStrings()
    {
        var doc = Helpers.Doc("cat and cat and dog");

        var result = new FindAllOperation { Pattern = "cat" }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(2);
        result.FoundMatches.Should().HaveCount(2);
        result.FoundMatches.Should().AllBe("cat");
    }

    [Fact]
    public void FindAll_ZeroResults_SucceedsWithEmptyMatches()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new FindAllOperation { Pattern = "xyz" }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(0);
        result.FoundMatches.Should().BeEmpty();
    }

    [Fact]
    public void FindAll_CaseInsensitive_MatchesVariousCase()
    {
        var doc = Helpers.Doc("Hello hello HELLO");

        var result = new FindAllOperation { Pattern = "hello", CaseSensitive = false }.Execute(doc);

        result.MatchCount.Should().Be(3);
    }

    [Fact]
    public void FindAll_RegexPattern_ReturnsMatchedText()
    {
        var doc = Helpers.Doc("price: $42.50 and $100.00");

        var result = new FindAllOperation { Pattern = @"\$\d+\.\d+", UseRegex = true }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(2);
        result.FoundMatches.Should().Contain("$42.50");
        result.FoundMatches.Should().Contain("$100.00");
    }

    [Fact]
    public void FindAll_DoesNotMutateDocument()
    {
        var doc = Helpers.Doc("Hello World");
        var textBefore = doc.GetText();

        new FindAllOperation { Pattern = "Hello" }.Execute(doc);

        doc.GetText().Should().Be(textBefore);
    }

    // ── ExtractSection ────────────────────────────────────────────────────────

    [Fact]
    public void ExtractSection_HappyPath_ReturnsTextBetweenAnchors()
    {
        var doc = Helpers.Doc("[START]extracted content[END]");

        var result = new ExtractSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        result.ExtractedText.Should().Be("extracted content");
    }

    [Fact]
    public void ExtractSection_IncludeAnchors_ReturnsAnchorsInText()
    {
        var doc = Helpers.Doc("[START]content[END]");

        var result = new ExtractSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[END]",
            IncludeAnchors = true
        }.Execute(doc);

        result.Success.Should().BeTrue();
        result.ExtractedText.Should().Be("[START]content[END]");
    }

    [Fact]
    public void ExtractSection_StartAnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[START]content[END]");

        var result = new ExtractSectionOperation
        {
            StartAnchor = "[MISSING]",
            EndAnchor = "[END]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MISSING");
    }

    [Fact]
    public void ExtractSection_EndAnchorNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[START]content[END]");

        var result = new ExtractSectionOperation
        {
            StartAnchor = "[START]",
            EndAnchor = "[MISSING]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void ExtractSection_DoesNotMutateDocument()
    {
        var doc = Helpers.Doc("[START]content[END]");
        var textBefore = doc.GetText();

        new ExtractSectionOperation { StartAnchor = "[START]", EndAnchor = "[END]" }.Execute(doc);

        doc.GetText().Should().Be(textBefore);
    }

    // ── Contains ──────────────────────────────────────────────────────────────

    [Fact]
    public void Contains_PatternFound_SucceedsWithMatchCountOne()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ContainsOperation { Pattern = "World" }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Contains_PatternNotFound_SucceedsWithMatchCountZero()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ContainsOperation { Pattern = "missing" }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(0);
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Contains_CaseInsensitive_FindsPattern()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ContainsOperation { Pattern = "WORLD", CaseSensitive = false }.Execute(doc);

        result.MatchCount.Should().Be(1);
    }
}
