using TextEditor.Core;
using TextEditor.Core.Navigation;
using Xunit;

namespace TextEditor.Tests;

public class BookmarkTests
{
    private static TextDocument MakeDoc(int lines = 5)
    {
        var doc = new TextDocument();
        doc.Load(string.Join("\n", Enumerable.Range(0, lines).Select(i => $"line {i}")));
        return doc;
    }

    // ── Toggle ──────────────────────────────────────────────────────────────

    [Fact]
    public void Toggle_AddsBookmark_ReturnsTrue()
    {
        var m = new BookmarkModel();
        Assert.True(m.Toggle(2));
        Assert.True(m.IsBookmarked(2));
    }

    [Fact]
    public void Toggle_RemovesExisting_ReturnsFalse()
    {
        var m = new BookmarkModel();
        m.Toggle(2);
        Assert.False(m.Toggle(2));
        Assert.False(m.IsBookmarked(2));
    }

    [Fact]
    public void Toggle_MultipleLines_AllTracked()
    {
        var m = new BookmarkModel();
        m.Toggle(0); m.Toggle(3); m.Toggle(7);
        Assert.Equal(new[] { 0, 3, 7 }, m.GetAll().ToArray());
    }

    [Fact]
    public void GetAll_ReturnsSortedAscending()
    {
        var m = new BookmarkModel();
        m.Toggle(9); m.Toggle(1); m.Toggle(5);
        Assert.Equal(new[] { 1, 5, 9 }, m.GetAll().ToArray());
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    [Fact]
    public void NextBookmark_ReturnsStrictlyAfterFromLine()
    {
        var m = new BookmarkModel();
        m.Toggle(1); m.Toggle(4); m.Toggle(8);
        Assert.Equal(4, m.NextBookmark(1));
        Assert.Equal(8, m.NextBookmark(4));
    }

    [Fact]
    public void NextBookmark_ReturnsNull_WhenNoneAfter()
    {
        var m = new BookmarkModel();
        m.Toggle(2);
        Assert.Null(m.NextBookmark(2));
        Assert.Null(m.NextBookmark(5));
    }

    [Fact]
    public void PrevBookmark_ReturnsStrictlyBeforeFromLine()
    {
        var m = new BookmarkModel();
        m.Toggle(1); m.Toggle(4); m.Toggle(8);
        Assert.Equal(4, m.PrevBookmark(8));
        Assert.Equal(1, m.PrevBookmark(4));
    }

    [Fact]
    public void PrevBookmark_ReturnsNull_WhenNoneBefore()
    {
        var m = new BookmarkModel();
        m.Toggle(5);
        Assert.Null(m.PrevBookmark(0));
        Assert.Null(m.PrevBookmark(5));
    }

    // ── Remapping on insert ─────────────────────────────────────────────────

    [Fact]
    public void OnInsert_ShiftsBookmarks_AtOrAfterInsertLine()
    {
        var m = new BookmarkModel();
        m.Toggle(2); m.Toggle(5);
        m.OnInsert(3, 2);  // 2 new lines inserted at line 3
        Assert.True(m.IsBookmarked(2));   // before insertion point → unchanged
        Assert.True(m.IsBookmarked(7));   // 5 + 2 = 7
    }

    [Fact]
    public void OnInsert_AtBookmarkedLine_ShiftsIt()
    {
        var m = new BookmarkModel();
        m.Toggle(3);
        m.OnInsert(3, 1);
        Assert.True(m.IsBookmarked(4));
        Assert.False(m.IsBookmarked(3));
    }

    [Fact]
    public void OnInsert_ZeroCount_NoChange()
    {
        var m = new BookmarkModel();
        m.Toggle(2);
        m.OnInsert(1, 0);
        Assert.True(m.IsBookmarked(2));
    }

    // ── Remapping on delete ─────────────────────────────────────────────────

    [Fact]
    public void OnDelete_RemovesCoveredBookmarks()
    {
        var m = new BookmarkModel();
        m.Toggle(2); m.Toggle(3); m.Toggle(6);
        m.OnDelete(2, 2);  // delete lines 2–3
        Assert.False(m.IsBookmarked(2));
        Assert.False(m.IsBookmarked(3));
        Assert.True(m.IsBookmarked(4));   // 6 - 2 = 4
    }

    [Fact]
    public void OnDelete_ShiftsBookmarksAfterRange()
    {
        var m = new BookmarkModel();
        m.Toggle(5);
        m.OnDelete(2, 2);  // delete lines 2–3
        Assert.True(m.IsBookmarked(3));   // 5 - 2 = 3
    }

    [Fact]
    public void OnDelete_ZeroCount_NoChange()
    {
        var m = new BookmarkModel();
        m.Toggle(3);
        m.OnDelete(1, 0);
        Assert.True(m.IsBookmarked(3));
    }

    // ── Events ──────────────────────────────────────────────────────────────

    [Fact]
    public void BookmarksChanged_FiredOnToggle()
    {
        int count = 0;
        var m = new BookmarkModel();
        m.BookmarksChanged += () => count++;
        m.Toggle(1);
        m.Toggle(1);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Clear_RemovesAll_FiresEvent()
    {
        int fired = 0;
        var m = new BookmarkModel();
        m.Toggle(1); m.Toggle(3);
        m.BookmarksChanged += () => fired++;
        m.Clear();
        Assert.Empty(m.GetAll());
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Clear_Empty_DoesNotFireEvent()
    {
        int fired = 0;
        var m = new BookmarkModel();
        m.BookmarksChanged += () => fired++;
        m.Clear();
        Assert.Equal(0, fired);
    }

    // ── TextDocument integration ─────────────────────────────────────────────

    [Fact]
    public void TextDocument_GetBookmarkModel_ReturnsSameInstance()
    {
        var doc = MakeDoc();
        var a = doc.GetBookmarkModel();
        var b = doc.GetBookmarkModel();
        Assert.Same(a, b);
    }

    [Fact]
    public void TextDocument_Load_ClearsBookmarks()
    {
        var doc = MakeDoc();
        doc.GetBookmarkModel().Toggle(1);
        doc.Load("new content");
        Assert.Empty(doc.GetBookmarkModel().GetAll());
    }

    [Fact]
    public void TextDocument_Insert_RemapsBookmark()
    {
        var doc = MakeDoc(5);
        doc.GetBookmarkModel().Toggle(3);
        doc.Insert(doc.PositionToOffset(1, 0), "inserted\n");
        Assert.True(doc.GetBookmarkModel().IsBookmarked(4));
        Assert.False(doc.GetBookmarkModel().IsBookmarked(3));
    }

    [Fact]
    public void TextDocument_Delete_RemovesAndShiftsBookmarks()
    {
        var doc = MakeDoc(6);
        doc.GetBookmarkModel().Toggle(2);
        doc.GetBookmarkModel().Toggle(4);
        // Delete line 2 (the line itself + newline)
        string line2 = doc.GetLine(2);
        doc.Delete(doc.PositionToOffset(2, 0), line2.Length + 1);
        Assert.False(doc.GetBookmarkModel().IsBookmarked(2)); // removed (covered)
        Assert.True(doc.GetBookmarkModel().IsBookmarked(3));  // 4 - 1 = 3
    }
}
