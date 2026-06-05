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

public class OffsetOperationTests
{
    // ── InsertAt ─────────────────────────────────────────────────────────────

    [Fact]
    public void InsertAt_MiddleOfDocument_InsertsTextAtCorrectPosition()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new InsertAtOperation(5, " Beautiful").Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello Beautiful World");
    }

    [Fact]
    public void InsertAt_OffsetZero_InsertsAtBeginning()
    {
        var doc = Helpers.Doc("World");

        var result = new InsertAtOperation(0, "Hello ").Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void InsertAt_OffsetAtDocEnd_AppendsText()
    {
        var doc = Helpers.Doc("Hello");

        var result = new InsertAtOperation(5, " World").Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void InsertAt_NegativeOffset_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new InsertAtOperation(-1, "X").Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void InsertAt_OffsetBeyondLength_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new InsertAtOperation(100, "X").Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void InsertAt_ReportsCharsInserted()
    {
        var doc = Helpers.Doc("Hello");

        var result = new InsertAtOperation(5, " World").Execute(doc);

        result.CharsInserted.Should().Be(6);
        result.CharsDeleted.Should().Be(0);
    }

    [Fact]
    public void InsertAt_ApplyDrift_AdjustsOffset()
    {
        // op starts at offset 2; drift of +3 makes effective offset = 5
        var op = new InsertAtOperation(2, "X");
        op.ApplyDrift(3);

        var doc = Helpers.Doc("Hello World");
        op.Execute(doc);

        // "X" should be at index 5 in the result
        doc.GetText()[5].Should().Be('X');
    }

    // ── DeleteAt ─────────────────────────────────────────────────────────────

    [Fact]
    public void DeleteAt_MiddleOfDocument_DeletesCorrectRange()
    {
        var doc = Helpers.Doc("Hello Beautiful World");

        var result = new DeleteAtOperation(5, 10).Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact]
    public void DeleteAt_FromStart_DeletesFromBeginning()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new DeleteAtOperation(0, 6).Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("World");
    }

    [Fact]
    public void DeleteAt_NegativeOffset_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new DeleteAtOperation(-1, 2).Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DeleteAt_RangeExceedsLength_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new DeleteAtOperation(3, 100).Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void DeleteAt_ReportsCharsDeleted()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new DeleteAtOperation(5, 6).Execute(doc);

        result.CharsDeleted.Should().Be(6);
        result.CharsInserted.Should().Be(0);
    }

    [Fact]
    public void DeleteAt_ApplyDrift_AdjustsOffset()
    {
        // op starts at offset 8; drift of +3 makes effective offset = 11
        var op = new DeleteAtOperation(8, 3);
        op.ApplyDrift(3);

        // "Hello World!!!" → delete 3 chars at offset 11 ("!") gives "Hello WorldX!!"
        // Actually offset 11 = 'd', 'l', 'd' in "Hello World!!!" → after delete: "Hello Wor!!!"
        // Wait: "Hello World!!!" is 14 chars, offset 11 = '!', delete 3 → "Hello World"
        var doc = Helpers.Doc("Hello World!!!");
        var result = op.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello World");
    }

    // ── ReplaceAt ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAt_MiddleOfDocument_ReplacesCorrectRange()
    {
        var doc = Helpers.Doc("Hello Old World");

        var result = new ReplaceAtOperation(6, 3, "New").Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Hello New World");
    }

    [Fact]
    public void ReplaceAt_AtStart_ReplacesBeginning()
    {
        var doc = Helpers.Doc("Bad start here");

        var result = new ReplaceAtOperation(0, 3, "Good").Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("Good start here");
    }

    [Fact]
    public void ReplaceAt_NegativeOffset_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new ReplaceAtOperation(-1, 2, "X").Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReplaceAt_RangeExceedsLength_ReturnsFail()
    {
        var doc = Helpers.Doc("Hello");

        var result = new ReplaceAtOperation(3, 100, "X").Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ReplaceAt_ReportsCharsInsertedAndDeleted()
    {
        var doc = Helpers.Doc("Hello World");

        var result = new ReplaceAtOperation(6, 5, "Everyone").Execute(doc);

        result.CharsInserted.Should().Be(8);
        result.CharsDeleted.Should().Be(5);
        result.DeltaChars.Should().Be(3);
    }

    [Fact]
    public void ReplaceAt_ApplyDrift_AdjustsOffset()
    {
        var op = new ReplaceAtOperation(6, 5, "Everyone");
        op.ApplyDrift(2);

        var doc = Helpers.Doc("Hello  World extra");
        var result = op.Execute(doc);

        result.Success.Should().BeTrue();
    }
}
