using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.Cursor;
using Xunit;
using Xunit.Abstractions;

namespace TextEditor.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class C
{
    /// <summary>Create a TextDocument loaded with content and a cursor at offset 0.</summary>
    public static (TextDocument Doc, TextCursor Cur) Make(string content, int offset = 0)
    {
        var doc = new TextDocument();
        doc.Load(content);
        return (doc, new TextCursor(doc, offset));
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Construction and basic properties
// ═══════════════════════════════════════════════════════════════════════════

public class CursorConstructionTests
{
    [Fact] public void DefaultOffset_IsZero()
    {
        var (_, cur) = C.Make("Hello");
        cur.CaretOffset.Should().Be(0);
        cur.AnchorOffset.Should().Be(0);
        cur.ActiveOffset.Should().Be(0);
    }

    [Fact] public void InitialOffset_Respected()
    {
        var (_, cur) = C.Make("Hello", 3);
        cur.CaretOffset.Should().Be(3);
    }

    [Fact] public void InitialOffset_ClampedToLength()
    {
        var (_, cur) = C.Make("Hello", 999);
        cur.CaretOffset.Should().Be(5);
    }

    [Fact] public void NegativeOffset_ClampedToZero()
    {
        var (_, cur) = C.Make("Hello", -10);
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void HasSelection_FalseInitially()
    {
        var (_, cur) = C.Make("Hello");
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void SelectedText_EmptyInitially()
    {
        var (_, cur) = C.Make("Hello");
        cur.SelectedText.Should().Be(string.Empty);
    }

    [Fact] public void Document_ReturnsWrappedDoc()
    {
        var (doc, cur) = C.Make("Hello");
        cur.Document.Should().BeSameAs(doc);
    }

    [Fact] public void EmptyDocument_OffsetZero()
    {
        var (_, cur) = C.Make("");
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void CaretLine_Column_FromOffsetToPosition()
    {
        var (_, cur) = C.Make("Hello\nWorld", 7);
        cur.CaretLine.Should().Be(1);
        cur.CaretColumn.Should().Be(1);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. MoveTo / SelectTo / SetSelection
// ═══════════════════════════════════════════════════════════════════════════

public class CursorDirectPositionTests
{
    [Fact] public void MoveTo_SetsCaretAndCollapsesSelection()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SelectRight(5);
        cur.HasSelection.Should().BeTrue();
        cur.MoveTo(3);
        cur.CaretOffset.Should().Be(3);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void MoveTo_ClampedToDocumentBounds()
    {
        var (_, cur) = C.Make("Hello");
        cur.MoveTo(1000);
        cur.CaretOffset.Should().Be(5);
        cur.MoveTo(-5);
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void SelectTo_MovesActiveKeepsAnchor()
    {
        var (_, cur) = C.Make("Hello World");
        cur.MoveTo(3);
        cur.SelectTo(8);
        cur.AnchorOffset.Should().Be(3);
        cur.ActiveOffset.Should().Be(8);
        cur.HasSelection.Should().BeTrue();
    }

    [Fact] public void SelectTo_Backward_SelectionStartLessThanEnd()
    {
        var (_, cur) = C.Make("Hello World");
        cur.MoveTo(8);
        cur.SelectTo(3);
        cur.SelectionStart.Should().Be(3);
        cur.SelectionEnd.Should().Be(8);
        cur.AnchorOffset.Should().Be(8);
        cur.ActiveOffset.Should().Be(3);
    }

    [Fact] public void SetSelection_SetsAnchorAndActive()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(2, 7);
        cur.AnchorOffset.Should().Be(2);
        cur.ActiveOffset.Should().Be(7);
        cur.SelectionStart.Should().Be(2);
        cur.SelectionEnd.Should().Be(7);
    }

    [Fact] public void SetSelection_BothClamped()
    {
        var (_, cur) = C.Make("Hello");
        cur.SetSelection(-5, 999);
        cur.AnchorOffset.Should().Be(0);
        cur.ActiveOffset.Should().Be(5);
    }

    [Fact] public void CollapseToStart_CollapsesToLeft()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(3, 8);
        cur.CollapseToStart();
        cur.CaretOffset.Should().Be(3);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void CollapseToEnd_CollapsesToRight()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(3, 8);
        cur.CollapseToEnd();
        cur.CaretOffset.Should().Be(8);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void CollapseToStart_BackwardSelection()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(8, 3);   // anchor=8, active=3
        cur.CollapseToStart();
        cur.CaretOffset.Should().Be(3);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Horizontal movement
// ═══════════════════════════════════════════════════════════════════════════

public class CursorHorizontalMoveTests
{
    [Fact] public void MoveLeft_DecrementsOffset()
    {
        var (_, cur) = C.Make("Hello", 3);
        cur.MoveLeft();
        cur.CaretOffset.Should().Be(2);
    }

    [Fact] public void MoveLeft_AtZero_NoOp()
    {
        var (_, cur) = C.Make("Hello", 0);
        cur.MoveLeft();
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void MoveLeft_WithSelection_CollapsesToStart()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(3, 8);
        cur.MoveLeft();
        cur.CaretOffset.Should().Be(3);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void MoveLeft_Count5_Moves5()
    {
        var (_, cur) = C.Make("Hello World", 8);
        cur.MoveLeft(5);
        cur.CaretOffset.Should().Be(3);
    }

    [Fact] public void MoveRight_IncrementsOffset()
    {
        var (_, cur) = C.Make("Hello", 2);
        cur.MoveRight();
        cur.CaretOffset.Should().Be(3);
    }

    [Fact] public void MoveRight_AtEnd_NoOp()
    {
        var (_, cur) = C.Make("Hello", 5);
        cur.MoveRight();
        cur.CaretOffset.Should().Be(5);
    }

    [Fact] public void MoveRight_WithSelection_CollapsesToEnd()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SetSelection(3, 8);
        cur.MoveRight();
        cur.CaretOffset.Should().Be(8);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void MoveRight_Count5_Moves5()
    {
        var (_, cur) = C.Make("Hello World", 3);
        cur.MoveRight(5);
        cur.CaretOffset.Should().Be(8);
    }

    [Fact] public void MoveToLineStart_GoesToColumn0()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);   // in "World"
        cur.MoveToLineStart();
        cur.CaretOffset.Should().Be(6);
        cur.CaretColumn.Should().Be(0);
    }

    [Fact] public void MoveToLineStart_FirstLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 4);
        cur.MoveToLineStart();
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void MoveToLineEnd_GoesAfterLastChar()
    {
        var (_, cur) = C.Make("Hello\nWorld", 7);   // in "World"
        cur.MoveToLineEnd();
        cur.CaretOffset.Should().Be(11);   // end of "World"
        cur.CaretColumn.Should().Be(5);
    }

    [Fact] public void MoveToLineEnd_EmptyLine_StaysAtLineStart()
    {
        var (_, cur) = C.Make("Hello\n\nWorld", 6);   // empty second line
        cur.MoveToLineEnd();
        cur.CaretOffset.Should().Be(6);
    }

    [Fact] public void MoveToDocumentStart_GoesToZero()
    {
        var (_, cur) = C.Make("Hello World", 8);
        cur.MoveToDocumentStart();
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void MoveToDocumentEnd_GoesToLength()
    {
        var (doc, cur) = C.Make("Hello World", 3);
        cur.MoveToDocumentEnd();
        cur.CaretOffset.Should().Be(doc.Length);
    }

    [Fact] public void HorizontalMove_ClearsPreferredColumn()
    {
        // After MoveUp sets a preferred column, MoveLeft clears it.
        // Subsequent MoveUp should re-derive column from current position.
        var (_, cur) = C.Make("Hello\nWo\nWorld", 5);   // end of "Hello"
        cur.MoveDown();   // sets preferred col = 5 → clamps to 2 on "Wo"
        cur.CaretColumn.Should().Be(2, "clamped to end of 'Wo'");
        cur.MoveLeft();   // clears preferred column
        cur.MoveUp();     // re-derives from current column (1), goes to "Hello" col 1
        cur.CaretLine.Should().Be(0);
        cur.CaretColumn.Should().Be(1);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Vertical movement
// ═══════════════════════════════════════════════════════════════════════════

public class CursorVerticalMoveTests
{
    [Fact] public void MoveUp_GoesToPreviousLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 7);   // col 1 of "World"
        cur.MoveUp();
        cur.CaretLine.Should().Be(0);
        cur.CaretColumn.Should().Be(1);
    }

    [Fact] public void MoveDown_GoesToNextLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 3);   // col 3 of "Hello"
        cur.MoveDown();
        cur.CaretLine.Should().Be(1);
        cur.CaretColumn.Should().Be(3);
    }

    [Fact] public void MoveUp_AtTopLine_GoesToOffset0()
    {
        var (_, cur) = C.Make("Hello\nWorld", 3);
        cur.MoveUp();
        cur.CaretOffset.Should().Be(3);   // stayed on line 0, same column
        cur.MoveUp();
        cur.CaretOffset.Should().Be(3);   // can't go further up
    }

    [Fact] public void MoveDown_AtBottomLine_StaysOnLastLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 8);
        cur.MoveDown();
        cur.CaretLine.Should().Be(1);   // stays on last line
    }

    [Fact] public void MoveUp_PreservesPreferredColumn_ThroughShortLine()
    {
        // Lines: "Hello World" (11 chars), "Hi" (2 chars), "Hello World" again
        var content = "Hello World\nHi\nHello World";
        var (_, cur) = C.Make(content, 26);   // col 11 of last line (offset = 15 + 11 = 26 = doc.Length)
        cur.CaretColumn.Should().Be(11);
        cur.MoveUp();
        cur.CaretLine.Should().Be(1);
        cur.CaretColumn.Should().Be(2, "clamped to end of short 'Hi' line");
        cur.MoveUp();
        cur.CaretLine.Should().Be(0);
        cur.CaretColumn.Should().Be(11, "preferred column restored on long line");
    }

    [Fact] public void MoveDown_PreservesPreferredColumn_ThroughShortLine()
    {
        var content = "Hello World\nHi\nHello World";
        var (_, cur) = C.Make(content, 11);   // end of first line
        cur.MoveDown();
        cur.CaretLine.Should().Be(1);
        cur.CaretColumn.Should().Be(2, "clamped to end of 'Hi'");
        cur.MoveDown();
        cur.CaretLine.Should().Be(2);
        cur.CaretColumn.Should().Be(11, "preferred column snaps back");
    }

    [Fact] public void MoveUp_CollapsesSelection()
    {
        var (_, cur) = C.Make("Hello\nWorld", 7);
        cur.SelectRight(3);
        cur.HasSelection.Should().BeTrue();
        cur.MoveUp();
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void MoveDown_CollapsesSelection()
    {
        var (_, cur) = C.Make("Hello\nWorld", 3);
        cur.SelectLeft(2);
        cur.HasSelection.Should().BeTrue();
        cur.MoveDown();
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void MoveUp_Count2_SkipsTwoLines()
    {
        var (_, cur) = C.Make("A\nB\nC\nD", 4);   // 'C' is at offset 4 (line 2)
        cur.MoveUp(2);
        cur.CaretLine.Should().Be(0);
    }

    [Fact] public void MoveDown_Count2_SkipsTwoLines()
    {
        var (_, cur) = C.Make("A\nB\nC\nD", 0);
        cur.MoveDown(2);
        cur.CaretLine.Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. Word movement
// ═══════════════════════════════════════════════════════════════════════════

public class CursorWordMoveTests
{
    [Fact] public void MoveWordRight_SkipsWordAndTrailingSpace()
    {
        var (_, cur) = C.Make("hello world foo");
        cur.MoveWordRight();
        cur.CaretOffset.Should().Be(6, "skip 'hello' (word) + ' ' (trailing non-word) = 6 = start of 'world'");
    }

    [Fact] public void MoveWordRight_FromInsideWord_SkipsToStartOfNextWord()
    {
        var (_, cur) = C.Make("hello world", 2);   // inside "hello"
        cur.MoveWordRight();
        cur.CaretOffset.Should().Be(6, "skip 'llo' + space → start of 'world'");
    }

    [Fact] public void MoveWordRight_AtEnd_StaysAtEnd()
    {
        var (_, cur) = C.Make("hello", 5);
        cur.MoveWordRight();
        cur.CaretOffset.Should().Be(5);
    }

    [Fact] public void MoveWordLeft_SkipsBackToWordStart()
    {
        var (_, cur) = C.Make("hello world", 11);
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(6, "from end of 'world' → start of 'world'");
    }

    [Fact] public void MoveWordLeft_FromStartOfWord_SkipsToPreviousWordStart()
    {
        var (_, cur) = C.Make("hello world", 6);   // at start of "world"
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(0, "skip space + 'hello' → start of 'hello'");
    }

    [Fact] public void MoveWordLeft_AtStart_StaysAtZero()
    {
        var (_, cur) = C.Make("hello", 0);
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void MoveWordRight_SkipsLeadingPunctuation()
    {
        var (_, cur) = C.Make("   hello");
        cur.MoveWordRight();
        cur.CaretOffset.Should().Be(3, "on non-word: skip non-word chars to next word start (3 spaces → land at 'h')");
    }

    [Fact] public void MoveWordLeft_SkipsTrailingPunctuation()
    {
        var (_, cur) = C.Make("hello   ", 8);
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(0, "skip spaces, then skip 'hello'");
    }

    [Fact] public void MoveWordRight_ThenLeft_RoundTrip()
    {
        var (_, cur) = C.Make("hello world foo", 0);
        cur.MoveWordRight();
        int afterFirst = cur.CaretOffset;
        cur.MoveWordRight();
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(afterFirst);
    }

    [Fact] public void WordLeft_Public_MatchesMoveWordLeft()
    {
        var (_, cur) = C.Make("hello world", 11);
        int expected = cur.WordLeft(11);
        cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(expected);
    }

    [Fact] public void WordRight_Public_MatchesMoveWordRight()
    {
        var (_, cur) = C.Make("hello world", 0);
        int expected = cur.WordRight(0);
        cur.MoveWordRight();
        cur.CaretOffset.Should().Be(expected);
    }

    [Fact] public void IsWordChar_Letters_True()
    {
        TextCursor.IsWordChar('a').Should().BeTrue();
        TextCursor.IsWordChar('Z').Should().BeTrue();
    }

    [Fact] public void IsWordChar_Digits_True()
        => TextCursor.IsWordChar('9').Should().BeTrue();

    [Fact] public void IsWordChar_Underscore_True()
        => TextCursor.IsWordChar('_').Should().BeTrue();

    [Fact] public void IsWordChar_Space_False()
        => TextCursor.IsWordChar(' ').Should().BeFalse();

    [Fact] public void IsWordChar_Newline_False()
        => TextCursor.IsWordChar('\n').Should().BeFalse();

    [Fact] public void IsWordChar_Punctuation_False()
        => TextCursor.IsWordChar('.').Should().BeFalse();
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. Horizontal selection
// ═══════════════════════════════════════════════════════════════════════════

public class CursorHorizontalSelectTests
{
    [Fact] public void SelectRight_CreatesForwardSelection()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SelectRight(5);
        cur.AnchorOffset.Should().Be(0);
        cur.ActiveOffset.Should().Be(5);
        cur.HasSelection.Should().BeTrue();
        cur.SelectedText.Should().Be("Hello");
    }

    [Fact] public void SelectLeft_CreatesBackwardSelection()
    {
        var (_, cur) = C.Make("Hello World", 5);
        cur.SelectLeft(5);
        cur.AnchorOffset.Should().Be(5);
        cur.ActiveOffset.Should().Be(0);
        cur.SelectionStart.Should().Be(0);
        cur.SelectionEnd.Should().Be(5);
        cur.SelectedText.Should().Be("Hello");
    }

    [Fact] public void SelectLeft_ClampedAtZero()
    {
        var (_, cur) = C.Make("Hello", 2);
        cur.SelectLeft(100);
        cur.ActiveOffset.Should().Be(0);
    }

    [Fact] public void SelectRight_ClampedAtLength()
    {
        var (_, cur) = C.Make("Hello", 3);
        cur.SelectRight(100);
        cur.ActiveOffset.Should().Be(5);
    }

    [Fact] public void SelectToLineStart_FromMidLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);   // col 3 of "World"
        cur.SelectToLineStart();
        cur.AnchorOffset.Should().Be(9);
        cur.ActiveOffset.Should().Be(6);
        cur.SelectedText.Should().Be("Wor");
    }

    [Fact] public void SelectToLineEnd_FromMidLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 7);   // col 1 of "World"
        cur.SelectToLineEnd();
        cur.SelectedText.Should().Be("orld");
    }

    [Fact] public void SelectToDocumentStart_SelectsFromCaret()
    {
        var (_, cur) = C.Make("Hello World", 6);
        cur.SelectToDocumentStart();
        cur.SelectionStart.Should().Be(0);
        cur.SelectionEnd.Should().Be(6);
        cur.SelectedText.Should().Be("Hello ");
    }

    [Fact] public void SelectToDocumentEnd_SelectsToEnd()
    {
        var (doc, cur) = C.Make("Hello World", 6);
        cur.SelectToDocumentEnd();
        cur.SelectedText.Should().Be("World");
        cur.SelectionEnd.Should().Be(doc.Length);
    }

    [Fact] public void SelectWordLeft_ExtendsLeft()
    {
        var (_, cur) = C.Make("hello world", 11);
        cur.SelectWordLeft();
        cur.SelectedText.Should().Be("world");
    }

    [Fact] public void SelectWordRight_ExtendsRight()
    {
        var (_, cur) = C.Make("hello world");
        cur.SelectWordRight();
        cur.SelectedText.Should().Be("hello ");
    }

    [Fact] public void ContinuousSelectRight_GrowsSelection()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SelectRight();
        cur.SelectRight();
        cur.SelectRight();
        cur.SelectedText.Should().Be("Hel");
    }

    [Fact] public void SelectRight_ThenSelectLeft_ShrinksSelection()
    {
        var (_, cur) = C.Make("Hello World");
        cur.SelectRight(5);
        cur.SelectLeft(2);
        cur.SelectedText.Should().Be("Hel");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Vertical selection
// ═══════════════════════════════════════════════════════════════════════════

public class CursorVerticalSelectTests
{
    [Fact] public void SelectUp_ExtendsToPreviousLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);   // col 3 of "World"
        cur.SelectUp();
        cur.AnchorOffset.Should().Be(9);
        cur.ActiveOffset.Should().Be(3);   // line 0, col 3
        cur.HasSelection.Should().BeTrue();
    }

    [Fact] public void SelectDown_ExtendsToNextLine()
    {
        var (_, cur) = C.Make("Hello\nWorld", 3);   // col 3 of "Hello"
        cur.SelectDown();
        cur.AnchorOffset.Should().Be(3);
        cur.ActiveOffset.Should().Be(9);   // line 1, col 3
    }

    [Fact] public void SelectUp_AtTopLine_ActiveGoesToLineStart()
    {
        var (_, cur) = C.Make("Hello\nWorld", 3);
        cur.SelectUp();
        cur.ActiveOffset.Should().Be(3, "already on line 0, SelectUp can't go further");
    }

    [Fact] public void SelectDown_AtBottomLine_Clamps()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);
        cur.SelectDown();
        cur.ActiveOffset.Should().Be(9, "already at last line, clamps to same position (col 3 on line 1 = offset 9)");
    }

    [Fact] public void SelectUp_ThenDown_RestoredToOriginalActive()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);
        cur.SelectUp();
        cur.SelectDown();
        cur.ActiveOffset.Should().Be(9);
    }

    [Fact] public void SelectUp_PreservesAnchor()
    {
        var (_, cur) = C.Make("Hello\nWorld", 9);
        cur.SelectUp();
        cur.SelectUp();
        cur.AnchorOffset.Should().Be(9, "anchor never changes during selection extension");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. SelectAll / SelectLine / SelectWordAtCaret
// ═══════════════════════════════════════════════════════════════════════════

public class CursorBulkSelectTests
{
    [Fact] public void SelectAll_SelectsEntireDocument()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SelectAll();
        cur.SelectionStart.Should().Be(0);
        cur.SelectionEnd.Should().Be(doc.Length);
        cur.SelectedText.Should().Be("Hello World");
    }

    [Fact] public void SelectAll_EmptyDocument_ZeroSelection()
    {
        var (_, cur) = C.Make("");
        cur.SelectAll();
        cur.SelectionStart.Should().Be(0);
        cur.SelectionEnd.Should().Be(0);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void SelectLine_IncludesTrailingNewline()
    {
        var (_, cur) = C.Make("Hello\nWorld\nFoo", 3);
        cur.SelectLine();
        cur.SelectedText.Should().Be("Hello\n");
    }

    [Fact] public void SelectLine_LastLine_SelectsToDocEnd()
    {
        var (_, cur) = C.Make("Hello\nWorld", 8);
        cur.SelectLine();
        cur.SelectedText.Should().Be("World");
        cur.SelectionEnd.Should().Be(11);
    }

    [Fact] public void SelectLine_ByIndex_SelectsCorrectLine()
    {
        var (_, cur) = C.Make("Hello\nWorld\nFoo");
        cur.SelectLine(1);
        cur.SelectedText.Should().Be("World\n");
    }

    [Fact] public void SelectLine_Index0_SelectsFirstLine()
    {
        var (_, cur) = C.Make("Hello\nWorld");
        cur.SelectLine(0);
        cur.SelectedText.Should().Be("Hello\n");
    }

    [Fact] public void SelectLine_ThenDelete_RemovesWholeLine()
    {
        var (doc, cur) = C.Make("Hello\nWorld\nFoo");
        cur.SelectLine(1);
        cur.DeleteSelection();
        doc.GetText().Should().Be("Hello\nFoo");
    }

    [Fact] public void SelectWordAtCaret_OnWord_SelectsWord()
    {
        var (_, cur) = C.Make("hello world", 2);   // inside "hello"
        cur.SelectWordAtCaret();
        cur.SelectedText.Should().Be("hello");
    }

    [Fact] public void SelectWordAtCaret_AtWordStart_SelectsWord()
    {
        var (_, cur) = C.Make("hello world", 0);
        cur.SelectWordAtCaret();
        cur.SelectedText.Should().Be("hello");
    }

    [Fact] public void SelectWordAtCaret_AtWordEnd_SelectsWord()
    {
        var (_, cur) = C.Make("hello world");
        cur.MoveTo(5);   // just after "hello"... actually at space
        // pos=5 is space character, so selects the space
        cur.SelectWordAtCaret();
        cur.SelectedText.Should().Be(" ");
    }

    [Fact] public void SelectWordAtCaret_OnSpace_SelectsSpaceGroup()
    {
        var (_, cur) = C.Make("hello   world", 7);   // middle space
        cur.SelectWordAtCaret();
        cur.SelectedText.Should().Be("   ");
    }

    [Fact] public void SelectWordAtCaret_EmptyDocument_NoOp()
    {
        var (_, cur) = C.Make("");
        var act = () => cur.SelectWordAtCaret();
        act.Should().NotThrow();
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void SelectWordAtCaret_Underscore_IncludedInWord()
    {
        var (_, cur) = C.Make("my_var = 5", 3);
        cur.SelectWordAtCaret();
        cur.SelectedText.Should().Be("my_var");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. InsertText
// ═══════════════════════════════════════════════════════════════════════════

public class CursorInsertTextTests
{
    [Fact] public void InsertText_InsertsAtCaret()
    {
        var (doc, cur) = C.Make("Hello");
        cur.MoveTo(5);
        cur.InsertText(" World");
        doc.GetText().Should().Be("Hello World");
    }

    [Fact] public void InsertText_AdvancesCaretPastInserted()
    {
        var (_, cur) = C.Make("Hello");
        cur.MoveTo(5);
        cur.InsertText(" World");
        cur.CaretOffset.Should().Be(11);
    }

    [Fact] public void InsertText_CollapsesSelection()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SetSelection(6, 11);
        cur.InsertText("Claude");
        doc.GetText().Should().Be("Hello Claude");
        cur.CaretOffset.Should().Be(12);
        cur.HasSelection.Should().BeFalse();
    }

    [Fact] public void InsertText_AtStart_Prepends()
    {
        var (doc, cur) = C.Make("World");
        cur.InsertText("Hello ");
        doc.GetText().Should().Be("Hello World");
        cur.CaretOffset.Should().Be(6);
    }

    [Fact] public void InsertText_WithNewline_IncreasesLineCount()
    {
        var (doc, cur) = C.Make("HelloWorld", 5);
        cur.InsertText("\n");
        doc.LineCount.Should().Be(2);
        cur.CaretLine.Should().Be(1);
        cur.CaretColumn.Should().Be(0);
    }

    [Fact] public void InsertText_WithCrLf_NormalisedToLf()
    {
        var (doc, cur) = C.Make("Hello", 5);
        cur.InsertText("\r\nWorld");
        doc.GetText().Should().NotContain("\r");
        cur.CaretOffset.Should().Be(11, "normalised to 6 chars (\\n not \\r\\n)");
    }

    [Fact] public void InsertText_EmptyString_NoChange()
    {
        var (doc, cur) = C.Make("Hello", 3);
        cur.InsertText("");
        doc.GetText().Should().Be("Hello");
        cur.CaretOffset.Should().Be(3);
    }

    [Fact] public void InsertText_IsUndoable()
    {
        var (doc, cur) = C.Make("Hello");
        cur.MoveTo(5);
        cur.InsertText(" World");
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }

    [Fact] public void InsertText_SelectAll_ThenInsert_ReplacesAll()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SelectAll();
        cur.InsertText("Replaced");
        doc.GetText().Should().Be("Replaced");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. DeleteSelection / DeleteLeft / DeleteRight
// ═══════════════════════════════════════════════════════════════════════════

public class CursorDeleteTests
{
    [Fact] public void DeleteSelection_RemovesSelectedText()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SetSelection(5, 11);
        cur.DeleteSelection();
        doc.GetText().Should().Be("Hello");
        cur.CaretOffset.Should().Be(5);
    }

    [Fact] public void DeleteSelection_NoSelection_NoOp()
    {
        var (doc, cur) = C.Make("Hello");
        cur.MoveTo(3);
        cur.DeleteSelection();
        doc.GetText().Should().Be("Hello");
        cur.CaretOffset.Should().Be(3);
    }

    [Fact] public void DeleteSelection_IsUndoable()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SetSelection(5, 11);
        cur.DeleteSelection();
        doc.Undo();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact] public void DeleteLeft_RemovesCharToLeft()
    {
        var (doc, cur) = C.Make("Hello", 3);
        cur.DeleteLeft();
        doc.GetText().Should().Be("Helo");
        cur.CaretOffset.Should().Be(2);
    }

    [Fact] public void DeleteLeft_AtOffset0_NoOp()
    {
        var (doc, cur) = C.Make("Hello", 0);
        cur.DeleteLeft();
        doc.GetText().Should().Be("Hello");
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void DeleteLeft_WithSelection_DeletesSelection()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SetSelection(0, 6);
        cur.DeleteLeft();
        doc.GetText().Should().Be("World");
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void DeleteLeft_Count3_DeletesThreeChars()
    {
        var (doc, cur) = C.Make("Hello", 5);
        cur.DeleteLeft(3);
        doc.GetText().Should().Be("He");
        cur.CaretOffset.Should().Be(2);
    }

    [Fact] public void DeleteRight_RemovesCharToRight()
    {
        var (doc, cur) = C.Make("Hello", 2);
        cur.DeleteRight();
        doc.GetText().Should().Be("Helo");
        cur.CaretOffset.Should().Be(2);   // caret doesn't move
    }

    [Fact] public void DeleteRight_AtDocEnd_NoOp()
    {
        var (doc, cur) = C.Make("Hello", 5);
        cur.DeleteRight();
        doc.GetText().Should().Be("Hello");
    }

    [Fact] public void DeleteRight_WithSelection_DeletesSelection()
    {
        var (doc, cur) = C.Make("Hello World");
        cur.SetSelection(0, 6);
        cur.DeleteRight();
        doc.GetText().Should().Be("World");
    }

    [Fact] public void DeleteRight_Count3_DeletesThreeChars()
    {
        var (doc, cur) = C.Make("Hello", 1);
        cur.DeleteRight(3);
        doc.GetText().Should().Be("Ho");
    }

    [Fact] public void DeleteLeft_IsUndoable()
    {
        var (doc, cur) = C.Make("Hello", 5);
        cur.DeleteLeft();
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }

    [Fact] public void DeleteRight_IsUndoable()
    {
        var (doc, cur) = C.Make("Hello", 0);
        cur.DeleteRight();
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 11. DeleteWordLeft / DeleteWordRight
// ═══════════════════════════════════════════════════════════════════════════

public class CursorDeleteWordTests
{
    [Fact] public void DeleteWordLeft_RemovesWordToLeft()
    {
        var (doc, cur) = C.Make("hello world", 11);
        cur.DeleteWordLeft();
        doc.GetText().Should().Be("hello ");
        cur.CaretOffset.Should().Be(6);
    }

    [Fact] public void DeleteWordLeft_AtStart_NoOp()
    {
        var (doc, cur) = C.Make("hello", 0);
        cur.DeleteWordLeft();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void DeleteWordLeft_WithSelection_DeletesSelection()
    {
        var (doc, cur) = C.Make("hello world");
        cur.SetSelection(0, 5);
        cur.DeleteWordLeft();
        doc.GetText().Should().Be(" world");
    }

    [Fact] public void DeleteWordRight_RemovesWordToRight()
    {
        var (doc, cur) = C.Make("hello world", 6);
        cur.DeleteWordRight();
        doc.GetText().Should().Be("hello ");
        cur.CaretOffset.Should().Be(6);
    }

    [Fact] public void DeleteWordRight_AtEnd_NoOp()
    {
        var (doc, cur) = C.Make("hello", 5);
        cur.DeleteWordRight();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void DeleteWordLeft_IsUndoable()
    {
        var (doc, cur) = C.Make("hello world", 11);
        cur.DeleteWordLeft();
        doc.Undo();
        doc.GetText().Should().Be("hello world");
    }

    [Fact] public void DeleteWordRight_IsUndoable()
    {
        var (doc, cur) = C.Make("hello world", 0);
        cur.DeleteWordRight();
        doc.Undo();
        doc.GetText().Should().Be("hello world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 12. CaretLine / CaretColumn / position round-trips
// ═══════════════════════════════════════════════════════════════════════════

public class CursorPositionTests
{
    [Fact] public void CaretLine_Line0_WhenAtStart()
    {
        var (_, cur) = C.Make("Hello\nWorld");
        cur.CaretLine.Should().Be(0);
    }

    [Fact] public void CaretLine_Line1_AfterNewline()
    {
        var (_, cur) = C.Make("Hello\nWorld", 6);
        cur.CaretLine.Should().Be(1);
    }

    [Fact] public void CaretColumn_0_AtLineStart()
    {
        var (_, cur) = C.Make("Hello\nWorld", 6);
        cur.CaretColumn.Should().Be(0);
    }

    [Fact] public void CaretColumn_5_AtLineEnd()
    {
        var (_, cur) = C.Make("Hello\nWorld", 11);
        cur.CaretColumn.Should().Be(5);
    }

    [Fact] public void CaretLineColumn_ConsistentWithOffsetToPosition()
    {
        var (doc, cur) = C.Make("Line0\nLine1\nLine2\nLine3");
        var rng = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            int offset = rng.Next(0, doc.Length + 1);
            cur.MoveTo(offset);
            var (line, col) = doc.OffsetToPosition(offset);
            cur.CaretLine.Should().Be(line, $"offset={offset}");
            cur.CaretColumn.Should().Be(col, $"offset={offset}");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 13. Edge cases — empty document, single char, single newline, etc.
// ═══════════════════════════════════════════════════════════════════════════

public class CursorEdgeCaseTests
{
    [Fact] public void AllMoves_EmptyDocument_NeverThrow()
    {
        var (_, cur) = C.Make("");
        var act = () =>
        {
            cur.MoveLeft(); cur.MoveRight(); cur.MoveUp(); cur.MoveDown();
            cur.MoveToLineStart(); cur.MoveToLineEnd();
            cur.MoveToDocumentStart(); cur.MoveToDocumentEnd();
            cur.MoveWordLeft(); cur.MoveWordRight();
        };
        act.Should().NotThrow();
    }

    [Fact] public void AllSelects_EmptyDocument_NeverThrow()
    {
        var (_, cur) = C.Make("");
        var act = () =>
        {
            cur.SelectLeft(); cur.SelectRight(); cur.SelectUp(); cur.SelectDown();
            cur.SelectToLineStart(); cur.SelectToLineEnd();
            cur.SelectToDocumentStart(); cur.SelectToDocumentEnd();
            cur.SelectWordLeft(); cur.SelectWordRight();
            cur.SelectAll(); cur.SelectLine();
        };
        act.Should().NotThrow();
    }

    [Fact] public void InsertText_EmptyDoc_InsertsAtZero()
    {
        var (doc, cur) = C.Make("");
        cur.InsertText("Hello");
        doc.GetText().Should().Be("Hello");
        cur.CaretOffset.Should().Be(5);
    }

    [Fact] public void DeleteLeft_SingleChar_EmptiesDoc()
    {
        var (doc, cur) = C.Make("X", 1);
        cur.DeleteLeft();
        doc.Length.Should().Be(0);
        cur.CaretOffset.Should().Be(0);
    }

    [Fact] public void DeleteRight_SingleChar_EmptiesDoc()
    {
        var (doc, cur) = C.Make("X", 0);
        cur.DeleteRight();
        doc.Length.Should().Be(0);
    }

    [Fact] public void SelectLine_SingleLineNoNewline_SelectsAll()
    {
        var (_, cur) = C.Make("Hello");
        cur.SelectLine();
        cur.SelectedText.Should().Be("Hello");
    }

    [Fact] public void MoveUp_SingleLine_StaysAtCurrentLine()
    {
        var (_, cur) = C.Make("Hello", 3);
        cur.MoveUp();
        cur.CaretLine.Should().Be(0);
        cur.CaretColumn.Should().Be(3);
    }

    [Fact] public void MoveDown_SingleLine_StaysAtCurrentLine()
    {
        var (_, cur) = C.Make("Hello", 3);
        cur.MoveDown();
        cur.CaretLine.Should().Be(0);
    }

    [Fact] public void CaretAlwaysWithinDocBounds_AfterAllOps()
    {
        var (doc, cur) = C.Make("Hello\nWorld\nFoo");
        cur.MoveDown(); cur.MoveDown();
        cur.CaretOffset.Should().BeInRange(0, doc.Length);
        cur.MoveRight(100);
        cur.CaretOffset.Should().BeInRange(0, doc.Length);
        cur.MoveLeft(100);
        cur.CaretOffset.Should().BeInRange(0, doc.Length);
    }

    [Fact] public void Newline_Only_Document_Handled()
    {
        var (_, cur) = C.Make("\n");
        cur.MoveDown();
        cur.CaretLine.Should().Be(1);
        cur.MoveUp();
        cur.CaretLine.Should().Be(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 14. Integration — cursor + undo interplay
// ═══════════════════════════════════════════════════════════════════════════

public class CursorUndoIntegrationTests
{
    [Fact] public void InsertText_MultipleOps_EachUndoable()
    {
        var (doc, cur) = C.Make("Hello");
        cur.MoveTo(5);
        cur.InsertText(" World");
        cur.InsertText("!");
        doc.GetText().Should().Be("Hello World!");
        doc.Undo();
        doc.GetText().Should().Be("Hello World");
        doc.Undo();
        doc.GetText().Should().Be("Hello");
    }

    [Fact] public void TypeSimulation_CharByChar_DocumentCorrect()
    {
        var (doc, cur) = C.Make("");
        foreach (char c in "Hello, World!")
            cur.InsertText(c.ToString());
        doc.GetText().Should().Be("Hello, World!");
        cur.CaretOffset.Should().Be(13);
    }

    [Fact] public void SelectAll_InsertText_Undo_RestoresOriginal()
    {
        var (doc, cur) = C.Make("Original content");
        cur.SelectAll();
        cur.InsertText("New content");
        doc.Undo();
        doc.GetText().Should().Be("Original content");
    }

    [Fact] public void DeleteLine_ViaSelectAndDelete_Undoable()
    {
        var (doc, cur) = C.Make("Line1\nLine2\nLine3");
        cur.SelectLine(1);
        cur.DeleteSelection();
        doc.GetText().Should().Be("Line1\nLine3");
        doc.Undo();
        doc.GetText().Should().Be("Line1\nLine2\nLine3");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 15. Fuzz — cursor position invariants
// ═══════════════════════════════════════════════════════════════════════════

public class CursorPositionFuzzTests
{
    private readonly ITestOutputHelper _out;
    public CursorPositionFuzzTests(ITestOutputHelper o) => _out = o;

    private static readonly string[] Ops =
    [
        "left", "right", "up", "down",
        "wordleft", "wordright",
        "linestart", "lineend",
        "docstart", "docend",
        "selright", "selleft", "selup", "seldown",
        "sellinestart", "sellineend",
        "seldocstart", "seldocend",
        "selwordleft", "selwordright",
        "selall", "selline", "selword",
        "collapsestart", "collapseend",
        "moveto"
    ];

    private static void ApplyRandomOp(TextCursor cur, Random rng, TextDocument doc)
    {
        string op = Ops[rng.Next(Ops.Length)];
        switch (op)
        {
            case "left":         cur.MoveLeft(rng.Next(1, 5)); break;
            case "right":        cur.MoveRight(rng.Next(1, 5)); break;
            case "up":           cur.MoveUp(rng.Next(1, 3)); break;
            case "down":         cur.MoveDown(rng.Next(1, 3)); break;
            case "wordleft":     cur.MoveWordLeft(); break;
            case "wordright":    cur.MoveWordRight(); break;
            case "linestart":    cur.MoveToLineStart(); break;
            case "lineend":      cur.MoveToLineEnd(); break;
            case "docstart":     cur.MoveToDocumentStart(); break;
            case "docend":       cur.MoveToDocumentEnd(); break;
            case "selright":     cur.SelectRight(rng.Next(1, 8)); break;
            case "selleft":      cur.SelectLeft(rng.Next(1, 8)); break;
            case "selup":        cur.SelectUp(); break;
            case "seldown":      cur.SelectDown(); break;
            case "sellinestart": cur.SelectToLineStart(); break;
            case "sellineend":   cur.SelectToLineEnd(); break;
            case "seldocstart":  cur.SelectToDocumentStart(); break;
            case "seldocend":    cur.SelectToDocumentEnd(); break;
            case "selwordleft":  cur.SelectWordLeft(); break;
            case "selwordright": cur.SelectWordRight(); break;
            case "selall":       cur.SelectAll(); break;
            case "selline":      cur.SelectLine(); break;
            case "selword":      cur.SelectWordAtCaret(); break;
            case "collapsestart":cur.CollapseToStart(); break;
            case "collapseend":  cur.CollapseToEnd(); break;
            case "moveto":
                cur.MoveTo(doc.Length == 0 ? 0 : rng.Next(0, doc.Length + 1));
                break;
        }
    }

    private static void AssertCursorInvariant(TextCursor cur, TextDocument doc, string context)
    {
        cur.CaretOffset.Should().BeInRange(0, doc.Length, $"{context}: caret in bounds");
        cur.AnchorOffset.Should().BeInRange(0, doc.Length, $"{context}: anchor in bounds");
        cur.ActiveOffset.Should().BeInRange(0, doc.Length, $"{context}: active in bounds");
        cur.SelectionStart.Should().Be(Math.Min(cur.AnchorOffset, cur.ActiveOffset), context);
        cur.SelectionEnd.Should().Be(Math.Max(cur.AnchorOffset, cur.ActiveOffset), context);
        cur.SelectionStart.Should().BeLessThanOrEqualTo(cur.SelectionEnd, context);

        if (cur.HasSelection)
        {
            cur.SelectedText.Should().Be(
                doc.GetText(cur.SelectionStart, cur.SelectionEnd - cur.SelectionStart),
                $"{context}: SelectedText matches doc slice");
        }
        else
        {
            cur.SelectedText.Should().Be(string.Empty, context);
        }
    }

    [Theory]
    [InlineData(1001, 500)]
    [InlineData(1002, 500)]
    [InlineData(1003, 1000)]
    public void RandomMoves_CursorAlwaysInBounds(int seed, int ops)
    {
        var rng = new Random(seed);
        var (doc, cur) = C.Make(
            string.Join("\n", Enumerable.Range(0, 20).Select(i => $"Line{i} with some words here")));

        for (int i = 0; i < ops; i++)
        {
            ApplyRandomOp(cur, rng, doc);
            AssertCursorInvariant(cur, doc, $"op {i}");
        }
        _out.WriteLine($"Seed={seed} ops={ops} final offset={cur.CaretOffset}");
    }

    [Theory]
    [InlineData(2001, 300)]
    [InlineData(2002, 300)]
    public void RandomMoves_OnEmptyThenBuiltUpDoc_NeverThrows(int seed, int ops)
    {
        var rng = new Random(seed);
        var (doc, cur) = C.Make("");

        for (int i = 0; i < ops; i++)
        {
            // Occasionally insert text
            if (rng.Next(5) == 0)
            {
                cur.InsertText(new string((char)('a' + rng.Next(26)), rng.Next(1, 10)));
                if (rng.Next(3) == 0) cur.InsertText("\n");
            }
            ApplyRandomOp(cur, rng, doc);
            AssertCursorInvariant(cur, doc, $"op {i}");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 16. Fuzz — InsertText / Delete through cursor vs StringBuilder oracle
// ═══════════════════════════════════════════════════════════════════════════

public class CursorEditFuzzTests
{
    private readonly ITestOutputHelper _out;
    public CursorEditFuzzTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(3001, 500)]
    [InlineData(3002, 500)]
    [InlineData(3003, 1000)]
    public void RandomEdits_MatchOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var oracle = new System.Text.StringBuilder();
        var (doc, cur) = C.Make("");

        for (int i = 0; i < ops; i++)
        {
            // Keep cursor offset in sync with oracle position
            int caretPos = cur.CaretOffset;
            caretPos.Should().BeLessThanOrEqualTo(oracle.Length, $"op {i} pre-check");

            int opType = rng.Next(3);
            if (opType == 0 || oracle.Length == 0)
            {
                // Insert
                string text = new string((char)('a' + rng.Next(26)), rng.Next(1, 8));
                if (rng.Next(5) == 0) text += "\n";
                cur.InsertText(text);
                oracle.Insert(caretPos, text);
            }
            else if (opType == 1 && caretPos > 0)
            {
                // DeleteLeft
                int count = Math.Min(rng.Next(1, 4), caretPos);
                cur.DeleteLeft(count);
                oracle.Remove(caretPos - count, count);
            }
            else if (opType == 2 && caretPos < oracle.Length)
            {
                // DeleteRight
                int count = Math.Min(rng.Next(1, 4), oracle.Length - caretPos);
                cur.DeleteRight(count);
                oracle.Remove(caretPos, count);
            }

            doc.GetText().Should().Be(oracle.ToString(), $"seed={seed} op={i}");
            cur.CaretOffset.Should().BeInRange(0, doc.Length, $"op {i}");
        }
        _out.WriteLine($"Seed={seed} ops={ops} docLen={doc.Length}");
    }

    [Theory]
    [InlineData(4001, 200)]
    [InlineData(4002, 200)]
    public void RandomTyping_CharByChar_MatchesOracle(int seed, int ops)
    {
        var rng    = new Random(seed);
        var oracle = new System.Text.StringBuilder();
        var (doc, cur) = C.Make("");

        for (int i = 0; i < ops; i++)
        {
            char c = rng.Next(10) == 0 ? '\n' : (char)('a' + rng.Next(26));
            cur.InsertText(c.ToString());
            oracle.Append(c);
            doc.GetText().Should().Be(oracle.ToString(), $"op {i}");
            cur.CaretOffset.Should().Be(oracle.Length, $"op {i}");
        }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 17. Fuzz — word movement oracle
// ═══════════════════════════════════════════════════════════════════════════

public class CursorWordMoveFuzzTests
{
    private readonly ITestOutputHelper _out;
    public CursorWordMoveFuzzTests(ITestOutputHelper o) => _out = o;

    /// <summary>String-based oracle for WordRight: matches the cursor's algorithm.</summary>
    private static int OracleWordRight(string text, int offset)
    {
        int len = text.Length;
        if (offset >= len) return len;
        if (TextCursor.IsWordChar(text[offset]))
        {
            while (offset < len && TextCursor.IsWordChar(text[offset]))  offset++;
            while (offset < len && !TextCursor.IsWordChar(text[offset])) offset++;
        }
        else
        {
            while (offset < len && !TextCursor.IsWordChar(text[offset])) offset++;
        }
        return offset;
    }

    /// <summary>String-based oracle for WordLeft: matches the cursor's algorithm.</summary>
    private static int OracleWordLeft(string text, int offset)
    {
        if (offset <= 0) return 0;
        offset--;
        while (offset > 0 && !TextCursor.IsWordChar(text[offset])) offset--;
        while (offset > 0 && TextCursor.IsWordChar(text[offset - 1])) offset--;
        return offset;
    }

    [Theory]
    [InlineData(5001, 200)]
    [InlineData(5002, 500)]
    [InlineData(5003, 300)]
    public void WordRight_MatchesStringOracle(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = string.Join(" ", Enumerable.Range(0, 30)
            .Select(_ => new string((char)('a' + rng.Next(26)), rng.Next(1, 10))));

        var (_, cur) = C.Make(text);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, text.Length + 1);
            int expected = OracleWordRight(text, offset);
            int actual   = cur.WordRight(offset);
            actual.Should().Be(expected, $"WordRight({offset}) seed={seed} op={i}");
        }
    }

    [Theory]
    [InlineData(6001, 200)]
    [InlineData(6002, 500)]
    [InlineData(6003, 300)]
    public void WordLeft_MatchesStringOracle(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = string.Join(" ", Enumerable.Range(0, 30)
            .Select(_ => new string((char)('a' + rng.Next(26)), rng.Next(1, 10))));

        var (_, cur) = C.Make(text);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, text.Length + 1);
            int expected = OracleWordLeft(text, offset);
            int actual   = cur.WordLeft(offset);
            actual.Should().Be(expected, $"WordLeft({offset}) seed={seed} op={i}");
        }
    }

    [Theory]
    [InlineData(7001, 100)]
    [InlineData(7002, 100)]
    public void RepeatWordRight_ReachesDocEnd(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = string.Join(" ", Enumerable.Range(0, 20)
            .Select(_ => new string((char)('a' + rng.Next(26)), rng.Next(1, 8))));

        var (doc, cur) = C.Make(text);
        int limit = text.Length + 10;
        int steps = 0;
        while (cur.CaretOffset < doc.Length && steps++ < limit)
            cur.MoveWordRight();
        cur.CaretOffset.Should().Be(doc.Length, "repeated MoveWordRight eventually reaches end");
    }

    [Theory]
    [InlineData(8001, 100)]
    [InlineData(8002, 100)]
    public void RepeatWordLeft_ReachesDocStart(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = string.Join(" ", Enumerable.Range(0, 20)
            .Select(_ => new string((char)('a' + rng.Next(26)), rng.Next(1, 8))));

        var (doc, cur) = C.Make(text, text.Length);
        int limit = text.Length + 10;
        int steps = 0;
        while (cur.CaretOffset > 0 && steps++ < limit)
            cur.MoveWordLeft();
        cur.CaretOffset.Should().Be(0, "repeated MoveWordLeft eventually reaches start");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 18. Fuzz — preferred column invariants
// ═══════════════════════════════════════════════════════════════════════════

public class CursorPreferredColumnFuzzTests
{
    [Theory]
    [InlineData(9001, 300)]
    [InlineData(9002, 300)]
    public void PreferredColumn_NeverCausesOutOfBounds(int seed, int iters)
    {
        var rng = new Random(seed);
        var lines = Enumerable.Range(0, 15)
            .Select(i => new string('a', rng.Next(0, 20)))
            .ToArray();
        var (doc, cur) = C.Make(string.Join("\n", lines));

        for (int i = 0; i < iters; i++)
        {
            int op = rng.Next(4);
            switch (op)
            {
                case 0: cur.MoveUp(rng.Next(1, 4)); break;
                case 1: cur.MoveDown(rng.Next(1, 4)); break;
                case 2: cur.MoveLeft(rng.Next(1, 4)); break;
                case 3: cur.MoveRight(rng.Next(1, 4)); break;
            }
            cur.CaretOffset.Should().BeInRange(0, doc.Length, $"op {i}");
            var (line, col) = doc.OffsetToPosition(cur.CaretOffset);
            col.Should().BeLessThanOrEqualTo(doc.GetLine(line).Length, $"op {i}: column within line");
        }
    }

    [Theory]
    [InlineData(9101, 5)]
    [InlineData(9102, 10)]
    public void MoveDownThenUp_SameColumn_OnUniformLines(int seed, int count)
    {
        // All lines same length — up/down should restore exact column
        var content = string.Join("\n", Enumerable.Repeat("Hello World", count + 2));
        var (_, cur) = C.Make(content, 5);   // col 5 of first line
        for (int i = 0; i < count; i++) cur.MoveDown();
        for (int i = 0; i < count; i++) cur.MoveUp();
        cur.CaretColumn.Should().Be(5, "preferred column restored after up/down on uniform lines");
    }
}
