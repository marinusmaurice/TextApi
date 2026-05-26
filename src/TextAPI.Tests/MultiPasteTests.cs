using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Item 18 — Multi-line paste across multi-cursors
// ═══════════════════════════════════════════════════════════════════════════

file static class MPHelper
{
    public static (TextDocument doc, MultiCursor mc) Make(string text = "")
    {
        var doc = new TextDocument();
        if (!string.IsNullOrEmpty(text)) doc.Load(text);
        var mc = new MultiCursor(doc);
        return (doc, mc);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 1. Distributed paste — N lines, N cursors
// ─────────────────────────────────────────────────────────────────────────

public class MultiPasteDistributedTests
{
    [Fact]
    public void ThreeLines_ThreeCursors_EachCursorGetsItsLine()
    {
        // Document: "   \n   \n   " (three lines with 3 spaces each)
        // Offsets: ' '=0,' '=1,' '=2,'\n'=3,' '=4,' '=5,' '=6,'\n'=7,' '=8,' '=9,' '=10
        var (doc, mc) = MPHelper.Make("   \n   \n   ");
        // Collapsed cursors at the start of each line.
        mc.SetSingle(0);
        mc.AddCursor(4);
        mc.AddCursor(8);
        mc.All.Count.Should().Be(3);

        mc.Paste(["alpha", "beta", "gamma"]);

        // Collapsed cursors insert without deleting anything.
        // Result: "alpha   \nbeta   \ngamma   "
        var text = doc.GetText(0, doc.Length);
        text.Should().Be("alpha   \nbeta   \ngamma   ");
    }

    [Fact]
    public void Distributed_CursorsInSortedOrder_MatchesLineOrder()
    {
        // Add cursors in reverse order — they must be sorted before distribution.
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        // Offsets: '.' at 0, '\n' at 1, '.' at 2, '\n' at 3, '.' at 4
        mc.SetSingle(4); // bottom cursor first
        mc.AddCursor(2);
        mc.AddCursor(0);
        // After SortAndMerge cursors are [0, 2, 4] top-to-bottom.

        mc.Paste(["X", "Y", "Z"]);

        doc.GetText(0, doc.Length).Should().Be("X.\nY.\nZ.");
    }

    [Fact]
    public void Distributed_ReplacesSelectionAtEachCursor()
    {
        // Each cursor has a selection; paste should replace it.
        var (doc, mc) = MPHelper.Make("aaa\nbbb\nccc");
        // Select each word: "aaa" at 0..3, "bbb" at 4..7, "ccc" at 8..11
        mc.SetSingle(0);
        mc.Primary.SelectTo(3);
        mc.AddCursor(4, 7);
        mc.AddCursor(8, 11);

        mc.Paste(["111", "222", "333"]);

        doc.GetText(0, doc.Length).Should().Be("111\n222\n333");
    }

    [Fact]
    public void Distributed_SingleCursor_SingleLine_Works()
    {
        var (doc, mc) = MPHelper.Make("hello");
        mc.SetSingle(5);

        mc.Paste(["!"]);

        doc.GetText(0, doc.Length).Should().Be("hello!");
    }

    [Fact]
    public void Distributed_CaretPlacedAfterInsertedText()
    {
        // Document: ".\n.\n."  (offsets: '.'=0 '\n'=1 '.'=2 '\n'=3 '.'=4)
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["aa", "bb", "cc"]);

        // After paste: "aa.\nbb.\ncc."
        // Offsets: a=0,a=1,.=2,\n=3,b=4,b=5,.=6,\n=7,c=8,c=9,.=10
        // Cursors land after their inserted text:
        //   cursor 0 inserted "aa" at 0 → caret 2
        //   cursor 1 inserted "bb" at 4 (shifted by +2) → caret 6
        //   cursor 2 inserted "cc" at 8 (shifted by +4) → caret 10
        mc.All[0].CaretOffset.Should().Be(2);
        mc.All[1].CaretOffset.Should().Be(6);
        mc.All[2].CaretOffset.Should().Be(10);
    }

    [Fact]
    public void Distributed_IsOneUndoStep()
    {
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["X", "Y", "Z"]);
        doc.GetText(0, doc.Length).Should().Be("X.\nY.\nZ.");

        doc.Undo();
        doc.GetText(0, doc.Length).Should().Be(".\n.\n.");
    }

    [Fact]
    public void Distributed_NormalizesLineEndings()
    {
        var (doc, mc) = MPHelper.Make("\n\n");
        mc.SetSingle(0);
        mc.AddCursor(1);
        mc.AddCursor(2);

        // Lines with CRLF should be normalised to LF.
        mc.Paste(["a\r\nb", "c\r\nd", "e\r\nf"]);

        var text = doc.GetText(0, doc.Length);
        text.Should().NotContain("\r");
        text.Should().Contain("a\nb");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 2. Broadcast paste — count mismatch → same text at every cursor
// ─────────────────────────────────────────────────────────────────────────

public class MultiPasteBroadcastTests
{
    [Fact]
    public void OneLine_ThreeCursors_SameTextAtAll()
    {
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["X"]);

        // "X" inserted at each position; bottom-to-top so offsets stay valid.
        doc.GetText(0, doc.Length).Should().Be("X.\nX.\nX.");
    }

    [Fact]
    public void TwoLines_ThreeCursors_JoinedTextAtAll()
    {
        var (doc, mc) = MPHelper.Make("|\n|\n|");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["line1", "line2"]);

        // "line1\nline2" inserted at each cursor.
        var text = doc.GetText(0, doc.Length);
        text.Should().Be("line1\nline2|\nline1\nline2|\nline1\nline2|");
    }

    [Fact]
    public void FourLines_ThreeCursors_JoinedTextAtAll()
    {
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["a", "b", "c", "d"]);

        var text = doc.GetText(0, doc.Length);
        // "a\nb\nc\nd" inserted at each cursor
        text.Should().Contain("a\nb\nc\nd");
    }

    [Fact]
    public void SingleCursor_MultipleLines_PastsJoined()
    {
        var (doc, mc) = MPHelper.Make("hello");
        mc.SetSingle(5);

        mc.Paste(["world", "foo", "bar"]);

        doc.GetText(0, doc.Length).Should().Be("helloworld\nfoo\nbar");
    }

    [Fact]
    public void EmptyLinesList_NoOp()
    {
        var (doc, mc) = MPHelper.Make("hello");
        mc.SetSingle(0);

        mc.Paste([]);

        doc.GetText(0, doc.Length).Should().Be("hello");
    }

    [Fact]
    public void Broadcast_IsOneUndoStep()
    {
        var (doc, mc) = MPHelper.Make(".\n.\n.");
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["X"]);
        doc.GetText(0, doc.Length).Should().Be("X.\nX.\nX.");

        doc.Undo();
        doc.GetText(0, doc.Length).Should().Be(".\n.\n.");
    }

    [Fact]
    public void Broadcast_ReplacesSelections()
    {
        var (doc, mc) = MPHelper.Make("aaa\nbbb");
        mc.SetSingle(0);
        mc.Primary.SelectTo(3);
        mc.AddCursor(4, 7);

        // 3 lines, 2 cursors → broadcast (joins with \n, pastes at each)
        mc.Paste(["X", "Y", "Z"]);

        doc.GetText(0, doc.Length).Should().Be("X\nY\nZ\nX\nY\nZ");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 3. Cursor ordering
// ─────────────────────────────────────────────────────────────────────────

public class MultiPasteCursorOrderTests
{
    [Fact]
    public void TopToBottom_LineAssignment()
    {
        // Verify that the topmost cursor always gets lines[0].
        var (doc, mc) = MPHelper.Make("_\n_\n_");
        // Offsets: '_'=0 '\n'=1 '_'=2 '\n'=3 '_'=4
        mc.SetSingle(0);
        mc.AddCursor(2);
        mc.AddCursor(4);

        mc.Paste(["FIRST", "SECOND", "THIRD"]);

        // Line 0 starts at 0, line 1 at 2, line 2 at 4.
        // After paste: "FIRST_\nSECOND_\nTHIRD_"
        var text = doc.GetText(0, doc.Length);
        var lines = text.Split('\n');
        lines[0].Should().StartWith("FIRST");
        lines[1].Should().StartWith("SECOND");
        lines[2].Should().StartWith("THIRD");
    }

    [Fact]
    public void CursorsAddedInAnyOrder_SortedBeforeDistribution()
    {
        var (doc, mc) = MPHelper.Make("_\n_\n_");
        // Add in reverse — after SortAndMerge cursors are ascending.
        mc.SetSingle(4);
        mc.AddCursor(2);
        mc.AddCursor(0);

        mc.Paste(["A", "B", "C"]);

        var text = doc.GetText(0, doc.Length);
        var lines = text.Split('\n');
        lines[0].Should().StartWith("A");
        lines[1].Should().StartWith("B");
        lines[2].Should().StartWith("C");
    }
}
