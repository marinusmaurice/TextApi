using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.WordWrap;
using Xunit;

namespace TextEditor.Tests;

public class WordWrapTests
{
    // ── Helper ────────────────────────────────────────────────────────────

    private static TextDocument Doc(string content)
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }

    // ── Basic single-line wrapping ────────────────────────────────────────

    [Fact]
    public void ShortLine_OneRow()
    {
        var doc   = Doc("hello");
        var model = doc.GetWordWrapModel(80);
        model.WrappedRowCount(0).Should().Be(1);
    }

    [Fact]
    public void ExactFit_OneRow()
    {
        var doc   = Doc("0123456789"); // exactly 10 chars at width=10
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(1);
    }

    [Fact]
    public void OneCharOver_TwoRows()
    {
        var doc   = Doc("01234567890"); // 11 chars at width=10
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(2);
    }

    [Fact]
    public void LongLine_CorrectRowCount()
    {
        var doc   = Doc(new string('a', 100)); // 100 chars at width=10 → 10 rows
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(10);
    }

    [Fact]
    public void EmptyLine_OneRow()
    {
        var doc   = Doc("");
        var model = doc.GetWordWrapModel(80);
        model.WrappedRowCount(0).Should().Be(1);
    }

    [Fact]
    public void EmptyDocument_DisplayRowCountOne()
    {
        var doc   = Doc("");
        var model = doc.GetWordWrapModel(80);
        model.DisplayRowCount.Should().Be(1);
    }

    // ── Multi-line ────────────────────────────────────────────────────────

    [Fact]
    public void MultiLine_NoWrap_RowCountEqualsLineCount()
    {
        var doc   = Doc("a\nb\nc");
        var model = doc.GetWordWrapModel(80);
        model.DisplayRowCount.Should().Be(3);
    }

    [Fact]
    public void MultiLine_SomeWrapped_CorrectTotal()
    {
        // Line 0: "a"          → 1 row at width=10
        // Line 1: "0123456789X" → 2 rows at width=10
        var doc   = Doc("a\n0123456789X");
        var model = doc.GetWordWrapModel(10);
        model.DisplayRowCount.Should().Be(3);
    }

    [Fact]
    public void AllLinesExactFit_NoWrap()
    {
        // Three lines of exactly 10 chars each — no wrapping
        var doc   = Doc("0123456789\n0123456789\n0123456789");
        var model = doc.GetWordWrapModel(10);
        model.DisplayRowCount.Should().Be(3);
        model.IsWrapped(0).Should().BeFalse();
        model.IsWrapped(1).Should().BeFalse();
        model.IsWrapped(2).Should().BeFalse();
    }

    // ── ToDisplayRow ──────────────────────────────────────────────────────

    [Fact]
    public void ToDisplayRow_FirstLine_IsZero()
    {
        var doc   = Doc("hello\nworld");
        var model = doc.GetWordWrapModel(80);
        model.ToDisplayRow(0).Should().Be(0);
    }

    [Fact]
    public void ToDisplayRow_SecondLine()
    {
        // "short" is 5 chars → 1 row at width=10; line 1 starts at display row 1
        var doc   = Doc("short\n0123456789X");
        var model = doc.GetWordWrapModel(10);
        model.ToDisplayRow(1).Should().Be(1);
    }

    [Fact]
    public void ToDisplayRow_WrappedSecondLine()
    {
        // First line is 30 chars at width=10 → 3 rows; line 1 starts at row 3
        var doc   = Doc(new string('a', 30) + "\nhello");
        var model = doc.GetWordWrapModel(10);
        model.ToDisplayRow(1).Should().Be(3);
    }

    // ── ToDocumentLine ────────────────────────────────────────────────────

    [Fact]
    public void ToDocumentLine_FirstRow_IsLine0()
    {
        var doc   = Doc("hello\nworld");
        var model = doc.GetWordWrapModel(80);
        model.ToDocumentLine(0).Should().Be(0);
    }

    [Fact]
    public void ToDocumentLine_MidWrappedLine()
    {
        // Line 0: 30 chars at width=10 → rows 0,1,2  (3 rows)
        // Line 1: "hello"              → row 3
        var doc   = Doc(new string('a', 30) + "\nhello");
        var model = doc.GetWordWrapModel(10);
        model.ToDocumentLine(0).Should().Be(0);
        model.ToDocumentLine(1).Should().Be(0);
        model.ToDocumentLine(2).Should().Be(0);
        model.ToDocumentLine(3).Should().Be(1);
    }

    [Fact]
    public void ToDocumentLine_RoundTrip()
    {
        var doc   = Doc(new string('a', 30) + "\nhello\n" + new string('b', 25));
        var model = doc.GetWordWrapModel(10);
        for (int i = 0; i < doc.LineCount; i++)
        {
            int dr   = model.ToDisplayRow(i);
            int back = model.ToDocumentLine(dr);
            back.Should().Be(i, $"round-trip failed for doc line {i}");
        }
    }

    // ── GetWrappedSegments ────────────────────────────────────────────────

    [Fact]
    public void GetWrappedSegments_ShortLine_OneSegment()
    {
        var doc   = Doc("hello");
        var model = doc.GetWordWrapModel(80);
        var segs  = model.GetWrappedSegments(0);
        segs.Should().HaveCount(1);
        segs[0].Should().Be((0, 5));
    }

    [Fact]
    public void GetWrappedSegments_WrappedLine_CorrectSegmentCount()
    {
        // 20 chars at width=10 → 2 segments
        var doc   = Doc(new string('a', 20));
        var model = doc.GetWordWrapModel(10);
        var segs  = model.GetWrappedSegments(0);
        segs.Should().HaveCount(2);
    }

    [Fact]
    public void GetWrappedSegments_EmptyLine_OneSegment()
    {
        var doc   = Doc("");
        var model = doc.GetWordWrapModel(80);
        var segs  = model.GetWrappedSegments(0);
        segs.Should().HaveCount(1);
        segs[0].Should().Be((0, 0));
    }

    [Fact]
    public void GetWrappedSegments_Continuous()
    {
        // End of segment N == start of segment N+1
        var doc   = Doc(new string('x', 35));
        var model = doc.GetWordWrapModel(10);
        var segs  = model.GetWrappedSegments(0);
        for (int i = 0; i < segs.Count - 1; i++)
            segs[i].End.Should().Be(segs[i + 1].Start, $"seg {i} end should equal seg {i + 1} start");
    }

    [Fact]
    public void GetWrappedSegments_CoversFullLine()
    {
        var doc   = Doc(new string('y', 35));
        var model = doc.GetWordWrapModel(10);
        var segs  = model.GetWrappedSegments(0);
        segs[0].Start.Should().Be(0);
        segs[^1].End.Should().Be(35);
    }

    // ── IsWrapped ─────────────────────────────────────────────────────────

    [Fact]
    public void IsWrapped_ShortLine_False()
    {
        var doc   = Doc("hi");
        var model = doc.GetWordWrapModel(80);
        model.IsWrapped(0).Should().BeFalse();
    }

    [Fact]
    public void IsWrapped_LongLine_True()
    {
        var doc   = Doc(new string('z', 50));
        var model = doc.GetWordWrapModel(10);
        model.IsWrapped(0).Should().BeTrue();
    }

    // ── East Asian Width ──────────────────────────────────────────────────

    [Fact]
    public void WideChars_ExactFit()
    {
        // 5 CJK chars × 2 cols each = 10 cols — exactly fits at width=10
        var doc   = Doc("中文日本한");
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(1);
    }

    [Fact]
    public void WideChars_OneOver()
    {
        // 6 CJK chars × 2 cols = 12 cols at width=10 → 2 rows
        var doc   = Doc("中文日本한국");
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(2);
    }

    [Fact]
    public void WideChar_CantFitInHalf()
    {
        // A line of 3 CJK chars (each w=2) at viewport=1:
        // char 0 (w=2): colUsed=0, 0+2>1 → rows=2, colUsed=2
        // char 1 (w=2): colUsed=2, 2+2>1 → rows=3, colUsed=2
        // char 2 (w=2): colUsed=2, 2+2>1 → rows=4, colUsed=2
        // Total: 4 rows (each char overflows because colUsed carries over the wide width)
        var doc   = Doc("中文日");
        var model = doc.GetWordWrapModel(1);
        model.WrappedRowCount(0).Should().Be(4);
    }

    [Fact]
    public void MixedWidthChars_CorrectWrapping()
    {
        // "aあb" at width=3:
        //   'a' → col 1, 'あ' (w=2) → col 1+2=3 (fits exactly), 'b' → 3+1=4 > 3 → new row
        // So row 0 = "aあ" (cols 1+2=3), row 1 = "b" → 2 rows
        var doc   = Doc("aあb");
        var model = doc.GetWordWrapModel(3);
        model.WrappedRowCount(0).Should().Be(2);

        var segs  = model.GetWrappedSegments(0);
        segs.Should().HaveCount(2);
        // First segment: "aあ" — 'a' is 1 char, 'あ' is 1 char = chars 0..2
        segs[0].Should().Be((0, 2));
        // Second segment: "b" — char 2..3
        segs[1].Should().Be((2, 3));
    }

    // ── Resize ────────────────────────────────────────────────────────────

    [Fact]
    public void Resize_NarrowViewport_MoreRows()
    {
        var doc   = Doc(new string('a', 50));
        var model = doc.GetWordWrapModel(10);
        int rows10 = model.DisplayRowCount;   // 5 rows

        model.Resize(5);
        model.DisplayRowCount.Should().BeGreaterThan(rows10);
    }

    [Fact]
    public void Resize_WideViewport_FewerRows()
    {
        var doc   = Doc("line1\n" + new string('a', 50) + "\nline3");
        var model = doc.GetWordWrapModel(10);

        model.Resize(200);
        // With width=200 nothing wraps: display rows == line count
        model.DisplayRowCount.Should().Be(doc.LineCount);
    }

    [Fact]
    public void Resize_FiresWrapChanged()
    {
        var doc   = Doc("hello");
        var model = doc.GetWordWrapModel(80);
        int fired = 0;
        model.WrapChanged += (_, _) => fired++;

        model.Resize(40);
        fired.Should().Be(1);
    }

    [Fact]
    public void Resize_ToSameWidth_NoEvent()
    {
        var doc   = Doc("hello");
        var model = doc.GetWordWrapModel(80);
        int fired = 0;
        model.WrapChanged += (_, _) => fired++;

        model.Resize(80); // same width — should not fire
        fired.Should().Be(0);
    }

    // ── Live updates ──────────────────────────────────────────────────────

    [Fact]
    public void InsertNewline_RowCountUpdates()
    {
        var doc   = Doc("hello\nworld");
        var model = doc.GetWordWrapModel(80);
        int before = model.DisplayRowCount; // 2

        // Insert a newline at offset 5 ("hello|world" → "hello\n\nworld")
        doc.Insert(5, "\n");
        model.DisplayRowCount.Should().BeGreaterThan(before);
        model.DisplayRowCount.Should().Be(3);
    }

    [Fact]
    public void DeleteLine_RowCountUpdates()
    {
        var doc   = Doc("hello\nworld\nfoo");
        var model = doc.GetWordWrapModel(80);
        int before = model.DisplayRowCount; // 3

        // Delete "hello\n" (6 chars at offset 0)
        doc.Delete(0, 6);
        model.DisplayRowCount.Should().BeLessThan(before);
        model.DisplayRowCount.Should().Be(2);
    }

    [Fact]
    public void EditLine_WrapChanges()
    {
        var doc   = Doc("short");
        var model = doc.GetWordWrapModel(10);
        model.WrappedRowCount(0).Should().Be(1); // initially 1 row

        // Extend the line to 25 chars → should now wrap at width=10
        doc.Insert(5, new string('x', 20));
        model.IsWrapped(0).Should().BeTrue();
        model.WrappedRowCount(0).Should().Be(3); // 25 chars / 10 = 3 rows (10,10,5)
    }

    // ── DisplayRowCount sum invariant ─────────────────────────────────────

    [Fact]
    public void DisplayRowCount_Sum()
    {
        var doc   = Doc("hello\n" + new string('a', 25) + "\nworld\n" + new string('b', 15));
        var model = doc.GetWordWrapModel(10);

        int sum = 0;
        for (int i = 0; i < doc.LineCount; i++)
            sum += model.WrappedRowCount(i);

        model.DisplayRowCount.Should().Be(sum);
    }
}
