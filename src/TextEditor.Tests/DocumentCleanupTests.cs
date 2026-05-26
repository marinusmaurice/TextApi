using TextEditor.Core;
using TextEditor.Core.EOL;
using TextEditor.Core.Language;
using Xunit;

namespace TextEditor.Tests;

public class DocumentCleanupTests
{
    // ── TrimTrailingWhitespace ───────────────────────────────────────────────

    [Fact]
    public void Trim_RemovesTrailingSpaces()
    {
        var doc = new TextDocument();
        doc.Load("hello   \nworld  ");
        int count = DocumentCleanup.TrimTrailingWhitespace(doc);
        Assert.Equal(2, count);
        Assert.Equal("hello\nworld", doc.GetText());
    }

    [Fact]
    public void Trim_AlreadyClean_ReturnsZero_NoUndoEntry()
    {
        var doc = new TextDocument();
        doc.Load("hello\nworld");
        int count = DocumentCleanup.TrimTrailingWhitespace(doc);
        Assert.Equal(0, count);
        Assert.False(doc.CanUndo);
    }

    [Fact]
    public void Trim_IsUndoable_SingleStep()
    {
        var doc = new TextDocument();
        doc.Load("hello   \nworld  ");
        DocumentCleanup.TrimTrailingWhitespace(doc);
        doc.Undo();
        Assert.Equal("hello   \nworld  ", doc.GetText());
    }

    [Fact]
    public void Trim_PreservesLeadingWhitespace()
    {
        var doc = new TextDocument();
        doc.Load("  leading   ");
        DocumentCleanup.TrimTrailingWhitespace(doc);
        Assert.Equal("  leading", doc.GetText());
    }

    [Fact]
    public void Trim_RemovesTrailingTabs()
    {
        var doc = new TextDocument();
        doc.Load("hello\t\t");
        DocumentCleanup.TrimTrailingWhitespace(doc);
        Assert.Equal("hello", doc.GetText());
    }

    [Fact]
    public void Trim_PartialLines_OnlyDirtyLinesModified()
    {
        var doc = new TextDocument();
        doc.Load("clean\ndirty   \nclean");
        int count = DocumentCleanup.TrimTrailingWhitespace(doc);
        Assert.Equal(1, count);
        Assert.Equal("clean\ndirty\nclean", doc.GetText());
    }

    // ── NormalizeLineEndings ─────────────────────────────────────────────────

    [Fact]
    public void Normalize_SetsSaveStyle_Lf()
    {
        var doc = new TextDocument();
        doc.Load("a\nb");
        DocumentCleanup.NormalizeLineEndings(doc, "\n");
        Assert.Equal(EolStyle.Lf, doc.SaveEolStyle);
    }

    [Fact]
    public void Normalize_SetsSaveStyle_CrLf()
    {
        var doc = new TextDocument();
        doc.Load("a\nb");
        DocumentCleanup.NormalizeLineEndings(doc, "\r\n");
        Assert.Equal(EolStyle.CrLf, doc.SaveEolStyle);
    }

    [Fact]
    public void Normalize_RemovesStrayCarriageReturns()
    {
        var doc = new TextDocument();
        doc.Load("a\nb");
        doc.Insert(1, "\r");  // inject a stray \r
        DocumentCleanup.NormalizeLineEndings(doc, "\n");
        Assert.DoesNotContain('\r', doc.GetText());
    }

    [Fact]
    public void Normalize_CleanDocument_NoUndoEntry_WhenNoStrayR()
    {
        var doc = new TextDocument();
        doc.Load("a\nb\nc");
        DocumentCleanup.NormalizeLineEndings(doc, "\n");
        // Only SaveEolStyle changed (not an undoable doc op), so CanUndo is false
        Assert.False(doc.CanUndo);
    }
}
