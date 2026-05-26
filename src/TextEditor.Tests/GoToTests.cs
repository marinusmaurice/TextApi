using TextEditor.Core;
using Xunit;

namespace TextEditor.Tests;

public class GoToTests
{
    private static TextDocument MakeDoc()
    {
        var doc = new TextDocument();
        doc.Load("line 0\nline 1\nline 2\nline 3");
        return doc;
    }

    [Fact]
    public void GoTo_PushesEntryIntoCursorHistory()
    {
        var doc = MakeDoc();
        doc.GoTo(2, 0);
        var entry = doc.GetCursorHistory().Current;
        Assert.NotNull(entry);
        Assert.Equal(doc.PositionToOffset(2, 0), entry.Value.Offset);
    }

    [Fact]
    public void GoTo_CorrectLine_CorrectColumn()
    {
        var doc = MakeDoc();
        doc.GoTo(1, 3);
        var pos = doc.OffsetToPosition(doc.GetCursorHistory().Current!.Value.Offset);
        Assert.Equal(1, pos.Line);
        Assert.Equal(3, pos.Column);
    }

    [Fact]
    public void GoTo_ClampsNegativeLine_ToZero()
    {
        var doc = MakeDoc();
        doc.GoTo(-5, 0);
        var pos = doc.OffsetToPosition(doc.GetCursorHistory().Current!.Value.Offset);
        Assert.Equal(0, pos.Line);
    }

    [Fact]
    public void GoTo_ClampsLineAboveMax()
    {
        var doc = MakeDoc();
        doc.GoTo(999, 0);
        var pos = doc.OffsetToPosition(doc.GetCursorHistory().Current!.Value.Offset);
        Assert.Equal(doc.LineCount - 1, pos.Line);
    }

    [Fact]
    public void GoTo_ClampsColumnAboveLineLength()
    {
        var doc = MakeDoc();
        doc.GoTo(0, 999);
        var pos = doc.OffsetToPosition(doc.GetCursorHistory().Current!.Value.Offset);
        Assert.Equal(0, pos.Line);
        Assert.Equal(doc.GetLine(0).Length, pos.Column);
    }

    [Fact]
    public void GoTo_MultipleCalls_NavigableWithBack()
    {
        var doc = MakeDoc();
        doc.GoTo(0, 0);
        doc.GoTo(2, 0);
        doc.GoTo(3, 0);

        var hist = doc.GetCursorHistory();
        Assert.Equal(3, doc.OffsetToPosition(hist.Current!.Value.Offset).Line);

        var back = hist.Back();
        Assert.Equal(2, doc.OffsetToPosition(back!.Value.Offset).Line);
    }

    [Fact]
    public void GoTo_WithFilePath_StoresIt()
    {
        var doc = MakeDoc();
        doc.GoTo(1, 0, "foo.cs");
        Assert.Equal("foo.cs", doc.GetCursorHistory().Current!.Value.FilePath);
    }

    [Fact]
    public void GoTo_NullFilePath_UsesDocumentFilePath()
    {
        var doc = new TextDocument();
        doc.Load("hello", "myfile.cs");
        doc.GoTo(0, 0);
        Assert.Equal("myfile.cs", doc.GetCursorHistory().Current!.Value.FilePath);
    }
}
