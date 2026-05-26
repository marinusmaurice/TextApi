using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class UG
{
    public static TextDocument MakeDoc(string content = "")
    {
        var doc = new TextDocument();
        if (content.Length > 0) doc.Load(content);
        return doc;
    }

    /// <summary>
    /// Flush, count undo steps, and restore document to original state via redo.
    /// </summary>
    public static int CountUndoSteps(TextDocument doc)
    {
        doc.FlushUndoGroup();
        int steps = 0;
        while (doc.CanUndo) { doc.Undo(); steps++; }
        // Redo back to original state
        while (doc.CanRedo) doc.Redo();
        return steps;
    }

    public static void TypeString(TextDocument doc, TextCursor cursor, string text)
    {
        foreach (char c in text)
            cursor.InsertText(c.ToString());
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Basic insert coalescing
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_InsertCoalescing
{
    [Fact]
    public void AsciiRun_CoalescedIntoOneStep()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello");
        UG.CountUndoSteps(doc).Should().Be(1);
        doc.GetText().Should().Be("hello");
    }

    [Fact]
    public void AsciiRun_UndoRemovesAll()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello");
        doc.FlushUndoGroup();
        doc.Undo();
        doc.GetText().Should().Be("");
    }

    [Fact]
    public void MultipleWords_CoalescedIntoOneStep()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello world");
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void PasteMultiCluster_IsOwnUndoUnit()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        // "hello" is 5 clusters — paste path
        doc.Insert(0, "hello");
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void SingleCharInsert_ThenPaste_AreTwoSteps()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");          // grouped
        doc.Insert(1, "hello");       // paste — its own unit (flushes first)
        UG.CountUndoSteps(doc).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Backspace coalescing
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_BackspaceCoalescing
{
    [Fact]
    public void BackspaceRun_CoalescedIntoOneStep()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 5);
        for (int i = 0; i < 5; i++) cur.DeleteLeft();
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void BackspaceRun_UndoRestoresAll()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 5);
        for (int i = 0; i < 5; i++) cur.DeleteLeft();
        doc.FlushUndoGroup();
        doc.Undo();
        doc.GetText().Should().Be("hello");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Forward-delete coalescing
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_ForwardDeleteCoalescing
{
    [Fact]
    public void ForwardDeleteRun_CoalescedIntoOneStep()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 0);
        for (int i = 0; i < 5; i++) cur.DeleteRight();
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void ForwardDeleteRun_UndoRestoresAll()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 0);
        for (int i = 0; i < 5; i++) cur.DeleteRight();
        doc.FlushUndoGroup();
        doc.Undo();
        doc.GetText().Should().Be("hello");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Backspace and forward-delete are SEPARATE groups
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_DirectionSeparation
{
    [Fact]
    public void Backspace_ThenForwardDelete_AreTwoSteps()
    {
        var doc = UG.MakeDoc("hello world");
        var cur = new TextCursor(doc, 5); // after "hello"
        cur.DeleteLeft();   // backspace 'o'
        cur.DeleteLeft();   // backspace 'l'
        // Now at offset 3, forward-delete 'l'
        cur.DeleteRight();  // forward-delete 'l' — direction change flushes
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void ForwardDelete_ThenBackspace_AreTwoSteps()
    {
        // Forward-delete 'h' and 'e' from the front, then move cursor to after 'l'
        // and backspace — that must flush and start a new group.
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 0);
        cur.DeleteRight();  // forward-delete 'h' — doc="ello", cursor at 0
        cur.DeleteRight();  // forward-delete 'e' — doc="llo", cursor at 0
        // Move cursor to end of "llo" (offset 3) to enable backspace
        cur.MoveTo(3);      // this flushes the forward-delete group
        cur.DeleteLeft();   // backspace 'o' — new delete group
        // Two separate groups: fwd-delete group + backspace group
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void SwitchingInsertToDelete_FlushesGroup()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.InsertText("b");
        cur.DeleteLeft();   // switch kind → flushes insert group
        UG.CountUndoSteps(doc).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. Unicode insert grouping
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_UnicodeInsert
{
    [Fact]
    public void SingleEmoji_SurrogatePair_IsOneGroupedInsert()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        // Waving hand 👋 = U+1F44B = 2 code units, 1 cluster
        cur.InsertText("\U0001F44B");
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void FamilyEmoji_IsOneGroupedInsert()
    {
        // 👨‍👩‍👧‍👦 = family emoji, 11 code units, 1 grapheme cluster
        string familyEmoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        familyEmoji.Length.Should().Be(11);
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(familyEmoji);
        // 11 code units inserted as one cluster → grouped insert
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void FlagEmoji_IsOneGroupedInsert()
    {
        // 🇺🇸 = U+1F1FA U+1F1F8 = 4 code units, 1 cluster
        string flag = "\U0001F1FA\U0001F1F8";
        flag.Length.Should().Be(4);
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(flag);
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void SkinToneEmoji_IsOneGroupedInsert()
    {
        // 👋🏽 = U+1F44B U+1F3FD = 4 code units, 1 cluster
        string skinTone = "\U0001F44B\U0001F3FD";
        skinTone.Length.Should().Be(4);
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(skinTone);
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void CombiningMark_InsertedTogether_IsOneGroupedInsert()
    {
        // é = 'e' + U+0301 combining acute = 2 code units, 1 cluster
        string combined = "e\u0301";
        combined.Length.Should().Be(2);
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(combined);
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void FiveEmoji_TypedOneByOne_CoalescedIntoOneStep()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        for (int i = 0; i < 5; i++)
            cur.InsertText("\U0001F44B");
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void BackspaceThroughEmoji_IsGrouped()
    {
        // Type 3 wave emojis, backspace all 3
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        for (int i = 0; i < 3; i++) cur.InsertText("\U0001F44B");
        doc.FlushUndoGroup(); // flush the insert group
        for (int i = 0; i < 3; i++) cur.DeleteLeft();
        // Backspace group should be 1 step
        doc.FlushUndoGroup();
        doc.Undo(); // undo backspace group
        doc.GetText().Should().Be("\U0001F44B\U0001F44B\U0001F44B");
    }

    [Fact]
    public void BackspaceThroughFamilyEmoji_IsGrouped()
    {
        string familyEmoji = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(familyEmoji);
        cur.InsertText(familyEmoji);
        doc.FlushUndoGroup();
        cur.DeleteLeft();
        cur.DeleteLeft();
        UG.CountUndoSteps(doc).Should().Be(2); // insert group + backspace group
    }

    [Fact]
    public void BackspaceThroughFlagEmoji_IsGrouped()
    {
        string flag = "\U0001F1FA\U0001F1F8";
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText(flag);
        cur.InsertText(flag);
        doc.FlushUndoGroup();
        cur.DeleteLeft();
        cur.DeleteLeft();
        UG.CountUndoSteps(doc).Should().Be(2); // insert + delete
    }

    [Fact]
    public void MultiClusterInsert_Paste_IsNotGrouped()
    {
        var doc = UG.MakeDoc();
        // "hello" has 5 clusters — treated as paste
        doc.Insert(0, "hello");
        UG.CountUndoSteps(doc).Should().Be(1);
        // But it is still its own unit (not coalesced with prior inserts)
        var doc2 = UG.MakeDoc();
        var cur2 = new TextCursor(doc2);
        cur2.InsertText("a"); // grouped
        doc2.Insert(1, "hello"); // paste — flushes and its own unit
        UG.CountUndoSteps(doc2).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Max group size
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_MaxGroupSize
{
    [Fact]
    public void ExceedingMaxGroupCodeUnits_SplitsIntoTwoUndoUnits()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        // MaxGroupCodeUnits = 200; type 201 'a' chars
        for (int i = 0; i < 201; i++)
            cur.InsertText("a");
        UG.CountUndoSteps(doc).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Flush on cursor navigation
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_FlushOnNavigation
{
    [Fact]
    public void FlushOnMoveTo()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.InsertText("b");
        cur.MoveTo(0);  // flush
        cur.InsertText("c");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnMoveLeft()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.InsertText("b");
        cur.MoveLeft(); // flush
        cur.InsertText("c");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnMoveRight()
    {
        var doc = UG.MakeDoc("x");
        var cur = new TextCursor(doc, 0);
        cur.InsertText("a");
        cur.MoveRight(); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnMoveUp()
    {
        var doc = UG.MakeDoc("line1\nline2");
        var cur = new TextCursor(doc, 11); // end of doc
        cur.InsertText("a");
        cur.MoveUp(); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnMoveDown()
    {
        var doc = UG.MakeDoc("line1\nline2");
        var cur = new TextCursor(doc, 0);
        cur.InsertText("a");
        cur.MoveDown(); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnSelectTo()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.SelectTo(0); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnSelectRight()
    {
        var doc = UG.MakeDoc("x");
        var cur = new TextCursor(doc, 0);
        cur.InsertText("a");
        cur.SelectRight(); // calls SelectTo -> flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnSelectLeft()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.InsertText("b");
        cur.SelectLeft(); // calls SelectTo -> flush
        cur.InsertText("c");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnSelectUp()
    {
        var doc = UG.MakeDoc("line1\nline2");
        var cur = new TextCursor(doc, 11);
        cur.InsertText("a");
        cur.SelectUp(); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnSelectDown()
    {
        var doc = UG.MakeDoc("line1\nline2");
        var cur = new TextCursor(doc, 0);
        cur.InsertText("a");
        cur.SelectDown(); // flush
        cur.InsertText("b");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnReplaceAll()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        doc.ReplaceAll("a", "b"); // flush + its own unit
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void FlushOnLoad()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        doc.Load("new content"); // clears history including pending
        doc.CanUndo.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Undo/Redo flush
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_UndoRedoFlush
{
    [Fact]
    public void Undo_FlushesPendingGroup()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        cur.InsertText("b");
        // Pending group has "ab"; Undo should flush then undo
        doc.Undo();
        doc.GetText().Should().Be("");
    }

    [Fact]
    public void Redo_FlushesPendingGroup()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        doc.FlushUndoGroup();
        doc.Undo(); // undo "a"
        cur.InsertText("b"); // new edit — pending
        doc.Redo();  // flush "b" pending, then redo "a" → impossible since redo cleared
        // After typing "b", redo stack was cleared; redo is no-op
        doc.GetText().Should().Be("b");
    }

    [Fact]
    public void UndoRedo_Symmetry_TypeHello()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello");
        doc.FlushUndoGroup();
        string afterTyping = doc.GetText();
        doc.Undo();
        doc.GetText().Should().Be("");
        doc.Redo();
        doc.GetText().Should().Be(afterTyping);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. Multiple groups
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_MultipleGroups
{
    [Fact]
    public void TypeHello_Move_TypeWorld_TwoGroups()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello");
        cur.MoveTo(5); // flush "hello"
        UG.TypeString(doc, cur, " world");
        UG.CountUndoSteps(doc).Should().Be(2);
    }

    [Fact]
    public void TwoGroups_FirstUndoRemovesSecond_SecondUndoRemovesFirst()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        UG.TypeString(doc, cur, "hello");
        cur.MoveTo(5); // flush
        UG.TypeString(doc, cur, " world");
        doc.FlushUndoGroup();

        doc.Undo(); // undo " world"
        doc.GetText().Should().Be("hello");
        doc.Undo(); // undo "hello"
        doc.GetText().Should().Be("");
    }

    [Fact]
    public void TypingOverSelection_IsOwnGroup()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 0);
        cur.SetSelection(0, 5); // select all
        cur.InsertText("world"); // replaces selection → Replace(0,5,"world") but "world" is 5 clusters = paste
        // The replace with deleteLength > 0 goes through Replace() which calls _history.Execute()
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void DeleteWordLeft_IsOwnGroup()
    {
        var doc = UG.MakeDoc("hello world");
        var cur = new TextCursor(doc, 11);
        cur.DeleteWordLeft(); // "world" = multi-cluster → own unit
        UG.CountUndoSteps(doc).Should().Be(1);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. CanUndo reflects pending group
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_CanUndoReflectsPending
{
    [Fact]
    public void CanUndo_TrueEvenBeforeFlush()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        doc.CanUndo.Should().BeFalse();
        cur.InsertText("a"); // pending but not flushed
        doc.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void CanUndo_FalseOnFreshDoc()
    {
        var doc = UG.MakeDoc("hello");
        doc.CanUndo.Should().BeFalse();
    }

    [Fact]
    public void CanRedo_FalseWhilePending()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a");
        doc.FlushUndoGroup();
        doc.Undo();
        doc.CanRedo.Should().BeTrue();
        // Now type again — kills redo
        cur.InsertText("b");
        doc.CanRedo.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 11. DeleteSelection flush
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_DeleteSelection
{
    [Fact]
    public void DeleteSelection_FlushesAndIsOwnUnit()
    {
        var doc = UG.MakeDoc("hello");
        var cur = new TextCursor(doc, 0);
        cur.InsertText("a"); // pending insert
        cur.SetSelection(0, 3); // flush "a" insert
        cur.DeleteSelection();  // flush (no-op since no pending), delete is multi-cluster or via Delete()
        // "a" insert + delete selection = 2 undo steps
        UG.CountUndoSteps(doc).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 12. Replace with deleteLength > 0 is own group
// ═══════════════════════════════════════════════════════════════════════════

public class UndoGrouping_ReplaceOwnGroup
{
    [Fact]
    public void Replace_NonZeroDelete_IsOwnUndoUnit()
    {
        var doc = UG.MakeDoc("hello");
        doc.Replace(0, 5, "world");
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void Replace_ZeroDelete_DelegatesToInsert_AndGroups()
    {
        var doc = UG.MakeDoc();
        var cur = new TextCursor(doc);
        cur.InsertText("a"); // pending
        doc.Replace(1, 0, "b"); // zero-delete → Insert(1,"b") → single cluster → groups
        UG.CountUndoSteps(doc).Should().Be(1);
    }

    [Fact]
    public void Replace_ZeroInsert_DelegatesToDelete_AndGroups()
    {
        var doc = UG.MakeDoc("ab");
        var cur = new TextCursor(doc, 2);
        cur.DeleteLeft(); // pending delete of 'b'
        doc.Replace(0, 1, ""); // zero-insert → Delete(0,1) → single cluster → groups with prior?
        // Note: direction changes (offset 0 + length 1 = 1, prior _deleteBack = 1), so joins as backspace
        UG.CountUndoSteps(doc).Should().Be(1);
    }
}
