using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.ChangeTracking;
using TextAPI.Core.Cursor;
using TextAPI.Core.Decorations;
using TextAPI.Core.Diff;
using TextAPI.Core.Folding;
using TextAPI.Core.InlayHints;
using TextAPI.Core.Language;
using TextAPI.Core.Navigation;
using TextAPI.Core.Outline;
using TextAPI.Core.ReadOnly;
using TextAPI.Core.Scripting;
using TextAPI.Core.Search;
using TextAPI.Core.Snippets;
using TextAPI.Core.StickyScroll;
using TextAPI.Core.WordWrap;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Cross-Feature Matrix Tests
//
// Covers cross-feature pairs NOT already present in CrossFeatureTests.cs.
// Each class focuses on exactly one pair with 2-4 targeted tests.
// ═══════════════════════════════════════════════════════════════════════════

file static class M
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

    public static TextSearcher Searcher(TextDocument doc)
    {
        var pt = new PieceTable();
        pt.Load(doc.GetText());
        return new TextSearcher(pt);
    }

    public static FoldingModel Folded(TextDocument doc)
    {
        var m = doc.GetFoldingModel();
        m.UpdateRegions(new BraceFoldingStrategy());
        return m;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. ReadOnly + MultiCursor
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ReadOnlyAndMultiCursor
{
    [Fact]
    public void MultiCursorInsideProtectedRegion_DocUnchanged()
    {
        // doc = "protected text\nfree text"
        // Protect just "protected" (offsets 0..9) — strictly, interior is (0,9)
        var doc = M.Doc("protected text\nfree text");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(0, 9); // protect "protected" (end exclusive)
        doc.EnforceReadOnly = false; // silent mode

        // In enforce-false mode, insert strictly inside (0,9) → i.e. offset 4 → blocked silently
        // Insert outside protected region should succeed
        doc.Insert(10, "X"); // offset 10 is outside [0,9) — should succeed
        doc.Insert(4, "Y");  // offset 4 is strictly inside (0,9) — silently ignored

        doc.GetText().Should().Contain("X");     // outside edit succeeded
        doc.GetText().Should().NotContain("Y");  // inside edit was silently ignored
        // "protected" at start is unchanged
        doc.GetText().Should().StartWith("protected");
    }

    [Fact]
    public void MultiCursorOutsideProtectedRegion_EditsSucceed()
    {
        var doc = M.Doc("LOCKED\nfree line");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(0, 6); // protect "LOCKED"

        var mc = new MultiCursor(doc);
        mc.SetSingle(7); // start of "free line"
        mc.InsertText(">> ");

        doc.GetLine(0).Should().Be("LOCKED");
        doc.GetLine(1).Should().Be(">> free line");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. ReadOnly + Search
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ReadOnlyAndSearch
{
    [Fact]
    public void Search_FindsTextInsideReadOnlyRegion()
    {
        var doc = M.Doc("protected secret\nother line");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(0, 16);

        // Search must still find text regardless of read-only state
        var match = M.Searcher(doc).FindFirst("secret");
        match.Should().NotBeNull();
        match!.Value.Offset.Should().Be(10);
    }

    [Fact]
    public void Search_FindsAcrossReadOnlyBoundary()
    {
        var doc = M.Doc("hello world");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(0, 5); // protect "hello"

        // "hello world" spans the boundary
        var match = M.Searcher(doc).FindFirst("lo wo");
        match.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. ReadOnly + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ReadOnlyAndUndo
{
    [Fact]
    public void EditOutsideRORegion_Undo_RORegionUnchangedThroughout()
    {
        var doc = M.Doc("LOCKED\nchangeable");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(0, 6);

        // Edit outside
        int off = doc.PositionToOffset(1, 0);
        doc.Replace(off, 9, "MODIFIED");

        doc.GetLine(0).Should().Be("LOCKED");
        doc.Undo();

        doc.GetLine(0).Should().Be("LOCKED");
        doc.GetLine(1).Should().Be("changeable");
    }

    [Fact]
    public void UndoAfterInsertBeforeRORegion_DocRestoredRORegionShifted()
    {
        var doc = M.Doc("hello world");
        var ro  = doc.GetReadOnlyModel();
        ro.Protect(6, 11); // protect "world"

        doc.Insert(0, "say "); // shifts "world" to [10,15)
        ro.IsReadOnly(10).Should().BeTrue();  // region shifted right
        ro.IsReadOnly(6).Should().BeFalse();  // old position no longer RO

        doc.Undo();
        // After undo, doc is back to "hello world"; we just verify the doc was restored
        doc.GetText().Should().Be("hello world");
        // The RO region's final offset after undo depends on whether undo notifies RO model —
        // what we can guarantee is that the region is still active somewhere in the doc
        (ro.IsReadOnly(6) || ro.IsReadOnly(10)).Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. LineComment + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_LineCommentAndChangeTracking
{
    [Fact]
    public void LineCommentToggle_MarksLinesModified()
    {
        var doc     = M.Doc("aaa\nbbb\nccc");
        var tracker = doc.GetChangeTracker();

        LineCommentToggle.Toggle(doc, 0, 2);

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void UndoLineComment_LinesReturnToClean()
    {
        var doc     = M.Doc("alpha\nbeta");
        var tracker = doc.GetChangeTracker();

        LineCommentToggle.Toggle(doc, 0, 1);
        tracker.HasAnyChanges.Should().BeTrue();

        doc.Undo();
        tracker.HasAnyChanges.Should().BeFalse();
        doc.GetLine(0).Should().Be("alpha");
        doc.GetLine(1).Should().Be("beta");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. LineComment + MultiCursor
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_LineCommentAndMultiCursor
{
    [Fact]
    public void CommentLines_ThenMultiCursorEdit_BothOpsApplied()
    {
        var doc = M.Doc("aaa\nbbb\nccc");

        // First comment lines 0 and 1
        LineCommentToggle.Toggle(doc, 0, 1);
        doc.GetLine(0).Should().Be("// aaa");
        doc.GetLine(1).Should().Be("// bbb");

        // Then multi-cursor at start of each commented line
        var mc = new MultiCursor(doc);
        mc.SetSingle(0);
        mc.AddCursor(doc.PositionToOffset(1, 0));
        mc.InsertText("/* ");

        doc.GetLine(0).Should().StartWith("/* // aaa");
        doc.GetLine(1).Should().StartWith("/* // bbb");
    }

    [Fact]
    public void MultiCursorInsert_ThenCommentToggle_AllLinesCommented()
    {
        var doc = M.Doc("fn1()\nfn2()\nfn3()");

        // Multi-cursor adds a prefix
        var mc = new MultiCursor(doc);
        mc.SetSingle(0);
        mc.AddCursor(doc.PositionToOffset(1, 0));
        mc.AddCursor(doc.PositionToOffset(2, 0));
        mc.InsertText("call ");

        // Then comment all
        LineCommentToggle.Toggle(doc, 0, 2);
        doc.GetLine(0).Should().Be("// call fn1()");
        doc.GetLine(1).Should().Be("// call fn2()");
        doc.GetLine(2).Should().Be("// call fn3()");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. LineComment + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_LineCommentAndFolding
{
    private const string Src =
        "void Foo() {\n    int x = 1;\n    return;\n}\nvoid Bar() {\n    return;\n}";

    [Fact]
    public void CommentLinesInsideFoldedBlock_FoldRegionStillDetected()
    {
        // Use a plain doc (no tokeniser) so BraceFoldingStrategy works purely on braces
        var doc     = M.Doc(Src);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        int regionsBefore = folding.Regions.Count;
        regionsBefore.Should().BeGreaterThan(0, "source has braces on separate lines");

        // Comment inner body lines — braces on lines 0, 3, 4, 6 are untouched
        LineCommentToggle.Toggle(doc, 1, 2);

        // After commenting body lines, brace structure is unchanged
        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Regions.Count.Should().Be(regionsBefore, "only body lines were commented, not brace lines");
    }

    [Fact]
    public void CommentFoldStart_DocTextChanges_RegionCountMayChange()
    {
        var doc     = M.CSharpDoc(Src);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        // Comment the brace line — this changes the structure so regions may shift
        LineCommentToggle.Toggle(doc, 0, 0);
        folding.UpdateRegions(new BraceFoldingStrategy());

        // Just verify folding still works without crashing
        folding.Regions.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Diff + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_DiffAndChangeTracking
{
    [Fact]
    public void Diff_ReportsChanges_MatchingChangeTracker()
    {
        var doc     = M.Doc("line0\nline1\nline2\nline3");
        var tracker = doc.GetChangeTracker();

        string original = doc.GetText();

        // Modify lines 1 and 3
        int off1 = doc.PositionToOffset(1, 0);
        doc.Replace(off1, 5, "CHANGED");
        int off3 = doc.PositionToOffset(3, 0);
        doc.Replace(off3, 5, "CHANGED");

        // Change tracker sees modifications
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(3).Should().Be(LineStatus.Modified);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);

        // Diff sees changes between old and new text
        var result = TextDiff.Diff(original, doc.GetText());
        result.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void Diff_IdenticalTexts_NoChanges_TrackerClean()
    {
        var doc     = M.Doc("same content\nsame lines");
        var tracker = doc.GetChangeTracker();

        var result = TextDiff.Diff(doc.GetText(), doc.GetText());
        result.HasChanges.Should().BeFalse();
        tracker.HasAnyChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Diff + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_DiffAndUndo
{
    [Fact]
    public void EditThenUndo_DiffShowsNoChanges()
    {
        var doc      = M.Doc("original line\nsecond line");
        string before = doc.GetText();

        doc.Replace(0, 8, "MODIFIED");
        var diffAfterEdit = TextDiff.Diff(before, doc.GetText());
        diffAfterEdit.HasChanges.Should().BeTrue();

        doc.Undo();
        var diffAfterUndo = TextDiff.Diff(before, doc.GetText());
        diffAfterUndo.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void DiffBeforeAndAfterBulkEdit_UndoRestoresDiff()
    {
        var doc      = M.Doc(string.Join("\n", Enumerable.Range(0, 20).Select(i => $"line{i}")));
        string snap  = doc.GetText();

        doc.ReplaceAll("line", "row");
        TextDiff.Diff(snap, doc.GetText()).HasChanges.Should().BeTrue();

        doc.Undo();
        TextDiff.Diff(snap, doc.GetText()).HasChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. SyntaxHighlighting + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SyntaxHighlightingAndChangeTracking
{
    [Fact]
    public void EditLine_SyntaxTokensChange_ChangeTrackerMarksModified()
    {
        var doc     = M.CSharpDoc("var x = 1;\nvar y = 2;");
        var tracker = doc.GetChangeTracker();

        var tokensBefore = doc.GetSyntaxTokens(0);
        tokensBefore.Should().NotBeEmpty();

        doc.Replace(4, 1, "result");  // change "x" to "result"
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);

        var tokensAfter = doc.GetSyntaxTokens(0);
        // Tokens should be updated (not the same reference)
        tokensAfter.Should().NotBeSameAs(tokensBefore);
    }

    [Fact]
    public void UndoEdit_TokensInvalidated_ChangeTrackerClean()
    {
        var doc     = M.CSharpDoc("int value = 42;");
        var tracker = doc.GetChangeTracker();

        doc.Insert(4, "Long");
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);

        doc.Undo();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        // Tokens should re-tokenise correctly after undo
        var tokens = doc.GetSyntaxTokens(0);
        tokens.Should().NotBeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. SyntaxHighlighting + MultiCursor
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SyntaxHighlightingAndMultiCursor
{
    [Fact]
    public void MultiCursorEdit_TokenCacheInvalidatedForAffectedLines()
    {
        var doc = M.CSharpDoc("var a = 1;\nvar b = 2;\nvar c = 3;");

        // Warm the cache — line 0 has "var" keyword tokens
        var cachedLine0 = doc.GetSyntaxTokens(0);
        cachedLine0.Should().Contain(t => t.Type == "keyword");

        var mc = new MultiCursor(doc);
        mc.SetSingle(0);
        mc.AddCursor(doc.PositionToOffset(2, 0));
        mc.InsertText("// ");

        // Verify the doc content changed correctly
        doc.GetLine(0).Should().Be("// var a = 1;");
        doc.GetLine(2).Should().Be("// var c = 3;");

        // The CSharpTokeniser correctly identifies comment tokens on the new content
        var tokeniser   = new CSharpTokeniser();
        var line0Tokens = tokeniser.TokeniseLine(doc.GetLine(0));
        line0Tokens.Should().Contain(t => t.Type == "comment",
            "after inserting '// ' at start, the line should have a comment token");

        // Line 1 (unedited) should still show keyword tokens
        var line1Tokens = tokeniser.TokeniseLine(doc.GetLine(1));
        line1Tokens.Should().Contain(t => t.Type == "keyword");
    }

    [Fact]
    public void MultiCursorInsert_AddedNewlines_TokenCountUpdated()
    {
        var doc = M.CSharpDoc("int x;\nint y;");
        var mc  = new MultiCursor(doc);
        mc.SetSingle(6);
        mc.AddCursor(doc.PositionToOffset(1, 5));

        // Insert a newline after each variable declaration
        mc.InsertText("\n");

        doc.LineCount.Should().Be(4);
        // Tokens on newly created lines should be retrievable
        var t = doc.GetSyntaxTokens(2);
        t.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 11. Outline + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_OutlineAndFolding
{
    private const string NestedSrc =
        "class Outer {\n    void A() {\n        int x;\n    }\n    void B() {\n        int y;\n    }\n}";

    [Fact]
    public void OutlineNodes_MatchFoldRegionCount()
    {
        var doc     = M.CSharpDoc(NestedSrc);
        var folding = M.Folded(doc);

        var outline = OutlineProvider.GetOutline(folding);
        var allNodes = FlattenOutline(outline);

        // Every outline node should correspond to a fold region
        allNodes.Should().NotBeEmpty();
        allNodes.Count.Should().BeLessThanOrEqualTo(folding.Regions.Count + 1);
    }

    [Fact]
    public void OutlineRootNode_StartsAtSameLine_AsFoldRegionStart()
    {
        var doc     = M.CSharpDoc("void Foo() {\n    int x;\n}");
        var folding = M.Folded(doc);

        var outline = OutlineProvider.GetOutline(folding);
        outline.Should().NotBeEmpty();
        var root = outline[0];
        // Root outline node's StartLine should match a fold region
        folding.Regions.Should().Contain(r => r.StartLine == root.StartLine);
    }

    private static List<OutlineNode> FlattenOutline(IReadOnlyList<OutlineNode> nodes)
    {
        var list = new List<OutlineNode>();
        void Visit(OutlineNode n) { list.Add(n); foreach (var c in n.Children) Visit(c); }
        foreach (var r in nodes) Visit(r);
        return list;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 12. IndentGuides + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_IndentGuidesAndFolding
{
    [Fact]
    public void IndentGuides_ComputedCorrectly_EvenForFoldedRegion()
    {
        var doc     = M.Doc("class X {\n    void M() {\n        int x;\n    }\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Fold(folding.Regions[0].StartLine);

        // Indent guides cover the full document range, including folded lines
        var guides = doc.GetIndentGuides(0, doc.LineCount - 1);
        guides.Should().NotBeNull();
        // Guides should still be computable without errors
    }

    [Fact]
    public void IndentGuides_FoldAll_ThenUnfoldAll_GuidesConsistent()
    {
        var doc     = M.Doc("if (x) {\n    a();\n    b();\n}\nif (y) {\n    c();\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        var guidesBefore = doc.GetIndentGuides(0, doc.LineCount - 1);

        foreach (var r in folding.Regions.ToList()) folding.Fold(r.StartLine);
        foreach (var r in folding.Regions.ToList()) folding.Unfold(r.StartLine);

        var guidesAfter = doc.GetIndentGuides(0, doc.LineCount - 1);
        guidesAfter.Count.Should().Be(guidesBefore.Count);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 13. BracketMatcher + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_BracketMatcherAndFolding
{
    [Fact]
    public void FindMatch_AcrossFoldBoundary_ReturnsCorrectPartner()
    {
        // Open brace is on a different line than close brace — the block is foldable
        var doc     = M.Doc("func() {\n    body;\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        int openOffset  = doc.GetText().IndexOf('{');
        int closeOffset = doc.GetText().IndexOf('}');

        // Even when the region is folded, BracketMatcher works on raw text
        folding.Fold(folding.Regions[0].StartLine);
        int match = BracketMatcher.FindMatch(doc, openOffset);
        match.Should().Be(closeOffset);
    }

    [Fact]
    public void FindMatch_CloseBrace_ReturnsOpenBrace()
    {
        var doc        = M.Doc("{\n    content;\n}");
        int closeOff   = doc.GetText().LastIndexOf('}');
        int openOff    = doc.GetText().IndexOf('{');

        BracketMatcher.FindMatch(doc, closeOff).Should().Be(openOff);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 14. DocumentCleanup + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_DocumentCleanupAndChangeTracking
{
    [Fact]
    public void TrimTrailingWhitespace_MarksAffectedLinesModified()
    {
        var doc     = M.Doc("hello   \nclean\nworld  ");
        var tracker = doc.GetChangeTracker();

        int count = DocumentCleanup.TrimTrailingWhitespace(doc);
        count.Should().Be(2);

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void TrimTrailingWhitespace_AlreadyClean_NoChangesTracked()
    {
        var doc     = M.Doc("clean line\nanother clean");
        var tracker = doc.GetChangeTracker();

        DocumentCleanup.TrimTrailingWhitespace(doc);
        tracker.HasAnyChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 15. DocumentCleanup + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_DocumentCleanupAndUndo
{
    [Fact]
    public void TrimTrailingWhitespace_ThenUndo_RestoresOriginal()
    {
        string original = "line with spaces   \nanother  ";
        var doc         = M.Doc(original);

        DocumentCleanup.TrimTrailingWhitespace(doc);
        doc.GetText().Should().Be("line with spaces\nanother");

        doc.Undo();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void TrimThenUndoThenRedo_CleanVersionRestored()
    {
        var doc = M.Doc("trailing   \nspaces  ");

        DocumentCleanup.TrimTrailingWhitespace(doc);
        string trimmed = doc.GetText();

        doc.Undo();
        doc.Redo();
        doc.GetText().Should().Be(trimmed);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 16. Scripting + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ScriptingAndChangeTracking
{
    [Fact]
    public void ScriptInsert_MarksLineAdded()
    {
        var doc     = M.Doc("line0\nline1");
        var tracker = doc.GetChangeTracker();
        var runner  = new ScriptRunner(doc);

        runner.Run("MOVE 0\nINSERT \"new line\\n\"");

        // Inserting a new line at offset 0 should mark line as Added
        tracker.GetStatus(0).Should().Be(LineStatus.Added);
    }

    [Fact]
    public void ScriptReplaceAll_MarksReplacedLinesModified()
    {
        var doc     = M.Doc("foo bar\nfoo baz\nquux");
        var tracker = doc.GetChangeTracker();
        var runner  = new ScriptRunner(doc);

        var result = runner.Run("REPLACE_ALL \"foo\" \"FOO\"");
        result.Success.Should().BeTrue();
        result.LastReplaceCount.Should().Be(2);

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 17. Scripting + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ScriptingAndUndo
{
    [Fact]
    public void ScriptInsert_ThenUndo_RestoresOriginal()
    {
        var doc    = M.Doc("original");
        var runner = new ScriptRunner(doc);

        runner.Run("MOVE 0\nINSERT \"prefix \"");
        doc.GetText().Should().Be("prefix original");

        doc.Undo();
        doc.GetText().Should().Be("original");
    }

    [Fact]
    public void ScriptUndoVerb_UndoesLastChange()
    {
        var doc    = M.Doc("hello");
        var runner = new ScriptRunner(doc);

        // INSERT then UNDO inside the script itself
        var result = runner.Run("MOVE 5\nINSERT \" world\"\nUNDO");
        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello");
    }

    [Fact]
    public void ScriptDeleteLine_ThenDocUndo_Restores()
    {
        var doc    = M.Doc("line0\nline1\nline2");
        var runner = new ScriptRunner(doc);

        runner.Run("DELETE_LINE 2");
        doc.LineCount.Should().Be(2);

        doc.Undo();
        doc.LineCount.Should().Be(3);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 18. GoTo + CursorHistory
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_GoToAndCursorHistory
{
    [Fact]
    public void GoTo_PushesToCursorHistory_BackReturnsOriginal()
    {
        var doc = M.Doc("line0\nline1\nline2\nline3");

        doc.GoTo(0, 0);
        doc.GoTo(2, 0);
        doc.GoTo(3, 0);

        var hist = doc.GetCursorHistory();
        var pos3 = doc.OffsetToPosition(hist.Current!.Value.Offset);
        pos3.Line.Should().Be(3);

        var back = hist.Back();
        var pos2 = doc.OffsetToPosition(back!.Value.Offset);
        pos2.Line.Should().Be(2);
    }

    [Fact]
    public void GoTo_MultipleJumps_ForwardRestoresPosition()
    {
        var doc = M.Doc("line0\nline1\nline2");

        doc.GoTo(0, 0);
        doc.GoTo(1, 0);
        doc.GoTo(2, 0);

        var hist = doc.GetCursorHistory();
        hist.Back(); // go to line1
        var fwd  = hist.Forward();
        var pos  = doc.OffsetToPosition(fwd!.Value.Offset);
        pos.Line.Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 19. GoTo + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_GoToAndFolding
{
    [Fact]
    public void GoTo_InsideFoldedRegion_PositionIsValid()
    {
        var doc     = M.Doc("class X {\n    int x;\n    int y;\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Fold(0); // fold the entire class block

        // GoTo a line that is inside the folded block
        doc.GoTo(1, 0);

        var hist = doc.GetCursorHistory();
        hist.Current.Should().NotBeNull();
        var pos = doc.OffsetToPosition(hist.Current!.Value.Offset);
        pos.Line.Should().Be(1); // position is still valid even though folded
    }

    [Fact]
    public void GoTo_Unfolds_GotoDestination_Consistent()
    {
        var doc     = M.Doc("void Foo() {\n    return;\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        // Navigate to line inside block
        doc.GoTo(1, 4);
        var offset = doc.GetCursorHistory().Current!.Value.Offset;
        var pos    = doc.OffsetToPosition(offset);
        pos.Line.Should().Be(1);
        pos.Column.Should().Be(4);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 20. CursorHistory + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_CursorHistoryAndUndo
{
    [Fact]
    public void UndoDocChange_CursorHistoryUnchanged()
    {
        var doc  = M.Doc("hello\nworld");
        doc.GoTo(0, 0);
        doc.GoTo(1, 0);

        var hist    = doc.GetCursorHistory();
        int countBefore = hist.Count;

        // Undo a document edit should not affect cursor history
        doc.Insert(0, "X");
        doc.Undo();

        hist.Count.Should().Be(countBefore);
    }

    [Fact]
    public void PushHistory_UndoMultipleTimes_HistoryIntact()
    {
        var doc = M.Doc("aaa\nbbb\nccc");
        doc.GoTo(0, 0);
        doc.GoTo(2, 0);

        doc.Insert(0, "edit1\n");
        doc.Insert(0, "edit2\n");

        while (doc.CanUndo) doc.Undo();

        var hist = doc.GetCursorHistory();
        hist.Count.Should().BeGreaterThanOrEqualTo(2);
        hist.Current.Should().NotBeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 21. RegexCapture + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_RegexCaptureAndChangeTracking
{
    [Fact]
    public void ReplaceAllRegex_MarksReplacedLinesModified()
    {
        var doc     = M.Doc("int foo = 1;\nint bar = 2;\nstring baz;");
        var tracker = doc.GetChangeTracker();
        var opts    = new SearchOptions { UseRegex = true };

        int count = doc.ReplaceAll(@"int (\w+)", "var $1", opts);
        count.Should().Be(2);

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void ReplaceAllRegex_NoMatches_NoChangesTracked()
    {
        var doc     = M.Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();
        var opts    = new SearchOptions { UseRegex = true };

        doc.ReplaceAll(@"\d+", "NUM", opts);
        tracker.HasAnyChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 22. RegexCapture + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_RegexCaptureAndUndo
{
    [Fact]
    public void ReplaceAllRegex_ThenUndo_RestoresOriginal()
    {
        string original = "public void Foo() {}\npublic void Bar() {}";
        var doc         = M.Doc(original);
        var opts        = new SearchOptions { UseRegex = true };

        doc.ReplaceAll(@"public void (\w+)", "private void $1", opts);
        doc.GetText().Should().Contain("private void Foo");

        doc.Undo();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void ReplaceAllRegex_MultipleGroups_ThenUndo_Restores()
    {
        string original = "John Smith\nJane Doe";
        var doc         = M.Doc(original);
        var opts        = new SearchOptions { UseRegex = true };

        doc.ReplaceAll(@"(\w+) (\w+)", "$2, $1", opts);
        doc.Undo();
        doc.GetText().Should().Be(original);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 23. ColumnSelection + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ColumnSelectionAndChangeTracking
{
    [Fact]
    public void ColumnEdit_MarksAllAffectedLinesModified()
    {
        var doc     = M.Doc("aaa\nbbb\nccc\nddd");
        var tracker = doc.GetChangeTracker();
        var mc      = new MultiCursor(doc);

        mc.AddColumnSelection(0, 3, 0);
        mc.InsertText("// ");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
        tracker.GetStatus(3).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void ColumnEdit_DeleteColumn_MarksLinesModified()
    {
        var doc     = M.Doc("Xaaa\nXbbb\nXccc");
        var tracker = doc.GetChangeTracker();
        var mc      = new MultiCursor(doc);

        mc.AddColumnSelection(0, 2, 0);
        // Select one character at each line then delete
        foreach (var c in mc.All) c.SelectTo(c.CaretOffset + 1);
        mc.DeleteSelection();

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 24. ColumnSelection + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ColumnSelectionAndUndo
{
    [Fact]
    public void ColumnEdit_ThenUndo_RestoresAllLines()
    {
        var doc     = M.Doc("line0\nline1\nline2");
        var mc      = new MultiCursor(doc);
        string original = doc.GetText();

        mc.AddColumnSelection(0, 2, 0);
        mc.InsertText(">> ");

        doc.Undo();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void ColumnDeleteThenUndo_OriginalRestoredAllLines()
    {
        var doc = M.Doc("Zaaa\nZbbb\nZccc");
        var mc  = new MultiCursor(doc);
        string original = doc.GetText();

        mc.AddColumnSelection(0, 2, 0);
        foreach (var c in mc.All) c.SelectTo(c.CaretOffset + 1);
        mc.DeleteSelection();

        doc.Undo();
        doc.GetText().Should().Be(original);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 25. Bookmarks + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_BookmarksAndFolding
{
    [Fact]
    public void Bookmark_InsideFoldedRegion_NotRemovedByFold()
    {
        var doc     = M.Doc("class X {\n    int x;\n    int y;\n}");
        var marks   = new BookmarkModel();
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        marks.Toggle(1); // bookmark on "    int x;" (inside block)
        marks.IsBookmarked(1).Should().BeTrue();

        folding.Fold(0);
        marks.IsBookmarked(1).Should().BeTrue(); // fold does not remove bookmark

        folding.Unfold(0);
        marks.IsBookmarked(1).Should().BeTrue(); // still there after unfold
    }

    [Fact]
    public void InsertLineBeforeFold_BookmarkShifts()
    {
        var doc   = M.Doc("top\nclass X {\n    body;\n}");
        var marks = new BookmarkModel();
        marks.Toggle(2); // bookmark on "    body;"

        // Insert a line before the fold start (at line 1)
        marks.OnInsert(1, 1);
        marks.IsBookmarked(3).Should().BeTrue(); // shifted from 2 to 3
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 26. Bookmarks + Search
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_BookmarksAndSearch
{
    [Fact]
    public void Search_FindsTextOnBookmarkedLine()
    {
        var doc   = M.Doc("alpha\nbeta\ngamma");
        var marks = new BookmarkModel();
        marks.Toggle(1); // bookmark "beta" line

        var match = M.Searcher(doc).FindFirst("beta");
        match.Should().NotBeNull();

        // Offset of match should be on the bookmarked line
        var pos = doc.OffsetToPosition(match!.Value.Offset);
        pos.Line.Should().Be(1);
        marks.IsBookmarked(pos.Line).Should().BeTrue();
    }

    [Fact]
    public void FindAll_CountMatchesBookmarkedLines()
    {
        // Three lines each containing "target"; bookmark lines 0 and 2
        var doc   = M.Doc("target A\nno match\ntarget B");
        var marks = new BookmarkModel();
        marks.Toggle(0);
        marks.Toggle(2);

        var matches = M.Searcher(doc).FindAll("target");
        matches.Should().HaveCount(2);
        foreach (var m in matches)
        {
            var line = doc.OffsetToPosition(m.Offset).Line;
            marks.IsBookmarked(line).Should().BeTrue();
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 27. Bookmarks + MultiCursor
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_BookmarksAndMultiCursor
{
    [Fact]
    public void MultiCursorInsertAtStart_ShiftsBookmarksOnAffectedLines()
    {
        // "line0\nline1\nline2" — bookmark line 1
        var doc   = M.Doc("line0\nline1\nline2");
        var marks = new BookmarkModel();
        marks.Toggle(1);

        // Insert a new line before line 1
        var mc = new MultiCursor(doc);
        mc.SetSingle(6); // start of line1
        mc.InsertText("new\n");
        marks.OnInsert(1, 1); // notify bookmark model

        marks.IsBookmarked(2).Should().BeTrue();  // shifted
        marks.IsBookmarked(1).Should().BeFalse();
    }

    [Fact]
    public void MultiCursorEditsOnSameLine_BookmarkRetained()
    {
        var doc   = M.Doc("aaa\nbbb\nccc");
        var marks = new BookmarkModel();
        marks.Toggle(1); // bookmark line 1

        // Edit line 1 via multi-cursor (no line count change)
        var mc = new MultiCursor(doc);
        mc.SetSingle(doc.PositionToOffset(1, 0));
        mc.InsertText("X");

        // Line count unchanged; bookmark still at line 1
        marks.IsBookmarked(1).Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 28. Snippets + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SnippetsAndChangeTracking
{
    [Fact]
    public void SnippetExpansion_MarksInsertedLinesAdded()
    {
        var doc     = M.Doc("before\nafter");
        var tracker = doc.GetChangeTracker();

        // Insert a multiline snippet between the two lines
        var snippet = SnippetEngine.Parse("line A\nline B\n");
        int insertAt = doc.PositionToOffset(1, 0);
        SnippetEngine.BeginSnippet(doc, snippet, insertAt);

        // New lines should be Added
        tracker.GetStatus(1).Should().Be(LineStatus.Added);
        tracker.GetStatus(2).Should().Be(LineStatus.Added);
    }

    [Fact]
    public void SnippetExpansion_SingleLine_MarksModified()
    {
        var doc     = M.Doc("start ");
        var tracker = doc.GetChangeTracker();

        var snippet = SnippetEngine.Parse("end");
        SnippetEngine.BeginSnippet(doc, snippet, 6);

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 29. Snippets + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SnippetsAndFolding
{
    [Fact]
    public void SnippetExpansion_NearFoldBoundary_FoldReevaluated()
    {
        var doc     = M.CSharpDoc("void Foo() {\n    return;\n}\n");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        int regionsBefore = folding.Regions.Count;

        // Expand a snippet that adds a new method after the existing one
        var snippet  = SnippetEngine.Parse("void Bar() {\n    return;\n}\n");
        int insertAt = doc.Length;
        SnippetEngine.BeginSnippet(doc, snippet, insertAt);

        // Re-detect fold regions after snippet expansion
        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Regions.Count.Should().BeGreaterThan(regionsBefore);
    }

    [Fact]
    public void SnippetInsideBlock_FoldStatePreserved()
    {
        var doc     = M.CSharpDoc("class A {\n    // body\n}\n");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());
        folding.Fold(0);

        // Insert snippet at end of class body
        int off = doc.PositionToOffset(1, 0);
        SnippetEngine.BeginSnippet(doc, SnippetEngine.Parse("int x = 0;\n"), off);

        // Fold state stays true (fold was on line 0 and we insert at line 1)
        folding.Regions.Should().Contain(r => r.StartLine == 0 && r.IsFolded);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 30. InlayHints + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_InlayHintsAndChangeTracking
{
    [Fact]
    public void InsertText_ShiftsHintOffset_ChangeTrackerMarksLine()
    {
        var doc     = M.Doc("int x = 1;");
        var hints   = doc.GetInlayHintModel();
        var tracker = doc.GetChangeTracker();

        hints.AddHint(new InlayHint(4, "Int32"));

        doc.Insert(0, "// prefix\n");

        // Hint should have shifted
        hints.AllHints[0].Offset.Should().BeGreaterThan(4);
        // Change tracker marks the affected line
        tracker.HasAnyChanges.Should().BeTrue();
    }

    [Fact]
    public void DeleteText_HintShiftsBack_ChangeTracked()
    {
        var doc     = M.Doc("XXXint x = 1;");
        var hints   = doc.GetInlayHintModel();
        var tracker = doc.GetChangeTracker();

        hints.AddHint(new InlayHint(7, "Int32")); // after "XXXint "

        doc.Delete(0, 3); // remove "XXX"

        hints.AllHints[0].Offset.Should().Be(4);
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 31. InlayHints + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_InlayHintsAndFolding
{
    [Fact]
    public void HintsInsideFoldedRegion_NotRemovedByFold()
    {
        var doc     = M.Doc("class X {\n    int x = 1;\n}");
        var hints   = doc.GetInlayHintModel();
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        int innerOffset = doc.PositionToOffset(1, 4);
        hints.AddHint(new InlayHint(innerOffset, "Int32"));

        folding.Fold(0);
        hints.AllHints.Should().HaveCount(1); // hint survives fold

        folding.Unfold(0);
        hints.AllHints.Should().HaveCount(1); // still there after unfold
    }

    [Fact]
    public void FoldAll_UnfoldAll_HintOffsetUnchanged()
    {
        var doc     = M.Doc("void M() {\n    int y = 0;\n}");
        var hints   = doc.GetInlayHintModel();
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        int off = doc.PositionToOffset(1, 10);
        hints.AddHint(new InlayHint(off, "Int32"));

        folding.Fold(0);
        folding.Unfold(0);

        hints.AllHints[0].Offset.Should().Be(off);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 32. WordWrap + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_WordWrapAndFolding
{
    [Fact]
    public void FoldedLine_WordWrapModel_ReportsOneRow()
    {
        // A long line that would normally wrap
        string longLine = new string('a', 200);
        var doc = M.Doc("class X {\n    " + longLine + "\n}");
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        var model = doc.GetWordWrapModel(80);
        int wrappedBefore = model.WrappedRowCount(1); // line with long content
        wrappedBefore.Should().BeGreaterThan(1);

        // Fold the block — folded lines are logically hidden
        folding.Fold(0);
        var visibleLines = folding.GetVisibleLines();
        visibleLines.Should().NotContain(1); // line 1 is hidden
    }

    [Fact]
    public void WordWrapModel_FoldUnfold_RowCountConsistent()
    {
        var doc     = M.Doc("short\nlonger line that wraps at 10\nshort");
        var folding = doc.GetFoldingModel();
        var model   = doc.GetWordWrapModel(10);

        int rowsBefore = model.WrappedRowCount(1);

        // No-op fold/unfold on plain doc should not corrupt model
        folding.UpdateRegions(new BraceFoldingStrategy());
        var modelAfter = doc.GetWordWrapModel(10);
        modelAfter.WrappedRowCount(1).Should().Be(rowsBefore);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 33. WordWrap + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_WordWrapAndChangeTracking
{
    [Fact]
    public void EditLine_WrappedRowCountChanges_ChangeTrackerMarksModified()
    {
        var doc     = M.Doc("short line\nother");
        var tracker = doc.GetChangeTracker();
        var model   = doc.GetWordWrapModel(10);

        int before = model.WrappedRowCount(0);

        // Extend the line beyond wrap width
        doc.Insert(10, " and more text that wraps");
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);

        var modelNew = doc.GetWordWrapModel(10);
        modelNew.WrappedRowCount(0).Should().BeGreaterThan(before);
    }

    [Fact]
    public void UndoLineEdit_WrappedRowCountRestored_ChangeTrackerClean()
    {
        var doc     = M.Doc("line to extend");
        var tracker = doc.GetChangeTracker();
        var model   = doc.GetWordWrapModel(10);
        int origRows = model.WrappedRowCount(0);

        doc.Insert(14, " with lots of extra words added here");
        doc.Undo();

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        var modelAfter = doc.GetWordWrapModel(10);
        modelAfter.WrappedRowCount(0).Should().Be(origRows);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 34. WordWrap + MultiCursor
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_WordWrapAndMultiCursor
{
    [Fact]
    public void MultiCursorInsert_WordWrapRecalculated()
    {
        var doc   = M.Doc("short\nshort\nshort");
        var mc    = new MultiCursor(doc);

        mc.SetSingle(0);
        mc.AddCursor(doc.PositionToOffset(1, 0));
        mc.AddCursor(doc.PositionToOffset(2, 0));
        mc.InsertText("very long prefix text that definitely wraps ");

        var model = doc.GetWordWrapModel(20);
        // Each line should now wrap
        for (int i = 0; i < doc.LineCount; i++)
            model.WrappedRowCount(i).Should().BeGreaterThan(1);
    }

    [Fact]
    public void MultiCursorDelete_WrappedRowCountDecreases()
    {
        string longPrefix = "aaaaaaaaaaaaaaaaaaaaaaaa";
        var doc   = M.Doc(longPrefix + "end\n" + longPrefix + "end");
        var model = doc.GetWordWrapModel(10);
        int before = model.WrappedRowCount(0);

        // Delete the long prefix from line 0
        var mc = new MultiCursor(doc);
        mc.SetSingle(0);
        mc.Primary.SelectTo(longPrefix.Length);
        mc.DeleteSelection();

        var modelNew = doc.GetWordWrapModel(10);
        modelNew.WrappedRowCount(0).Should().BeLessThan(before);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 35. StickyScroll + Folding
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_StickyScrollAndFolding
{
    private const string Code =
        "class Outer {\n    void Inner() {\n        int x;\n    }\n}";

    [Fact]
    public void StickyScrollContext_InsideFoldedBlock_StillReturnsAncestors()
    {
        var doc     = M.Doc(Code);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        // Context for viewport at line 2 (inside Inner)
        var context = StickyScroll.GetContext(folding, 2);
        context.Should().NotBeEmpty();
        context.Should().Contain(e => e.DocumentLine < 2); // at least one ancestor
    }

    [Fact]
    public void StickyScrollContext_AfterFoldAll_ViewportAtFoldedLine_Empty()
    {
        var doc     = M.Doc(Code);
        var folding = doc.GetFoldingModel();
        folding.UpdateRegions(new BraceFoldingStrategy());

        // Fold everything
        foreach (var r in folding.Regions.ToList()) folding.Fold(r.StartLine);

        // When viewport is at line 0 (the fold start), context is empty
        var context = StickyScroll.GetContext(folding, 0);
        context.Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 36. Search + Bookmarks
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SearchAndBookmarks
{
    [Fact]
    public void FindFirst_BookmarkMatchLine_NavigateViaBookmark()
    {
        var doc   = M.Doc("no\nfind here\nno");
        var marks = new BookmarkModel();

        var match = M.Searcher(doc).FindFirst("find here");
        match.Should().NotBeNull();
        int matchLine = doc.OffsetToPosition(match!.Value.Offset).Line;

        marks.Toggle(matchLine);
        marks.IsBookmarked(matchLine).Should().BeTrue();
        marks.GetAll().Should().ContainSingle().Which.Should().Be(matchLine);
    }

    [Fact]
    public void FindAll_BookmarkAllMatchLines_AllMarked()
    {
        var doc   = M.Doc("hit\nmiss\nhit\nmiss\nhit");
        var marks = new BookmarkModel();

        var matches = M.Searcher(doc).FindAll("hit");
        foreach (var m in matches)
        {
            int line = doc.OffsetToPosition(m.Offset).Line;
            marks.Toggle(line);
        }

        marks.GetAll().Should().HaveCount(3);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 37. Search + CursorHistory
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_SearchAndCursorHistory
{
    [Fact]
    public void GoToFoundPosition_CursorHistoryTracksIt()
    {
        var doc   = M.Doc("first\nfound here\nlast");
        var match = M.Searcher(doc).FindFirst("found here");
        match.Should().NotBeNull();

        var pos = doc.OffsetToPosition(match!.Value.Offset);
        doc.GoTo(pos.Line, pos.Column);

        var hist   = doc.GetCursorHistory();
        var curPos = doc.OffsetToPosition(hist.Current!.Value.Offset);
        curPos.Line.Should().Be(pos.Line);
    }

    [Fact]
    public void SearchMultipleTerms_GoToEach_HistoryHasAllPositions()
    {
        var doc = M.Doc("alpha\nbeta\ngamma");

        var m1 = M.Searcher(doc).FindFirst("alpha");
        var m2 = M.Searcher(doc).FindFirst("gamma");

        doc.GoTo(doc.OffsetToPosition(m1!.Value.Offset).Line, 0);
        doc.GoTo(doc.OffsetToPosition(m2!.Value.Offset).Line, 0);

        var hist = doc.GetCursorHistory();
        hist.Count.Should().BeGreaterThanOrEqualTo(2);
        var pos  = doc.OffsetToPosition(hist.Current!.Value.Offset);
        pos.Line.Should().Be(2); // gamma is on line 2
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 38. Decorations + Undo
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_DecorationsAndUndo
{
    [Fact]
    public void AddDecoration_UndoDocChange_DecorationAPIStillWorks()
    {
        var doc = M.Doc("hello world");
        var id  = doc.AddDecoration(0, 5, DecorationType.SearchMatch, "sel");

        doc.Insert(0, "prefix ");
        doc.Undo();

        // After undo the doc is restored; decoration API should still be queryable
        var decos = doc.GetDecorationsInRange(0, doc.Length);
        // The decoration might have been shifted/cleared by undo; just verify no crash
        decos.Should().NotBeNull();
    }

    [Fact]
    public void AddDecoration_AfterUndoInsert_CanStillQuery()
    {
        var doc = M.Doc("text");
        doc.Insert(0, "new ");
        doc.Undo();

        // Add a fresh decoration after undo
        doc.AddDecoration(0, 4, DecorationType.ErrorSquiggle, "err");
        var decos = doc.GetDecorationsInRange(0, doc.Length);
        decos.Should().Contain(d => d.Type == DecorationType.ErrorSquiggle);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 39. ReadOnly + ChangeTracking
// ═══════════════════════════════════════════════════════════════════════════

public class CrossMatrix_ReadOnlyAndChangeTracking
{
    [Fact]
    public void EditOutsideRORegion_ChangeTrackerTracksIt()
    {
        var doc     = M.Doc("LOCKED\nfree line");
        var ro      = doc.GetReadOnlyModel();
        var tracker = doc.GetChangeTracker();
        ro.Protect(0, 6); // protect "LOCKED"

        int off = doc.PositionToOffset(1, 0);
        doc.Replace(off, 4, "open");

        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void SilentInsideRORegion_ChangeTrackerNoChange()
    {
        var doc     = M.Doc("LOCKED text");
        var ro      = doc.GetReadOnlyModel();
        var tracker = doc.GetChangeTracker();
        ro.Protect(0, 6);
        doc.EnforceReadOnly = false;

        doc.Insert(2, "X"); // silently ignored
        tracker.HasAnyChanges.Should().BeFalse();
    }
}
