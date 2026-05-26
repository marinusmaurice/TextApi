using TextEditor.Core;
using TextEditor.Core.Cursor;
using Xunit;

namespace TextEditor.Tests;

public class ColumnSelectionTests
{
    private static (TextDocument doc, MultiCursor mc) Make(string content = "aaaa\nbbbb\ncccc\ndddd")
    {
        var doc = new TextDocument();
        doc.Load(content);
        var mc = new MultiCursor(doc);
        return (doc, mc);
    }

    [Fact]
    public void AddColumnSelection_CreatesOneCursorPerLine()
    {
        var (_, mc) = Make();
        mc.AddColumnSelection(0, 3, 2);
        Assert.Equal(4, mc.Count);
    }

    [Fact]
    public void AddColumnSelection_CursorsAtCorrectColumn()
    {
        var (doc, mc) = Make();
        mc.AddColumnSelection(0, 2, 2);
        foreach (var c in mc.All)
        {
            var (_, col) = doc.OffsetToPosition(c.CaretOffset);
            Assert.Equal(2, col);
        }
    }

    [Fact]
    public void AddColumnSelection_ClampsColumnToLineLength()
    {
        var doc = new TextDocument();
        doc.Load("ab\nlong line here\nxy");
        var mc = new MultiCursor(doc);
        mc.AddColumnSelection(0, 2, 99);
        var (_, col0) = doc.OffsetToPosition(mc.All[0].CaretOffset);
        var (_, col2) = doc.OffsetToPosition(mc.All[2].CaretOffset);
        Assert.Equal(2, col0);   // "ab" has length 2
        Assert.Equal(2, col2);   // "xy" has length 2
    }

    [Fact]
    public void AddColumnSelection_ReplacesAllExistingCursors()
    {
        var (doc, mc) = Make();
        mc.AddCursor(0);
        mc.AddCursor(5);
        mc.AddColumnSelection(1, 2, 1);
        Assert.Equal(2, mc.Count);
    }

    [Fact]
    public void AddColumnSelection_InsertText_WritesToAllLines()
    {
        var (doc, mc) = Make();
        mc.AddColumnSelection(0, 3, 0);
        mc.InsertText("> ");
        Assert.Equal("> aaaa", doc.GetLine(0));
        Assert.Equal("> bbbb", doc.GetLine(1));
        Assert.Equal("> cccc", doc.GetLine(2));
        Assert.Equal("> dddd", doc.GetLine(3));
    }

    [Fact]
    public void AddColumnSelection_SingleLine_ResultsInOneCursor()
    {
        var (_, mc) = Make();
        mc.AddColumnSelection(2, 2, 1);
        Assert.Equal(1, mc.Count);
    }

    [Fact]
    public void AddColumnSelection_ColumnZero_AtLineStart()
    {
        var (doc, mc) = Make();
        mc.AddColumnSelection(0, 1, 0);
        foreach (var c in mc.All)
        {
            var (_, col) = doc.OffsetToPosition(c.CaretOffset);
            Assert.Equal(0, col);
        }
    }

    [Fact]
    public void AddColumnSelection_InsertIsUndoable_SingleStep()
    {
        var (doc, mc) = Make();
        mc.AddColumnSelection(0, 3, 0);
        mc.InsertText("X");
        doc.Undo();
        Assert.Equal("aaaa", doc.GetLine(0));
        Assert.Equal("bbbb", doc.GetLine(1));
    }
}
