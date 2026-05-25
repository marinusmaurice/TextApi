using TextEditor.Core;
using TextEditor.Core.Language;
using FluentAssertions;
using Xunit;

namespace TextEditor.Tests;

public class IndentGuideTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static TextDocument Doc(string content)
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }

    private static IReadOnlyList<IndentGuide> Guides(string content, int tabWidth = 4)
    {
        var doc = Doc(content);
        return doc.GetIndentGuides(0, doc.LineCount - 1, tabWidth);
    }

    // ── Empty / no indent ────────────────────────────────────────────────

    [Fact]
    public void EmptyDocument_NoGuides()
    {
        // Single empty line → indent=-1 → filled to 0 → maxIndent=0 → no guides
        var guides = Guides("");
        guides.Should().BeEmpty();
    }

    [Fact]
    public void NoBlanks_NoIndent_NoGuides()
    {
        // "a\nb\nc" → indents [0,0,0] → max=0 → no guides
        var guides = Guides("a\nb\nc");
        guides.Should().BeEmpty();
    }

    [Fact]
    public void SingleLine_Indented_NoGuides()
    {
        // "    x" → single line, indents=[4], max=4
        // col=0: span lines where indent>0 → line 0 only → span [0,0] (1 line) → emitted
        // But wait: span of length 1, spanEnd(0) >= spanStart(0) → included
        // So this gives IndentGuide(0, 0, 0)
        var guides = Guides("    x");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 0, 0));
    }

    // ── Basic guides ─────────────────────────────────────────────────────

    [Fact]
    public void TwoLinesNoIndent_OneLineIndented_Guide()
    {
        // "line0\n    line1\n    line2\nline3"
        // indents: [0, 4, 4, 0]  max=4
        // col=0: inside where indent>0 → lines 1,2 → span [1,2] → IndentGuide(0, 1, 2)
        var guides = Guides("line0\n    line1\n    line2\nline3");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void TwoConsecutiveIndentedLines_OneGuide()
    {
        // "a\n    b\n    c"
        // indents: [0, 4, 4]  max=4
        // col=0: lines 1..2 → IndentGuide(0, 1, 2)
        var guides = Guides("a\n    b\n    c");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void NestedIndent_TwoGuides()
    {
        // "a\n    b\n        c\n    d\ne"
        // indents: [0, 4, 8, 4, 0]  max=8
        // col=0: indent>0 → lines 1,2,3 → span [1,3] → IndentGuide(0, 1, 3)
        // col=4: indent>4 → line 2 only → span [2,2] → IndentGuide(4, 2, 2)
        var guides = Guides("a\n    b\n        c\n    d\ne");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 1, 3));
        guides[1].Should().Be(new IndentGuide(4, 2, 2));
    }

    [Fact]
    public void ThreeLevelNesting_ThreeGuides()
    {
        // "a\n    b\n        c\n            d\n    e\nf"
        // indents: [0, 4, 8, 12, 4, 0]  max=12
        // col=0:  indent>0  → lines 1..4 → IndentGuide(0, 1, 4)
        // col=4:  indent>4  → lines 2..3 → IndentGuide(4, 2, 3)
        // col=8:  indent>8  → line 3     → IndentGuide(8, 3, 3)
        var guides = Guides("a\n    b\n        c\n            d\n    e\nf");
        guides.Should().HaveCount(3);
        guides[0].Should().Be(new IndentGuide(0,  1, 4));
        guides[1].Should().Be(new IndentGuide(4,  2, 3));
        guides[2].Should().Be(new IndentGuide(8,  3, 3));
    }

    // ── Blank line handling ───────────────────────────────────────────────

    [Fact]
    public void BlankLineBetween_GuideSpansContinues()
    {
        // "a\n    b\n\n    c\nd"
        // raw indents: [0, 4, -1, 4, 0]
        // fromAbove: [0, 4, 4, 4, 4]
        // fromBelow: [0, 4, 4, 4, 0]
        // filled:    [0, 4, min(4,4)=4, 4, 0]
        // col=0: indent>0 → lines 1,2,3 → IndentGuide(0, 1, 3)
        var guides = Guides("a\n    b\n\n    c\nd");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 3));
    }

    [Fact]
    public void BlankLineAtStart_TransparentToGuide()
    {
        // "\n    a\n    b"
        // raw indents: [-1, 4, 4]
        // fromAbove: [0, 4, 4]
        // fromBelow: [4, 4, 4]
        // filled:    [min(0,4)=0, 4, 4]
        // col=0: indent>0 → lines 1,2 → IndentGuide(0, 1, 2)
        var guides = Guides("\n    a\n    b");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void BlankLineAtEnd_TransparentToGuide()
    {
        // "    a\n    b\n"
        // raw indents: [4, 4, -1]
        // fromAbove: [4, 4, 4]
        // fromBelow: [4, 4, 0]
        // filled:    [4, 4, min(4,0)=0]
        // col=0: indent>0 → lines 0,1 → IndentGuide(0, 0, 1)
        // Note: line 2 has effective indent 0 so it does NOT continue the span
        var guides = Guides("    a\n    b\n");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 0, 1));
    }

    [Fact]
    public void AllBlankLines_NoGuides()
    {
        // "\n\n\n"
        // raw: [-1,-1,-1,-1]
        // fromAbove: [0,0,0,0]  fromBelow: [0,0,0,0]
        // filled: [0,0,0,0] → max=0 → no guides
        var guides = Guides("\n\n\n");
        guides.Should().BeEmpty();
    }

    [Fact]
    public void OnlyWhitespaceLines_NoGuides()
    {
        // "   \n  \n    " → all whitespace-only → all return -1 → filled to 0 → no guides
        var guides = Guides("   \n  \n    ");
        guides.Should().BeEmpty();
    }

    [Fact]
    public void MultipleBlankLinesBetween_SpanContinues()
    {
        // "a\n    b\n\n\n    c\nd"
        // raw: [0, 4, -1, -1, 4, 0]
        // fromAbove: [0, 4, 4, 4, 4, 4]
        // fromBelow: [0, 4, 4, 4, 4, 0]
        // filled: [0, 4, 4, 4, 4, 0]
        // col=0: indent>0 → lines 1..4 → IndentGuide(0, 1, 4)
        var guides = Guides("a\n    b\n\n\n    c\nd");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 4));
    }

    // ── Tab handling ─────────────────────────────────────────────────────

    [Fact]
    public void TabIndent_CorrectColumn_TabWidth4()
    {
        // "a\n\tb\n\tc\nd" with tabWidth=4
        // indents: [0, 4, 4, 0]
        // col=0: lines 1..2 → IndentGuide(0, 1, 2)
        var guides = Guides("a\n\tb\n\tc\nd", tabWidth: 4);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void TabWidth2_CorrectGuideColumns()
    {
        // "a\n  b\n  c\nd" with tabWidth=2
        // indents: [0, 2, 2, 0]  max=2
        // col=0: indent>0 → lines 1..2 → IndentGuide(0, 1, 2)
        var guides = Guides("a\n  b\n  c\nd", tabWidth: 2);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void TabWidth2_TwoLevels_TwoGuides()
    {
        // "a\n  b\n    c\n  d\ne" with tabWidth=2
        // indents: [0, 2, 4, 2, 0]  max=4
        // col=0: indent>0 → lines 1..3 → IndentGuide(0, 1, 3)
        // col=2: indent>2 → line 2    → IndentGuide(2, 2, 2)
        var guides = Guides("a\n  b\n    c\n  d\ne", tabWidth: 2);
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 1, 3));
        guides[1].Should().Be(new IndentGuide(2, 2, 2));
    }

    [Fact]
    public void MixedTabsAndSpaces_IndentCalculated()
    {
        // Line "\t    b" with tabWidth=4: tab→col4, then 4 spaces → col8
        // "a\n\t    b\n\t    c\nd" with tabWidth=4
        // indents: [0, 8, 8, 0]  max=8
        // col=0: lines 1..2 → IndentGuide(0, 1, 2)
        // col=4: indent>4 → lines 1..2 → IndentGuide(4, 1, 2)
        var guides = Guides("a\n\t    b\n\t    c\nd", tabWidth: 4);
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 1, 2));
        guides[1].Should().Be(new IndentGuide(4, 1, 2));
    }

    // ── Range parameter ───────────────────────────────────────────────────

    [Fact]
    public void StartLineMiddle_OnlyRequestedLinesConsidered()
    {
        // "a\nb\n    c\n    d\ne"
        // Request startLine=2, endLine=3
        // Only lines 2..3: indents [4,4] max=4
        // col=0: lines 2..3 → IndentGuide(0, 2, 3)
        var doc = Doc("a\nb\n    c\n    d\ne");
        var guides = doc.GetIndentGuides(2, 3);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 2, 3));
    }

    [Fact]
    public void EndLineClamped_WhenBeyondDocumentEnd()
    {
        // "a\n    b\n    c" → 3 lines (0..2)
        // Request endLine=999 → clamped to 2
        // indents: [0, 4, 4]  col=0: lines 1..2 → IndentGuide(0, 1, 2)
        var doc = Doc("a\n    b\n    c");
        var guides = doc.GetIndentGuides(0, 999);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    [Fact]
    public void StartLineGreaterThanEndLine_NoGuides()
    {
        var doc = Doc("a\n    b\n    c");
        var guides = doc.GetIndentGuides(5, 2);
        guides.Should().BeEmpty();
    }

    [Fact]
    public void StartLineNegative_ClampedToZero()
    {
        // startLine=-3 → clamped to 0
        // "a\n    b\n    c"  → IndentGuide(0, 1, 2)
        var doc = Doc("a\n    b\n    c");
        var guides = doc.GetIndentGuides(-3, 2);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 2));
    }

    // ── Single-line spans ─────────────────────────────────────────────────

    [Fact]
    public void SingleIndentedLine_SingleLineSpanIncluded()
    {
        // "a\n    b\nc"
        // indents: [0, 4, 0]  max=4
        // col=0: line 1 only → span [1,1] (1 line) → IndentGuide(0, 1, 1)
        var guides = Guides("a\n    b\nc");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 1, 1));
    }

    [Fact]
    public void TwoSeparateIndentedBlocks_TwoGuides()
    {
        // "a\n    b\nc\n    d\ne"
        // indents: [0, 4, 0, 4, 0]  max=4
        // col=0: span [1,1] → IndentGuide(0,1,1) then [3,3] → IndentGuide(0,3,3)
        var guides = Guides("a\n    b\nc\n    d\ne");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 1, 1));
        guides[1].Should().Be(new IndentGuide(0, 3, 3));
    }

    // ── Sort order ────────────────────────────────────────────────────────

    [Fact]
    public void GuidesSortedByColumnThenStartLine()
    {
        // "a\n    b\n        c\n    d\ne"
        // indents: [0, 4, 8, 4, 0]
        // col=0: [1,3], col=4: [2,2]
        // Sorted: col 0 first, then col 4
        var guides = Guides("a\n    b\n        c\n    d\ne");
        guides.Should().HaveCount(2);
        guides[0].Column.Should().Be(0);
        guides[1].Column.Should().Be(4);
    }

    [Fact]
    public void TwoGuidesSameColumn_SortedByStartLine()
    {
        // "a\n    b\nc\n    d\ne\n        f\n        g\nh"
        // indents: [0, 4, 0, 4, 0, 8, 8, 0]  max=8
        // col=0: spans [1,1] and [3,3] and [5,6]
        //   → IndentGuide(0,1,1), IndentGuide(0,3,3), IndentGuide(0,5,6)
        // col=4: spans [5,6] → IndentGuide(4,5,6)
        var guides = Guides("a\n    b\nc\n    d\ne\n        f\n        g\nh");
        // Verify col=0 guides are sorted by start line
        var col0 = guides.Where(g => g.Column == 0).ToList();
        col0.Should().HaveCount(3);
        col0[0].StartLine.Should().Be(1);
        col0[1].StartLine.Should().Be(3);
        col0[2].StartLine.Should().Be(5);
    }

    // ── C-style code ──────────────────────────────────────────────────────

    [Fact]
    public void CStyleBlock_GuidesCoverBodyLines()
    {
        // "void foo()\n{\n    int x = 1;\n    return x;\n}"
        // indents: [0, 0, 4, 4, 0]  max=4
        // col=0: indent>0 → lines 2,3 → IndentGuide(0, 2, 3)
        var guides = Guides("void foo()\n{\n    int x = 1;\n    return x;\n}");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 2, 3));
    }

    [Fact]
    public void CStyleNestedIfBlock_MultipleGuides()
    {
        // "void foo()\n{\n    if (x)\n    {\n        return;\n    }\n}"
        // indents: [0, 0, 4, 4, 8, 4, 0]  max=8
        // col=0: indent>0 → lines 2..5 → IndentGuide(0, 2, 5)
        // col=4: indent>4 → line 4 only → IndentGuide(4, 4, 4)
        var guides = Guides("void foo()\n{\n    if (x)\n    {\n        return;\n    }\n}");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 2, 5));
        guides[1].Should().Be(new IndentGuide(4, 4, 4));
    }

    [Fact]
    public void DeepNesting_AllGuideLevelsPresent()
    {
        // "a\n    b\n        c\n            d\n                e\na"
        // indents: [0, 4, 8, 12, 16, 0]  max=16
        // col=0:  lines 1..4 → IndentGuide(0, 1, 4)
        // col=4:  lines 2..4 → IndentGuide(4, 2, 4)
        // col=8:  lines 3..4 → IndentGuide(8, 3, 4)
        // col=12: line  4    → IndentGuide(12, 4, 4)
        var guides = Guides("a\n    b\n        c\n            d\n                e\na");
        guides.Should().HaveCount(4);
        guides[0].Should().Be(new IndentGuide(0,  1, 4));
        guides[1].Should().Be(new IndentGuide(4,  2, 4));
        guides[2].Should().Be(new IndentGuide(8,  3, 4));
        guides[3].Should().Be(new IndentGuide(12, 4, 4));
    }

    // ── All-indented content ──────────────────────────────────────────────

    [Fact]
    public void AllLinesIndented_SingleGuideFullRange()
    {
        // "    a\n    b\n    c"
        // indents: [4, 4, 4]  max=4
        // col=0: indent>0 → all lines → IndentGuide(0, 0, 2)
        var guides = Guides("    a\n    b\n    c");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 0, 2));
    }

    [Fact]
    public void AllLinesDoubleIndented_TwoGuides()
    {
        // "        a\n        b\n        c"
        // indents: [8, 8, 8]  max=8
        // col=0: indent>0 → all → IndentGuide(0, 0, 2)
        // col=4: indent>4 → all → IndentGuide(4, 0, 2)
        var guides = Guides("        a\n        b\n        c");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 0, 2));
        guides[1].Should().Be(new IndentGuide(4, 0, 2));
    }

    // ── Blank line context propagation ────────────────────────────────────

    [Fact]
    public void BlankBetweenDifferentLevels_MinOfNeighbors()
    {
        // "    a\n\n        b"
        // raw: [4, -1, 8]
        // fromAbove: [4, 4, 8]  fromBelow: [4, 8, 8]
        // filled: [4, min(4,8)=4, 8]
        // col=0: indent>0 → all → IndentGuide(0, 0, 2)
        // col=4: indent>4 → line 2 → IndentGuide(4, 2, 2)
        // (blank line has eff. indent 4, NOT > 4, so col=4 span starts only at line 2)
        var guides = Guides("    a\n\n        b");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 0, 2));
        guides[1].Should().Be(new IndentGuide(4, 2, 2));
    }

    [Fact]
    public void BlankBetweenOutdentedLines_SpanBroken()
    {
        // "a\n    b\n\nc\n    d"
        // raw: [0, 4, -1, 0, 4]
        // fromAbove: [0, 4, 4, 4, 4]  fromBelow: [0, 4, 0, 0, 4]
        // filled: [0, 4, min(4,0)=0, 0, 4]
        // col=0: indent>0 → line 1 → IndentGuide(0,1,1); line 4 → IndentGuide(0,4,4)
        var guides = Guides("a\n    b\n\nc\n    d");
        guides.Should().HaveCount(2);
        guides[0].Should().Be(new IndentGuide(0, 1, 1));
        guides[1].Should().Be(new IndentGuide(0, 4, 4));
    }

    // ── Partial range ─────────────────────────────────────────────────────

    [Fact]
    public void PartialRange_GuidesRelativeToDocumentLines()
    {
        // "a\nb\n    c\n    d\n    e\nf"
        // Request lines 2..4 (the indented block)
        // within that range: indents [4,4,4]  max=4
        // col=0: lines 2..4 → IndentGuide(0, 2, 4)
        var doc = Doc("a\nb\n    c\n    d\n    e\nf");
        var guides = doc.GetIndentGuides(2, 4);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 2, 4));
    }

    [Fact]
    public void RequestSingleLine_ZeroOrOneGuide()
    {
        // Request just line 2 of "a\nb\n    c\nd"
        // indents slice: [4]  max=4
        // col=0: line 2 → IndentGuide(0, 2, 2)
        var doc = Doc("a\nb\n    c\nd");
        var guides = doc.GetIndentGuides(2, 2);
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 2, 2));
    }

    // ── Line count edge cases ─────────────────────────────────────────────

    [Fact]
    public void TwoLineDocument_NoIndent_NoGuides()
    {
        var guides = Guides("a\nb");
        guides.Should().BeEmpty();
    }

    [Fact]
    public void TwoLineDocument_BothIndented_OneGuide()
    {
        // "    a\n    b"
        // indents: [4, 4]  max=4
        // col=0: lines 0..1 → IndentGuide(0, 0, 1)
        var guides = Guides("    a\n    b");
        guides.Should().ContainSingle()
              .Which.Should().Be(new IndentGuide(0, 0, 1));
    }
}
