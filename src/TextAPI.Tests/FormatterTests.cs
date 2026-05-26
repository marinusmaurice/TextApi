using TextAPI.Core;
using TextAPI.Core.Formatting;
using Xunit;

namespace TextAPI.Tests;

file sealed class UpperFormatter : IDocumentFormatter
{
    public string Format(string text) => text.ToUpperInvariant();
}

public class FormatterTests
{
    [Fact]
    public void Format_FullDocument_ReplacesContent()
    {
        var doc = new TextDocument();
        doc.Load("hello\nworld");
        doc.Format(new UpperFormatter());
        Assert.Equal("HELLO\nWORLD", doc.GetText());
    }

    [Fact]
    public void Format_SameContent_NoUndoEntry()
    {
        var doc = new TextDocument();
        doc.Load("ALREADY");
        doc.Format(new UpperFormatter());
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Format_IsUndoable_SingleStep()
    {
        var doc = new TextDocument();
        doc.Load("hello");
        doc.Format(new UpperFormatter());
        Assert.Equal("HELLO", doc.GetText());
        doc.Undo();
        Assert.Equal("hello", doc.GetText());
    }

    [Fact]
    public void Format_RangeSubset_OnlyFormatsSpecifiedLines()
    {
        var doc = new TextDocument();
        doc.Load("aaa\nbbb\nccc");
        doc.Format(new UpperFormatter(), startLine: 1, endLine: 1);
        Assert.Equal("aaa\nBBB\nccc", doc.GetText());
    }

    [Fact]
    public void Format_FirstLineOnly()
    {
        var doc = new TextDocument();
        doc.Load("aaa\nbbb\nccc");
        doc.Format(new UpperFormatter(), startLine: 0, endLine: 0);
        Assert.Equal("AAA\nbbb\nccc", doc.GetText());
    }

    [Fact]
    public void Format_LastLineOnly()
    {
        var doc = new TextDocument();
        doc.Load("aaa\nbbb\nccc");
        doc.Format(new UpperFormatter(), startLine: 2, endLine: 2);
        Assert.Equal("aaa\nbbb\nCCC", doc.GetText());
    }

    [Fact]
    public void Format_NullEndLine_ExtendsToEndOfDoc()
    {
        var doc = new TextDocument();
        doc.Load("aaa\nbbb\nccc");
        doc.Format(new UpperFormatter(), startLine: 1);
        Assert.Equal("aaa\nBBB\nCCC", doc.GetText());
    }
}
