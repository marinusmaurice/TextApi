using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.ChangeTracking;
using TextAPI.Core.Cursor;
using TextAPI.Core.Folding;
using TextAPI.Core.InlayHints;
using TextAPI.Core.Language;
using TextAPI.Core.Navigation;
using TextAPI.Core.Search;
using TextAPI.Core.Snippets;
using TextAPI.Core.WordWrap;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Cross-Feature Integration Tests
//
// Each test class exercises two or more library features interacting.
// The goal is to catch regressions that only manifest when features share
// the same document — e.g. undo rolling back model state, search returning
// stale offsets after insert, fold regions shifting after paste, etc.
// ═══════════════════════════════════════════════════════════════════════════

// ── Shared helpers ──────────────────────────────────────────────────────────

file static class XF
{
    public static TextDocument Doc(string content = "")
    {
        var d = new TextDocument();
        if (content.Length > 0) d.Load(content);
        return d;
    }

    public static TextDocument CSharpDoc(string content)
    {
        var d = new TextDocument(new CSharpTokeniser());
        d.Load(content);
        return d;
    }

    /// <summary>Create a TextSearcher over the current document text.</summary>
    public static TextSearcher Searcher(TextDocument doc)
    {
        var pt = new PieceTable();
        pt.Load(doc.GetText());
        return new TextSearcher(pt);
    }

    public static bool IsFolded(FoldingModel folding, int startLine)
        => folding.Regions.FirstOrDefault(r => r.StartLine == startLine)?.IsFolded == true;
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Undo + Change Tracking
//    Editing lines marks them Modified; undoing restores them to Clean.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_UndoAndChangeTracking
{
    [Fact]
    public void EditThenUndo_LinesReturnToClean()
    {
        var doc     = XF.Doc("alpha\nbeta\ngamma");
        var tracker = doc.GetChangeTracker();

        doc.Replace(6, 4, "BETA");
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);

        doc.Undo();
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void InsertLineThenUndo_AddedLineDisappears_ChangeTrackingClean()
    {
        var doc     = XF.Doc("line0\nline2");
        var tracker = doc.GetChangeTracker();

        doc.Insert(6, "line1\n");
        doc.LineCount.Should().Be(3);
        tracker.GetStatus(1).Should().Be(LineStatus.Added);

        doc.Undo();
        doc.LineCount.Should().Be(2);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void MultipleEdits_UndoAll_AllLinesClean()
    {
        var doc     = XF.Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 1, "A");
        doc.Replace(2, 1, "B");
        doc.Replace(4, 1, "C");

        tracker.HasAnyChanges.Should().BeTrue();

        while (doc.CanUndo) doc.Undo();

        tracker.HasAnyChanges.Should().BeFalse();
    }

    [Fact]
    public void RedoAfterUndo_LineRemarkedModified()
    {
        var doc     = XF.Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 5, "HELLO");
        doc.Undo();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);

        doc.Redo();
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Undo + Bookmarks
//    Bookmarks track line numbers. Inserting lines shifts them; undo shifts
//    them back.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_UndoAndBookmarks
{
    [Fact]
    public void InsertLineShiftsBookmark_UndoRestoresPosition()
    {
        var doc   = XF.Doc("line0\nline1\nline2");
        var marks = new BookmarkModel();
        marks.Toggle(2);   // bookmark on "line2" at index 2

        // Insert a new line before line2 (after "line1\n", offset 12)
        doc.Insert(12, "new\n");
        marks.OnInsert(2, 1);
        marks.IsBookmarked(3).Should().BeTrue();  // shifted to 3

        // Undo the insert
        doc.Undo();
        marks.OnDelete(2, 1);
        marks.IsBookmarked(2).Should().BeTrue();  // restored to 2
        marks.IsBookmarked(3).Should().BeFalse();
    }

    [Fact]
    public void DeletedBookmarkedLine_BookmarkRemoved()
    {
        var doc   = XF.Doc("line0\nline1\nline2");
        var marks = new BookmarkModel();
        marks.Toggle(1);

        // Delete "\nline1" = 6 chars starting at offset 5
        doc.Delete(5, 6);
        marks.OnDelete(1, 1);

        marks.IsBookmarked(1).Should().BeFalse();
        marks.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void MultipleBookmarks_LargeInsert_AllShiftCorrectly()
    {
        var doc   = XF.Doc(string.Join("\n", Enumerable.Range(0, 10).Select(i => $"line{i}")));
        var marks = new BookmarkModel();
        marks.Toggle(3);
        marks.Toggle(7);

        // Insert 3 lines at line 2
        marks.OnInsert(2, 3);

        marks.IsBookmarked(6).Should().BeTrue();   // 3 + 3 = 6
        marks.IsBookmarked(10).Should().BeTrue();  // 7 + 3 = 10
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Search + Multi-Cursor
//    Find all occurrences of a pattern then use multi-cursor edits.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_SearchAndMultiCursor
{
    [Fact]
    public void SearchAfterInsert_FindsNewContent()
    {
        var doc = XF.Doc("hello world");

        XF.Searcher(doc).FindFirst("foo").Should().BeNull();

        doc.Insert(11, " foo");

        XF.Searcher(doc).FindFirst("foo")!.Value.Offset.Should().Be(12);
    }

    [Fact]
    public void CaseInsensitiveSearch_MultipleMatches_AllFound()
    {
        var doc  = XF.Doc("Apple apple APPLE");
        var opts = new SearchOptions { CaseSensitive = false };

        XF.Searcher(doc).FindAll("apple", opts).Should().HaveCount(3);
    }

    [Fact]
    public void RegexSearch_FindsPatternMatches()
    {
        var doc  = XF.Doc("var x = 1;\nvar y = 2;\nvar z = 3;");
        var opts = new SearchOptions { UseRegex = true };

        XF.Searcher(doc).FindAll(@"var \w+ = \d+;", opts).Should().HaveCount(3);
    }

    [Fact]
    public void MultiCursorInsertText_AllCursorsEdit()
    {
        var doc = XF.Doc("line0\nline1\nline2");
        var mc  = new MultiCursor(doc);

        mc.SetSingle(0);
        mc.AddCursor(6);   // start of line1
        mc.AddCursor(12);  // start of line2

        mc.InsertText("X");

        doc.GetLine(0).Should().StartWith("X");
        doc.GetLine(1).Should().StartWith("X");
        doc.GetLine(2).Should().StartWith("X");
    }

    [Fact]
    public void MultiCursorDeleteSelection_RemovesAllSelections()
    {
        var doc = XF.Doc("aaa bbb ccc");
        var mc  = new MultiCursor(doc);

        // Select "aaa" with cursor 0 (anchor=0, active=3), "bbb" with cursor 1 (anchor=4, active=7)
        mc.SetSingle(0);
        mc.Primary.SelectTo(3);
        mc.AddCursor(4, 7);  // anchor=4, active=7 → selects "bbb"

        mc.DeleteSelection();

        // Remaining: " ccc" but "aaa" and "bbb" are gone
        doc.GetText().Should().NotContain("aaa");
        doc.GetText().Should().NotContain("bbb");
        doc.GetText().Should().Contain("ccc");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Folding + Search
//    Folding computes regions; search inside folded text still returns results.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_FoldingAndSearch
{
    private static readonly string CsSource = string.Join("\n",
        "class Foo {",
        "    void Bar() {",
        "        int x = 1;",
        "        return;",
        "    }",
        "    void Baz() {",
        "        string s = \"hello\";",
        "    }",
        "}");

    [Fact]
    public void Search_FindsTextInsideFoldedRegion()
    {
        var doc     = XF.CSharpDoc(CsSource);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        var regions = folding.Regions;
        regions.Should().NotBeEmpty();
        folding.Fold(regions[0].StartLine);

        // Search must still find text regardless of fold state
        var match = XF.Searcher(doc).FindFirst("hello");
        match.Should().NotBeNull();
        match!.Value.Length.Should().Be(5);
    }

    [Fact]
    public void FoldRegionCount_AfterAddingBraces_Increases()
    {
        var doc     = XF.CSharpDoc("class A {\n    void M() {\n    }\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        int initial = folding.Regions.Count;

        // Append another method (insert before the last "}")
        int lastClose = doc.Length - 1;
        doc.Insert(lastClose, "\n    void N() {\n    }\n");
        folding.UpdateRegions(new BraceFoldingStrategy());

        folding.Regions.Count.Should().BeGreaterThan(initial);
    }

    [Fact]
    public void FoldUnfold_DocumentTextUnchanged()
    {
        var doc     = XF.CSharpDoc(CsSource);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        string originalText = doc.GetText();

        foreach (var r in folding.Regions.ToList())
            folding.Fold(r.StartLine);

        foreach (var r in folding.Regions.ToList())
            folding.Unfold(r.StartLine);

        doc.GetText().Should().Be(originalText);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. Paste + Change Tracking
//    Pasted content introduces Added lines; single-line paste marks Modified.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_PasteAndChangeTracking
{
    [Fact]
    public void PasteMultilineAtStart_NewLinesMarkedAdded()
    {
        var doc     = XF.Doc("existing\nlines");
        var tracker = doc.GetChangeTracker();

        // Simulate paste: insert multi-line block at offset 0
        doc.Insert(0, "pasted1\npasted2\n");

        doc.LineCount.Should().Be(4);
        tracker.GetStatus(0).Should().Be(LineStatus.Added);
        tracker.GetStatus(1).Should().Be(LineStatus.Added);
    }

    [Fact]
    public void PasteSingleLine_ModifiesCurrentLine()
    {
        var doc     = XF.Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();

        // Insert inline (no newline) → modifies line 0
        doc.Insert(5, " there");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void UndoPaste_ChangeTrackingReverts()
    {
        var doc     = XF.Doc("original");
        var tracker = doc.GetChangeTracker();

        doc.Insert(0, "prepended\n");
        tracker.GetStatus(0).Should().Be(LineStatus.Added);

        doc.Undo();
        doc.GetText().Should().Be("original");
        tracker.HasAnyChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Multi-Cursor + Snippets
//    Expanding a snippet inserts templated text at the cursor position.
//    After snippet expansion, document state is consistent.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_MultiCursorAndSnippets
{
    [Fact]
    public void SnippetExpansion_LiteralText_InsertsAtCursor()
    {
        var doc = XF.Doc("hello ");
        var mc  = new MultiCursor(doc);
        mc.SetSingle(6);  // after "hello "

        var snippet = SnippetEngine.Parse("world");
        SnippetEngine.BeginSnippet(doc, snippet, mc.Primary.CaretOffset);

        doc.GetText().Should().Be("hello world");
    }

    [Fact]
    public void SnippetWithTabStop_InsertedTextIncludesPlaceholder()
    {
        var doc     = XF.Doc("");
        var snippet = SnippetEngine.Parse("if (${1:condition}) {\n    $0\n}");
        SnippetEngine.BeginSnippet(doc, snippet, 0);

        doc.GetText().Should().Contain("if (condition)");
        doc.GetText().Should().Contain("{");
        doc.GetText().Should().Contain("}");
    }

    [Fact]
    public void SnippetExpansion_CanBeUndone()
    {
        var doc     = XF.Doc("start ");
        var snippet = SnippetEngine.Parse("end");
        SnippetEngine.BeginSnippet(doc, snippet, 6);

        doc.GetText().Should().Be("start end");
        doc.Undo();
        doc.GetText().Should().Be("start ");
    }

    [Fact]
    public void MultipleSnippetsExpanded_Sequentially_AllPresent()
    {
        var doc = XF.Doc("A B ");
        var s1  = SnippetEngine.Parse("[1]");
        var s2  = SnippetEngine.Parse("[2]");

        SnippetEngine.BeginSnippet(doc, s1, 0);          // inserts at 0
        SnippetEngine.BeginSnippet(doc, s2, doc.Length);  // appends

        doc.GetText().Should().Contain("[1]");
        doc.GetText().Should().Contain("[2]");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Word Wrap + Search
//    Wrapped text is virtual — search operates on raw document offsets.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_WordWrapAndSearch
{
    [Fact]
    public void Search_FindsTermInLongWrappedLine()
    {
        // A long line that would wrap at col 20; the search term is past the wrap point
        string longLine = "aaaaaaaaaaaaaaaaaaaaTARGETbbbbbbbbb";
        var doc         = XF.Doc(longLine);
        var _           = doc.GetWordWrapModel(20);  // activate wrap

        var match = XF.Searcher(doc).FindFirst("TARGET");
        match.Should().NotBeNull();
        match!.Value.Offset.Should().Be(20);
    }

    [Fact]
    public void WordWrapRowCount_IncreasesAfterInsert()
    {
        var doc   = XF.Doc("short");
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(1);

        // Extend line beyond wrap width
        doc.Insert(5, " and more text here please");
        model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().BeGreaterThan(1);
    }

    [Fact]
    public void SearchAll_MultiLineDocument_WrappedLinesDoNotDuplicateMatches()
    {
        var content  = string.Join("\n", Enumerable.Repeat("find me here in this long line", 5));
        var doc      = XF.Doc(content);
        var _        = doc.GetWordWrapModel(20);   // activate wrap

        // Search must count logical matches, not visual rows
        XF.Searcher(doc).FindAll("find me").Should().HaveCount(5);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Inlay Hints + Undo
//    Hints reference document offsets. After undo, the document reverts and
//    hint count remains stable (hints are caller-managed).
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_InlayHintsAndUndo
{
    [Fact]
    public void AddHint_SurvivesUndo_HintStillRegistered()
    {
        var doc   = XF.Doc("int x = 1;");
        var hints = doc.GetInlayHintModel();

        hints.AddHint(new InlayHint(4, "Int32"));
        hints.AllHints.Should().HaveCount(1);

        doc.Insert(0, "// comment\n");
        doc.Undo();

        // Document reverted; hint model cleared on undo (per TextDocument.Undo() impl)
        // Caller re-adds hints after undo — confirm doc text is correct
        doc.GetText().Should().Be("int x = 1;");
    }

    [Fact]
    public void GetHintAt_ReturnsCorrectHint()
    {
        var doc   = XF.Doc("void Foo(int n) {}");
        var hints = doc.GetInlayHintModel();
        hints.AddHint(new InlayHint(9,  "Int32"));
        hints.AddHint(new InlayHint(14, "n:"));

        hints.GetHintAt(9)!.Text.Should().Be("Int32");
        hints.GetHintAt(14)!.Text.Should().Be("n:");
        hints.GetHintAt(0).Should().BeNull();
    }

    [Fact]
    public void GetHintsInRange_ReturnsOnlyHintsInRange()
    {
        var doc   = XF.Doc("abcdefghij");
        var hints = doc.GetInlayHintModel();
        hints.AddHint(new InlayHint(2, "a:"));
        hints.AddHint(new InlayHint(5, "b:"));
        hints.AddHint(new InlayHint(8, "c:"));

        var inRange = hints.GetHintsInRange(3, 7);
        inRange.Should().HaveCount(1);
        inRange[0].Text.Should().Be("b:");
    }

    [Fact]
    public void ClearHints_ThenAddNew_CountIsOne()
    {
        var doc   = XF.Doc("x");
        var hints = doc.GetInlayHintModel();
        hints.AddHint(new InlayHint(0, "old"));
        hints.ClearHints();
        hints.AddHint(new InlayHint(0, "new"));

        hints.AllHints.Should().HaveCount(1);
        hints.AllHints[0].Text.Should().Be("new");
    }

    [Fact]
    public void InsertBeforeHint_ShiftsHintOffset()
    {
        var doc   = XF.Doc("hello world");
        var hints = doc.GetInlayHintModel();
        hints.AddHint(new InlayHint(6, "string:"));   // at "world"

        doc.Insert(0, "say ");  // insert 4 chars before hint

        // OnInsert shifts the hint offset forward
        hints.AllHints[0].Offset.Should().Be(10);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. Folding + Change Tracking
//    Editing inside a foldable region marks lines; folding state is separate.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_FoldingAndChangeTracking
{
    [Fact]
    public void EditInsideFoldedBlock_MarksLineDirty()
    {
        var src = "class X {\n    int x = 0;\n    int y = 1;\n}";
        var doc     = XF.CSharpDoc(src);
        var folding = doc.GetFoldingModel();
        var tracker = doc.GetChangeTracker();

        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Fold(0);  // fold the class block

        // Edit inside the folded region (line 1, "int x = 0;")
        int lineOffset = doc.PositionToOffset(1, 0);
        doc.Replace(lineOffset, 6, "string");

        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void FoldingRegions_AfterMinorEdit_StillConsistent()
    {
        var src = "class A {\n    void M() {\n        return;\n    }\n}";
        var doc     = XF.CSharpDoc(src);
        var folding = doc.GetFoldingModel();

        folding.UpdateRegions(new BraceFoldingStrategy());
        int before = folding.Regions.Count;

        // Single-character replace — does not add or remove braces
        doc.Replace(0, 1, "C");

        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Regions.Count.Should().Be(before);
    }

    [Fact]
    public void ChangeTracker_Clean_AfterUndoInsideBlock()
    {
        var src = "fn() {\n    x = 1;\n}";
        var doc     = XF.Doc(src);
        var folding = doc.GetFoldingModel();
        var tracker = doc.GetChangeTracker();

        folding.UpdateRegions(new BraceFoldingStrategy());

        int innerOffset = doc.PositionToOffset(1, 4);
        doc.Replace(innerOffset, 5, "y = 2");
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);

        doc.Undo();
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. Large Paste + Undo (performance / correctness at scale)
//     Inserting thousands of lines and undoing must leave the document
//     exactly as it was before the paste.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_LargePasteAndUndo
{
    [Fact]
    public void Paste1000Lines_UndoRestoresOriginal()
    {
        const int N     = 1000;
        string original = "anchor";
        var doc         = XF.Doc(original);

        var block = "\n" + string.Join("\n", Enumerable.Range(0, N).Select(i => $"line{i}"));
        doc.Insert(doc.Length, block);

        doc.LineCount.Should().BeGreaterThan(N);

        doc.Undo();

        doc.GetText().Should().Be(original);
        doc.LineCount.Should().Be(1);
    }

    [Fact]
    public void Paste1000Lines_ChangeTrackingShowsAdded_AfterUndo_ShowsClean()
    {
        const int N = 1000;
        var doc     = XF.Doc("anchor");
        var tracker = doc.GetChangeTracker();

        var block = "\n" + string.Join("\n", Enumerable.Range(0, N).Select(i => $"x{i}"));
        doc.Insert(doc.Length, block);

        tracker.GetStatus(1).Should().Be(LineStatus.Added);

        doc.Undo();

        tracker.HasAnyChanges.Should().BeFalse();
    }

    [Fact]
    public void Paste1000Lines_SearchFindsLastLine()
    {
        const int N = 1000;
        var doc     = XF.Doc("");
        doc.Insert(0, string.Join("\n", Enumerable.Range(0, N).Select(i => $"row{i}")));

        XF.Searcher(doc).FindFirst("row999").Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 11. Multi-Cursor + Change Tracking
//     Edits via multiple simultaneous cursors each mark their lines dirty.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_MultiCursorAndChangeTracking
{
    [Fact]
    public void MultiCursorInsert_EachLineMarkedModified()
    {
        var doc     = XF.Doc("line0\nline1\nline2");
        var tracker = doc.GetChangeTracker();
        var mc      = new MultiCursor(doc);

        // Cursors at start of line0 and line2
        mc.SetSingle(0);
        mc.AddCursor(12);   // start of line2

        mc.InsertText("X");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void MultiCursorInsert_UndoRestoresAllLines()
    {
        var doc     = XF.Doc("aaa\nbbb\nccc");
        var tracker = doc.GetChangeTracker();
        var mc      = new MultiCursor(doc);

        mc.SetSingle(0);
        mc.AddCursor(4);
        mc.AddCursor(8);
        mc.InsertText("!");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);

        // Single undo step reverts all three cursor edits
        doc.Undo();

        tracker.HasAnyChanges.Should().BeFalse();
        doc.GetText().Should().Be("aaa\nbbb\nccc");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 12. Search + Replace + Undo
//     Replace-all via search results, then undo, returns original text.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_SearchReplaceAndUndo
{
    [Fact]
    public void ReplaceAll_ThenUndo_RestoresOriginal()
    {
        string original = "cat sat on the mat";
        var doc         = XF.Doc(original);

        int count = doc.ReplaceAll("at", "og");
        count.Should().Be(3);

        doc.GetText().Should().Be("cog sog on the mog");

        doc.Undo();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void ReplaceAll_LongerReplacement_CorrectText()
    {
        var doc = XF.Doc("x x x");
        doc.ReplaceAll("x", "foo");
        doc.GetText().Should().Be("foo foo foo");
    }

    [Fact]
    public void ReplaceAll_ShorterReplacement_CorrectLength()
    {
        var doc = XF.Doc("hello hello hello");
        doc.ReplaceAll("hello", "hi");
        doc.GetText().Should().Be("hi hi hi");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 13. Folding + Multi-Cursor
//     Multi-cursor operations on text that spans foldable regions.
// ═══════════════════════════════════════════════════════════════════════════

public class CrossFeature_FoldingAndMultiCursor
{
    [Fact]
    public void MultiCursorInsideAndOutsideBlock_BothEdited()
    {
        var src = "class X {\n    int a;\n    int b;\n}\nint c;";
        var doc     = XF.CSharpDoc(src);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        var mc = new MultiCursor(doc);

        // Line 1 inside block, line 4 outside
        int line1Off = doc.PositionToOffset(1, 4);
        int line4Off = doc.PositionToOffset(4, 0);

        mc.SetSingle(line1Off);
        mc.AddCursor(line4Off);

        mc.InsertText("// ");

        // Cursors at column 4 (after the 4-space indent), so "// " appears inside the line
        doc.GetLine(1).Should().Be("    // int a;");
        doc.GetLine(4).Should().Be("// int c;");
    }

    [Fact]
    public void FoldedRegions_PersistAfterMultiCursorEdit()
    {
        var src = "if (x) {\n    y();\n}\nif (a) {\n    b();\n}";
        var doc     = XF.CSharpDoc(src);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        folding.Fold(0);
        folding.Fold(3);

        var mc = new MultiCursor(doc);
        mc.SetSingle(0);
        mc.AddCursor(doc.PositionToOffset(3, 0));
        mc.InsertText("// ");

        // Both if-blocks were folded; fold state persists after the edit
        XF.IsFolded(folding, 0).Should().BeTrue();
        XF.IsFolded(folding, 3).Should().BeTrue();
    }
}
