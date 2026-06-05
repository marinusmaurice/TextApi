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

public class PatternOperationTests
{
    // ── ReplaceAll ────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_BasicReplace_ReplacesAllOccurrences()
    {
        var doc = Helpers.Doc("cat and cat and cat");

        var result = new ReplaceAllOperation { Find = "cat", Replace = "dog" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("dog and dog and dog");
        result.MatchCount.Should().Be(3);
    }

    [Fact]
    public void ReplaceAll_CaseInsensitive_MatchesIgnoringCase()
    {
        var doc = Helpers.Doc("Hello hello HELLO");

        var result = new ReplaceAllOperation { Find = "hello", Replace = "hi", CaseSensitive = false }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hi hi hi");
        result.MatchCount.Should().Be(3);
    }

    [Fact]
    public void ReplaceAll_WholeWord_OnlyMatchesWholeWords()
    {
        var doc = Helpers.Doc("cat concatenate scat cat");

        var result = new ReplaceAllOperation { Find = "cat", Replace = "dog", WholeWord = true }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("dog concatenate scat dog");
        result.MatchCount.Should().Be(2);
    }

    [Fact]
    public void ReplaceAll_ZeroMatches_SucceedsWithMatchCountZero()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ReplaceAllOperation { Find = "xyz", Replace = "abc" }.Execute(doc);

        result.Success.Should().BeTrue();
        result.MatchCount.Should().Be(0);
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void ReplaceAll_EmptyFind_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ReplaceAllOperation { Find = "", Replace = "abc" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReplaceAll_ReplaceWithEmpty_EffectivelyDeletesMatches()
    {
        var doc = Helpers.Doc("Hello  World");

        var result = new ReplaceAllOperation { Find = "  ", Replace = " " }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    // ── RegexReplace ──────────────────────────────────────────────────────────

    [Fact]
    public void RegexReplace_BasicPattern_ReplacesMatches()
    {
        var doc = Helpers.Doc("2020-01-15 and 2021-12-01");

        var result = new RegexReplaceOperation { Pattern = @"\d{4}-\d{2}-\d{2}", Replacement = "DATE" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("DATE and DATE");
    }

    [Fact]
    public void RegexReplace_CaptureGroupsInReplacement_SubstitutesGroups()
    {
        var doc = Helpers.Doc("John Smith, Jane Doe");

        var result = new RegexReplaceOperation
        {
            Pattern = @"(\w+) (\w+)",
            Replacement = "$2, $1"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Smith, John, Doe, Jane");
    }

    [Fact]
    public void RegexReplace_InvalidPattern_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new RegexReplaceOperation { Pattern = @"[invalid(", Replacement = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("regex");
    }

    [Fact]
    public void RegexReplace_EmptyPattern_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new RegexReplaceOperation { Pattern = "", Replacement = "X" }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void RegexReplace_PatternWithWordBoundary_MatchesCorrectly()
    {
        var doc = Helpers.Doc("test testing tested");

        var result = new RegexReplaceOperation { Pattern = @"\btest\b", Replacement = "exam" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("exam testing tested");
    }

    [Fact]
    public void RegexReplace_ZeroMatches_SucceedsWithDocumentUnchanged()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new RegexReplaceOperation { Pattern = @"\d+", Replacement = "NUM" }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void RegexReplace_ReportsMatchCount()
    {
        var doc = Helpers.Doc("abc 123 def 456");

        var result = new RegexReplaceOperation { Pattern = @"\d+", Replacement = "N" }.Execute(doc);

        result.MatchCount.Should().Be(2);
    }
}
