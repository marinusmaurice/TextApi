using TextEditor.Core;
using TextEditor.Core.Language;
using Xunit;

namespace TextEditor.Tests;

public class LineCommentTests
{
    private static TextDocument Make(string content)
    {
        var doc = new TextDocument();
        doc.Load(content);
        return doc;
    }

    [Fact]
    public void Comment_SingleLine_AddsPrefix()
    {
        var doc = Make("hello");
        LineCommentToggle.Toggle(doc, 0, 0);
        Assert.Equal("// hello", doc.GetLine(0));
    }

    [Fact]
    public void Uncomment_SingleCommentedLine_RemovesPrefix()
    {
        var doc = Make("// hello");
        LineCommentToggle.Toggle(doc, 0, 0);
        Assert.Equal("hello", doc.GetLine(0));
    }

    [Fact]
    public void Comment_MultipleLines_AllCommented()
    {
        var doc = Make("aaa\nbbb\nccc");
        LineCommentToggle.Toggle(doc, 0, 2);
        Assert.Equal("// aaa", doc.GetLine(0));
        Assert.Equal("// bbb", doc.GetLine(1));
        Assert.Equal("// ccc", doc.GetLine(2));
    }

    [Fact]
    public void Uncomment_AllCommented_RemovesAll()
    {
        var doc = Make("// aaa\n// bbb\n// ccc");
        LineCommentToggle.Toggle(doc, 0, 2);
        Assert.Equal("aaa", doc.GetLine(0));
        Assert.Equal("bbb", doc.GetLine(1));
        Assert.Equal("ccc", doc.GetLine(2));
    }

    [Fact]
    public void Mixed_OneUncommented_CommentsAll()
    {
        var doc = Make("// aaa\nbbb");
        LineCommentToggle.Toggle(doc, 0, 1);
        // Both should be commented (since not ALL were commented)
        Assert.Equal("// // aaa", doc.GetLine(0));
        Assert.Equal("// bbb", doc.GetLine(1));
    }

    [Fact]
    public void Comment_SkipsEmptyLines()
    {
        var doc = Make("aaa\n\nccc");
        LineCommentToggle.Toggle(doc, 0, 2);
        Assert.Equal("// aaa", doc.GetLine(0));
        Assert.Equal("", doc.GetLine(1));       // empty line unchanged
        Assert.Equal("// ccc", doc.GetLine(2));
    }

    [Fact]
    public void Comment_IsUndoable_SingleStep()
    {
        var doc = Make("aaa\nbbb");
        LineCommentToggle.Toggle(doc, 0, 1);
        doc.Undo();
        Assert.Equal("aaa", doc.GetLine(0));
        Assert.Equal("bbb", doc.GetLine(1));
    }

    [Fact]
    public void Comment_RespectsIndentation_UsesMinIndent()
    {
        // Minimum indent is 4. "// " is inserted at col 4 in both lines.
        // "    aaa"     → "    // aaa"
        // "        bbb" → "    // " inserted at col 4 → "    //     bbb"
        var doc = Make("    aaa\n        bbb");
        LineCommentToggle.Toggle(doc, 0, 1);
        Assert.Equal("    // aaa", doc.GetLine(0));
        Assert.Equal("    //     bbb", doc.GetLine(1));
    }

    [Fact]
    public void CustomPrefix_Hash()
    {
        var doc = Make("print('hi')");
        LineCommentToggle.Toggle(doc, 0, 0, "#");
        Assert.Equal("# print('hi')", doc.GetLine(0));
        LineCommentToggle.Toggle(doc, 0, 0, "#");
        Assert.Equal("print('hi')", doc.GetLine(0));
    }

    [Fact]
    public void Uncomment_WithoutTrailingSpace_StillRemovesPrefix()
    {
        var doc = Make("//hello");
        LineCommentToggle.Toggle(doc, 0, 0);
        Assert.Equal("hello", doc.GetLine(0));
    }
}
