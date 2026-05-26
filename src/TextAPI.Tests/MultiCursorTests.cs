using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class MC
{
    public static (TextDocument doc, MultiCursor mc) Make(string text = "", int offset = 0)
    {
        var doc = new TextDocument();
        if (!string.IsNullOrEmpty(text)) doc.Load(text);  // Load clears history — no spurious undo step
        var mc = new MultiCursor(doc);
        if (offset != 0) mc.SetSingle(offset);
        return (doc, mc);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Construction and collection management
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorConstructionTests
{
    [Fact] public void Default_SingleCursorAtZero()
    {
        var (_, mc) = MC.Make("hello");
        mc.Count.Should().Be(1);
        mc.Primary.CaretOffset.Should().Be(0);
    }

    [Fact] public void AddCursor_IncrementsCount()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.Count.Should().Be(2);
    }

    [Fact] public void AddCursor_BecomesPrimary()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.Primary.CaretOffset.Should().Be(6);
    }

    [Fact] public void AddCursor_SortedAscending()
    {
        var (_, mc) = MC.Make("hello world foo");
        mc.AddCursor(12);
        mc.AddCursor(6);
        mc.All[0].CaretOffset.Should().Be(0);
        mc.All[1].CaretOffset.Should().Be(6);
        mc.All[2].CaretOffset.Should().Be(12);
    }

    [Fact] public void AddCursorWithSelection()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6, 11);
        mc.Primary.SelectionStart.Should().Be(6);
        mc.Primary.SelectionEnd.Should().Be(11);
        mc.Primary.SelectedText.Should().Be("world");
    }

    [Fact] public void RemoveCursor_DecreasesCount()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.RemoveCursor(0);
        mc.Count.Should().Be(1);
    }

    [Fact] public void RemoveCursor_CannotRemoveLastCursor()
    {
        var (_, mc) = MC.Make("hello");
        mc.RemoveCursor(0);
        mc.Count.Should().Be(1, "always at least one cursor");
    }

    [Fact] public void Clear_ResetsToSingleCursorAtZero()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.AddCursor(11);
        mc.Clear();
        mc.Count.Should().Be(1);
        mc.Primary.CaretOffset.Should().Be(0);
    }

    [Fact] public void SetSingle_ReplacesCursors()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.SetSingle(3);
        mc.Count.Should().Be(1);
        mc.Primary.CaretOffset.Should().Be(3);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Cursor merging
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorMergeTests
{
    [Fact] public void SameOffset_MergestoOne()
    {
        var (_, mc) = MC.Make("hello");
        mc.AddCursor(0);   // same as default
        mc.Count.Should().Be(1);
    }

    [Fact] public void OverlappingSelections_MergeToUnion()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(0, 6);    // [0,6)
        mc.AddCursor(3, 9);    // [3,9) overlaps
        mc.Count.Should().Be(1);
        mc.Primary.SelectionStart.Should().Be(0);
        mc.Primary.SelectionEnd.Should().Be(9);
    }

    [Fact] public void AdjacentSelections_DoNotMerge()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(0, 5);    // [0,5)
        mc.AddCursor(5, 11);   // [5,11) — starts where first ends
        mc.Count.Should().Be(2, "adjacent non-overlapping selections stay separate");
    }

    [Fact] public void CollapsedInsideSelection_Absorbed()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(0, 11);   // select all
        mc.AddCursor(5);       // collapsed inside
        mc.Count.Should().Be(1, "collapsed cursor inside selection is absorbed");
    }

    [Fact] public void SelectAll_CollapsesCursorsToOne()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.SelectAll();
        mc.Count.Should().Be(1);
    }

    [Fact] public void MoveDocumentStart_CollapsesCursorsToOne()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.AddCursor(11);
        mc.MoveToDocumentStart();
        mc.Count.Should().Be(1, "all cursors moved to 0 → merge");
        mc.Primary.CaretOffset.Should().Be(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Movement — all cursors move
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorMovementTests
{
    [Fact] public void MoveRight_AllCursorsAdvance()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.MoveRight();
        mc.All[0].CaretOffset.Should().Be(1);
        mc.All[1].CaretOffset.Should().Be(7);
    }

    [Fact] public void MoveLeft_AllCursorsRetreat()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.MoveLeft();
        mc.All[0].CaretOffset.Should().Be(0, "already at start, clamped");
        mc.All[1].CaretOffset.Should().Be(5);
    }

    [Fact] public void MoveWordRight_AllCursors()
    {
        var (_, mc) = MC.Make("hello world foo");
        mc.AddCursor(6);
        mc.MoveWordRight();
        // cursor 0 at 0: skip "hello " → 6
        // cursor 1 at 6: skip "world " → 12
        mc.All[0].CaretOffset.Should().Be(6);
        mc.All[1].CaretOffset.Should().Be(12);
    }

    [Fact] public void MoveToLineEnd_AllCursors_MultiLine()
    {
        var (doc, mc) = MC.Make("hello\nworld");
        mc.AddCursor(6);    // start of "world"
        mc.MoveToLineEnd();
        mc.All[0].CaretOffset.Should().Be(5,  "end of 'hello'");
        mc.All[1].CaretOffset.Should().Be(11, "end of 'world'");
    }

    [Fact] public void MoveUp_AllCursors()
    {
        var (doc, mc) = MC.Make("hello\nworld\nfoo");
        mc.AddCursor(doc.Length);   // end of "foo" (line 2)
        mc.MoveUp();
        mc.All[0].CaretLine.Should().Be(0);   // was line 0, up → clamped at 0
        mc.All[1].CaretLine.Should().Be(1);   // was line 2, up → line 1
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Selection operations
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorSelectionTests
{
    [Fact] public void SelectRight_ExtendsBothCursors()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.SelectRight(3);
        mc.All[0].HasSelection.Should().BeTrue();
        mc.All[0].SelectedText.Should().Be("hel");
        mc.All[1].SelectedText.Should().Be("wor");
    }

    [Fact] public void SelectWordAtCaret_EachCursorGetsItsWord()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(7);   // inside "world"
        mc.SelectWordAtCaret();
        mc.All[0].SelectedText.Should().Be("hello");
        mc.All[1].SelectedText.Should().Be("world");
    }

    [Fact] public void SelectLine_EachCursorGetsItsLine()
    {
        var (_, mc) = MC.Make("hello\nworld");
        mc.AddCursor(6);
        mc.SelectLine();
        mc.All[0].SelectedText.Should().Be("hello\n");
        mc.All[1].SelectedText.Should().Be("world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. InsertText — multi-cursor editing
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorInsertTests
{
    [Fact] public void Insert_SingleCursor_Works()
    {
        var (doc, mc) = MC.Make("hello");
        mc.SetSingle(5);
        mc.InsertText(" world");
        doc.GetText().Should().Be("hello world");
        mc.Primary.CaretOffset.Should().Be(11);
    }

    [Fact] public void Insert_TwoCursors_BothPositionsUpdated()
    {
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(6);   // cursor 0 at 0, cursor 1 at 6
        mc.All[0].MoveTo(3);   // cursor 0 at 3 (after "foo")
        mc.InsertText("X");
        doc.GetText().Should().Be("fooXbarX");
        mc.All[0].CaretOffset.Should().Be(4);
        mc.All[1].CaretOffset.Should().Be(8);
    }

    [Fact] public void Insert_ThreeCursors_AllAdjusted()
    {
        var (doc, mc) = MC.Make("abcdef");
        mc.AddCursor(2);
        mc.AddCursor(4);
        // cursors at 0, 2, 4
        mc.InsertText("-");
        doc.GetText().Should().Be("-ab-cd-ef");
        mc.All[0].CaretOffset.Should().Be(1);
        mc.All[1].CaretOffset.Should().Be(4);
        mc.All[2].CaretOffset.Should().Be(7);
    }

    [Fact] public void Insert_ReplacesSelection_AtEachCursor()
    {
        var (doc, mc) = MC.Make("hello world");
        mc.AddCursor(6, 11);  // select "world"
        mc.All[0].SetSelection(0, 5);  // select "hello"
        mc.InsertText("HI");
        doc.GetText().Should().Be("HI HI");
    }

    [Fact] public void Insert_CrLfNormalized()
    {
        var (doc, mc) = MC.Make("ab");
        mc.SetSingle(1);
        mc.InsertText("\r\n");
        doc.GetText().Should().Be("a\nb");
        mc.Primary.CaretOffset.Should().Be(2);
    }

    [Fact] public void Insert_EmptyString_NoChange()
    {
        var (doc, mc) = MC.Make("hello");
        mc.AddCursor(3);
        mc.InsertText("");
        doc.GetText().Should().Be("hello");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. DeleteLeft / DeleteRight
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorDeleteTests
{
    [Fact] public void DeleteLeft_TwoCursors()
    {
        // "foobar": cursor A at 3 (delete 'o' at offset 2), cursor B at 6 (delete 'r' at offset 5)
        // Both deletions applied → "foba" (length 4)
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(6);   // cursor 0 at 0, cursor 1 at 6
        mc.All[0].MoveTo(3);
        mc.DeleteLeft();
        doc.GetText().Should().Be("foba");
        mc.All[0].CaretOffset.Should().Be(2);
        mc.All[1].CaretOffset.Should().Be(4);
    }

    [Fact] public void DeleteLeft_AtStart_NoChange()
    {
        var (doc, mc) = MC.Make("hello");
        mc.DeleteLeft();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void DeleteRight_TwoCursors()
    {
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(3);
        // cursors at 0 and 3
        mc.DeleteRight();
        doc.GetText().Should().Be("ooar");
        mc.All[0].CaretOffset.Should().Be(0);
        mc.All[1].CaretOffset.Should().Be(2);
    }

    [Fact] public void DeleteRight_AtEnd_NoChange()
    {
        var (doc, mc) = MC.Make("hello");
        mc.SetSingle(5);
        mc.DeleteRight();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void DeleteLeft_WithSelection_DeletesSelection()
    {
        var (doc, mc) = MC.Make("hello world");
        mc.AddCursor(6, 11);   // selects "world"
        mc.All[0].SetSelection(0, 5);  // selects "hello"
        mc.DeleteLeft();
        doc.GetText().Should().Be(" ");
    }

    [Fact] public void DeleteWordRight_TwoCursors()
    {
        var (doc, mc) = MC.Make("foo bar baz");
        mc.AddCursor(4);
        // cursor 0 at 0, cursor 1 at 4
        mc.DeleteWordRight();
        // cursor 0 deletes "foo " (word then space = 4 chars → offset 0, deletes chars 0..3)
        // cursor 1 deletes "bar " (4 chars → offset 4-4=0, but adjusted: 4-4=0... wait
        // Let me recalculate: "foo bar baz"
        // cursor 0 at 0: WordRight(0) = skip "foo" + " " = 4, delete [0,4) = "foo "
        // cursor 1 at 4: WordRight(4) = skip "bar" + " " = 8, delete [4,8) = "bar "
        // After deleting "bar " first (bottom-to-top): "foo baz"
        // Then delete "foo ": "baz"
        doc.GetText().Should().Be("baz");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Single undo unit
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorUndoTests
{
    [Fact] public void InsertAtTwoCursors_IsOneUndoStep()
    {
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(6);
        mc.All[0].MoveTo(3);
        mc.InsertText("X");
        doc.GetText().Should().Be("fooXbarX");

        doc.CanUndo.Should().BeTrue();
        doc.Undo();
        doc.GetText().Should().Be("foobar", "single undo reverts both insertions");
        doc.CanUndo.Should().BeFalse("only one undo step was pushed");
    }

    [Fact] public void DeleteAtTwoCursors_IsOneUndoStep()
    {
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(3);
        mc.DeleteRight();   // deletes 'f' (at 0) and 'b' (at 3, shifted to 2 after first)
        string after = doc.GetText();
        doc.Undo();
        doc.GetText().Should().Be("foobar");
        doc.CanUndo.Should().BeFalse();
    }

    [Fact] public void MultipleInserts_EachIsOwnUndoStep()
    {
        var (doc, mc) = MC.Make("abc");
        mc.InsertText("1");   // step 1
        mc.InsertText("2");   // step 2
        doc.Undo();           // revert step 2
        doc.GetText().Should().Be("1abc");
        doc.Undo();           // revert step 1
        doc.GetText().Should().Be("abc");
    }

    [Fact] public void UndoRevertsCrlfNormalized()
    {
        var (doc, mc) = MC.Make("ab");
        mc.SetSingle(1);
        mc.InsertText("\r\n");
        doc.GetText().Should().Be("a\nb");
        doc.Undo();
        doc.GetText().Should().Be("ab");
    }

    [Fact] public void Redo_AfterUndo()
    {
        var (doc, mc) = MC.Make("foobar");
        mc.AddCursor(6);
        mc.All[0].MoveTo(3);
        mc.InsertText("X");
        doc.Undo();
        doc.Redo();
        doc.GetText().Should().Be("fooXbarX");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Primary cursor tracking
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorPrimaryTests
{
    [Fact] public void LastAdded_IsPrimary()
    {
        var (_, mc) = MC.Make("hello world foo");
        mc.AddCursor(6);
        mc.AddCursor(12);
        mc.Primary.CaretOffset.Should().Be(12);
    }

    [Fact] public void AfterMerge_PrimaryRemainsValid()
    {
        var (_, mc) = MC.Make("hello");
        mc.AddCursor(3);
        mc.MoveToDocumentStart();   // all → 0 → merge to 1
        mc.Count.Should().Be(1);
        mc.Primary.Should().NotBeNull();
        mc.Primary.CaretOffset.Should().Be(0);
    }

    [Fact] public void Clear_PrimaryIsZero()
    {
        var (_, mc) = MC.Make("hello world");
        mc.AddCursor(6);
        mc.Clear();
        mc.Primary.CaretOffset.Should().Be(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. Edge cases and invariants
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorEdgeCaseTests
{
    [Fact] public void EmptyDoc_AllOpsNoThrow()
    {
        var (_, mc) = MC.Make("");
        mc.AddCursor(0);
        mc.MoveLeft(); mc.MoveRight(); mc.MoveUp(); mc.MoveDown();
        mc.MoveWordLeft(); mc.MoveWordRight();
        mc.InsertText(""); mc.DeleteLeft(); mc.DeleteRight();
        mc.DeleteWordLeft(); mc.DeleteWordRight();
        mc.Count.Should().BeGreaterThan(0);
    }

    [Fact] public void Insert_ManyTimes_OffsetAlwaysValid()
    {
        var (doc, mc) = MC.Make("abcdef");
        mc.AddCursor(3);
        for (int i = 0; i < 10; i++)
        {
            mc.InsertText("X");
            foreach (var c in mc.All)
            {
                c.CaretOffset.Should().BeInRange(0, doc.Length);
                c.SelectionStart.Should().BeInRange(0, doc.Length);
                c.SelectionEnd.Should().BeInRange(0, doc.Length);
            }
        }
    }

    [Fact] public void Cursors_AlwaysSortedAfterEdits()
    {
        var (_, mc) = MC.Make("hello world foo bar");
        mc.AddCursor(6);
        mc.AddCursor(10);
        mc.AddCursor(14);
        mc.MoveRight(2);
        for (int i = 1; i < mc.Count; i++)
            mc.All[i].CaretOffset.Should().BeGreaterOrEqualTo(mc.All[i - 1].CaretOffset,
                "cursors must remain sorted ascending");
    }

    [Fact] public void Cursors_NeverOverlapAfterEdits()
    {
        var (_, mc) = MC.Make("hello world foo bar");
        mc.AddCursor(6);
        mc.AddCursor(10);
        mc.SelectWordAtCaret();
        for (int i = 1; i < mc.Count; i++)
            mc.All[i].SelectionStart.Should().BeGreaterOrEqualTo(mc.All[i - 1].SelectionEnd,
                "cursor selections must not overlap");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. Fuzz — offsets always valid under random operations
// ═══════════════════════════════════════════════════════════════════════════

public class MultiCursorFuzzTests
{
    private static (TextDocument doc, MultiCursor mc) Setup(string text, int[] offsets)
    {
        var doc = new TextDocument();
        doc.Load(text);   // Load clears history — no spurious undo step
        var mc = new MultiCursor(doc);
        foreach (var o in offsets) mc.AddCursor(o);
        return (doc, mc);
    }

    [Theory]
    [InlineData(1001, 200)]
    [InlineData(1002, 200)]
    [InlineData(1003, 200)]
    public void RandomEdits_CursorsAlwaysValid(int seed, int ops)
    {
        var rng = new Random(seed);
        var (doc, mc) = Setup("hello world foo bar baz", [5, 11, 15]);

        for (int i = 0; i < ops; i++)
        {
            int op = rng.Next(8);
            switch (op)
            {
                case 0: mc.InsertText(((char)('a' + rng.Next(26))).ToString()); break;
                case 1: mc.DeleteLeft(); break;
                case 2: mc.DeleteRight(); break;
                case 3: mc.MoveLeft(); break;
                case 4: mc.MoveRight(); break;
                case 5: mc.SelectRight(rng.Next(1, 5)); break;
                case 6: mc.InsertText("\n"); break;
                case 7:
                    if (doc.Length > 0)
                    {
                        int offset = rng.Next(0, doc.Length + 1);
                        mc.AddCursor(offset);
                    }
                    break;
            }

            mc.Count.Should().BeGreaterThan(0);
            for (int j = 0; j < mc.Count; j++)
            {
                var c = mc.All[j];
                c.CaretOffset.Should().BeInRange(0, doc.Length, $"caret out of range, op={i}");
                c.SelectionStart.Should().BeInRange(0, doc.Length);
                c.SelectionEnd.Should().BeInRange(0, doc.Length);
                c.SelectionStart.Should().BeLessOrEqualTo(c.SelectionEnd);
                if (j > 0)
                    mc.All[j].SelectionStart.Should().BeGreaterOrEqualTo(
                        mc.All[j - 1].SelectionEnd, $"overlap at op={i} cursors {j-1},{j}");
            }
        }
    }

    [Theory]
    [InlineData(2001, 50)]
    [InlineData(2002, 50)]
    public void RandomEdits_UndoRestoresDocument(int seed, int ops)
    {
        var rng = new Random(seed);
        var (doc, mc) = Setup("the quick brown fox", [4, 10]);
        string initial = doc.GetText();

        for (int i = 0; i < ops; i++)
        {
            switch (rng.Next(4))
            {
                case 0: mc.InsertText(((char)('a' + rng.Next(26))).ToString()); break;
                case 1: mc.DeleteLeft(); break;
                case 2: mc.DeleteRight(); break;
                case 3: mc.MoveRight(); break;
            }
        }

        while (doc.CanUndo) doc.Undo();
        doc.GetText().Should().Be(initial, "full undo must restore original text");
    }
}
