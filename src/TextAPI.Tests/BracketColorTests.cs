using TextAPI.Core;
using TextAPI.Core.Language;
using FluentAssertions;
using Xunit;

namespace TextAPI.Tests;

public class BracketColorTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static TextDocument Doc(string content, bool withTokeniser = false)
    {
        var d = withTokeniser
            ? new TextDocument(new CSharpTokeniser())
            : new TextDocument();
        d.Load(content);
        return d;
    }

    private static IReadOnlyList<BracketPair> Pairs(string content, bool withTokeniser = false)
    {
        var doc = Doc(content, withTokeniser);
        return doc.GetBracketPairs(0, doc.LineCount - 1);
    }

    // ── Basic colorization ────────────────────────────────────────────────────

    [Fact]
    public void FlatPairs_AllColorZero()
    {
        // "()()()" → 3 pairs all color 0
        var pairs = Pairs("()()()");
        pairs.Should().HaveCount(3);
        pairs.Should().OnlyContain(p => p.ColorIndex == 0);
        pairs.Should().OnlyContain(p => p.OpenOffset >= 0 && p.CloseOffset >= 0);
    }

    [Fact]
    public void NestedOnce_IncrementDepth()
    {
        // "(())" → outer color 0, inner color 1
        var pairs = Pairs("(())");
        pairs.Should().HaveCount(2);
        // Sorted by open offset: outer first (offset 0), inner second (offset 1)
        pairs[0].ColorIndex.Should().Be(0);
        pairs[1].ColorIndex.Should().Be(1);
        pairs[0].OpenOffset.Should().Be(0);
        pairs[1].OpenOffset.Should().Be(1);
    }

    [Fact]
    public void NestedTwice_Color2()
    {
        // "((()))" → depths 0,1,2
        var pairs = Pairs("((()))");
        pairs.Should().HaveCount(3);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[1].ColorIndex.Should().Be(1);
        pairs[2].ColorIndex.Should().Be(2);
    }

    [Fact]
    public void TripleNested_Resets()
    {
        // "(((())))" → depths 0,1,2,0
        var pairs = Pairs("(((())))");
        pairs.Should().HaveCount(4);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[1].ColorIndex.Should().Be(1);
        pairs[2].ColorIndex.Should().Be(2);
        pairs[3].ColorIndex.Should().Be(0);
    }

    // ── Mixed bracket types ───────────────────────────────────────────────────

    [Fact]
    public void MixedTypes_IndependentDepth()
    {
        // "({[]})" → ( at depth 0, { at depth 1, [ at depth 2
        var pairs = Pairs("({[]})");
        pairs.Should().HaveCount(3);
        pairs[0].ColorIndex.Should().Be(0); // (
        pairs[1].ColorIndex.Should().Be(1); // {
        pairs[2].ColorIndex.Should().Be(2); // [
    }

    [Fact]
    public void CurlyBraces_ColorZero()
    {
        var pairs = Pairs("{}");
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void SquareBrackets_ColorZero()
    {
        var pairs = Pairs("[]");
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    // ── Unmatched ─────────────────────────────────────────────────────────────

    [Fact]
    public void UnmatchedOpen_ColorMinusOne()
    {
        var pairs = Pairs("(");
        pairs.Should().HaveCount(1);
        pairs[0].CloseOffset.Should().Be(-1);
        pairs[0].ColorIndex.Should().Be(-1);
        pairs[0].OpenOffset.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void UnmatchedClose_ColorMinusOne()
    {
        var pairs = Pairs(")");
        pairs.Should().HaveCount(1);
        pairs[0].OpenOffset.Should().Be(-1);
        pairs[0].ColorIndex.Should().Be(-1);
        pairs[0].CloseOffset.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void UnmatchedInNested()
    {
        // "(()" → inner ( has no close; outer matches
        // After walking: '(' depth0→push(0,0),depth=1; '(' depth1→push(1,1),depth=2;
        // ')' pops (1,1) → matched pair (1,2,1%3=1), depth=1
        // remaining stack: (0,0) → unmatched open
        var pairs = Pairs("(()");
        pairs.Should().HaveCount(2);
        // One matched pair (the inner parens at offset 1 and 2)
        var matched = pairs.Where(p => p.ColorIndex >= 0).ToList();
        matched.Should().HaveCount(1);
        matched[0].ColorIndex.Should().Be(1);
        // One unmatched open (offset 0)
        var unmatched = pairs.Where(p => p.ColorIndex == -1).ToList();
        unmatched.Should().HaveCount(1);
        unmatched[0].OpenOffset.Should().Be(0);
        unmatched[0].CloseOffset.Should().Be(-1);
    }

    [Fact]
    public void MixedMatchedAndUnmatched()
    {
        // "(())" + unmatched "(" at end: "(())) ("
        // Actually simpler: "() ("  → 1 matched (color 0), 1 unmatched open
        var pairs = Pairs("() (");
        var matched   = pairs.Where(p => p.ColorIndex >= 0).ToList();
        var unmatched = pairs.Where(p => p.ColorIndex == -1).ToList();
        matched.Should().HaveCount(1);
        unmatched.Should().HaveCount(1);
        matched[0].ColorIndex.Should().Be(0);
    }

    // ── Line range ────────────────────────────────────────────────────────────

    [Fact]
    public void SingleLine_FullRange()
    {
        var doc  = Doc("(hello)");
        var pairs = doc.GetBracketPairs(0, 0);
        pairs.Should().HaveCount(1);
        pairs[0].OpenOffset.Should().Be(0);
        pairs[0].CloseOffset.Should().Be(6);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void MultiLine_CorrectOffsets()
    {
        // Line 0: "(a"  (length 2, offset 0-1, newline at 2)
        // Line 1: "b)"  (starts at offset 3)
        var doc  = Doc("(a\nb)");
        var pairs = doc.GetBracketPairs(0, doc.LineCount - 1);
        pairs.Should().HaveCount(1);
        pairs[0].OpenOffset.Should().Be(0);
        pairs[0].CloseOffset.Should().Be(4);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void StartLineFilter_OnlyReturnsOpenInRange()
    {
        // "(\n(\n)" → Line 0 has '(' at offset 0, Line 1 has '(' at offset 2,
        // Line 2 has ')' at offset 4
        // Request only lines 1..2: the '(' on line 0 is excluded from the range,
        // so the stack starts fresh. Line 1's '(' matches line 2's ')' → 1 matched pair.
        var doc  = Doc("(\n(\n)");
        var pairs = doc.GetBracketPairs(1, 2);
        // Only 1 matched pair: line1 '(' with line2 ')'
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[0].CloseOffset.Should().BeGreaterThan(0);
    }

    // ── Token-aware tests (with CSharpTokeniser) ──────────────────────────────

    [Fact]
    public void BracketsInString_Excluded()
    {
        // var s = "(hello)"; → brackets inside string literal should not count
        var pairs = Pairs("var s = \"(hello)\";", withTokeniser: true);
        pairs.Should().BeEmpty();
    }

    [Fact]
    public void BracketsInComment_Excluded()
    {
        // // (comment) → brackets inside line comment should not count
        var pairs = Pairs("// (comment)", withTokeniser: true);
        pairs.Should().BeEmpty();
    }

    [Fact]
    public void BracketsInCode_Included()
    {
        // "if (x > 0) { }" → ( at col 3, ) at col 9, { at col 11, } at col 13
        var pairs = Pairs("if (x > 0) { }", withTokeniser: true);
        pairs.Should().HaveCount(2);
        pairs.Should().OnlyContain(p => p.ColorIndex >= 0);
    }

    [Fact]
    public void MixedCodeAndString()
    {
        // foo("("); → only the outer ( ) is a bracket pair
        var pairs = Pairs("foo(\"(\");", withTokeniser: true);
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    // ── Empty / edge cases ────────────────────────────────────────────────────

    [Fact]
    public void EmptyDocument_NoPairs()
    {
        var pairs = Pairs("");
        pairs.Should().BeEmpty();
    }

    [Fact]
    public void NoBrackets_NoPairs()
    {
        var pairs = Pairs("hello world");
        pairs.Should().BeEmpty();
    }

    [Fact]
    public void EmptyLine_NoPairs()
    {
        var pairs = Pairs("   ");
        pairs.Should().BeEmpty();
    }

    // ── Return structure ──────────────────────────────────────────────────────

    [Fact]
    public void Pairs_SortedByOpenOffset()
    {
        // "()(())" → pairs at offsets 0, 2, 3
        var pairs = Pairs("()(())");
        pairs.Should().HaveCount(3);
        // Sorted ascending by open offset
        for (int i = 1; i < pairs.Count; i++)
        {
            int prev = pairs[i - 1].OpenOffset >= 0 ? pairs[i - 1].OpenOffset : pairs[i - 1].CloseOffset;
            int curr = pairs[i].OpenOffset     >= 0 ? pairs[i].OpenOffset     : pairs[i].CloseOffset;
            curr.Should().BeGreaterThanOrEqualTo(prev);
        }
    }

    [Fact]
    public void UnmatchedClose_HasNegativeOpenOffset()
    {
        var pairs = Pairs(")");
        pairs.Should().HaveCount(1);
        pairs[0].OpenOffset.Should().Be(-1);
    }

    [Fact]
    public void UnmatchedOpen_HasNegativeCloseOffset()
    {
        var pairs = Pairs("(");
        pairs.Should().HaveCount(1);
        pairs[0].CloseOffset.Should().Be(-1);
    }

    // ── Color cycling ─────────────────────────────────────────────────────────

    [Fact]
    public void ColorCycles_AfterDepth2()
    {
        // "((()))" has depths 0,1,2 → after depth 2 the next level wraps
        // "(((())))": depths 0,1,2,0
        var pairs = Pairs("(((())))");
        pairs.Should().HaveCount(4);
        pairs[3].ColorIndex.Should().Be(0);  // depth 3 → 3%3 = 0
    }

    [Fact]
    public void FourLevelsDeep_Colors_0_1_2_0()
    {
        var pairs = Pairs("(((())))");
        pairs.Should().HaveCount(4);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[1].ColorIndex.Should().Be(1);
        pairs[2].ColorIndex.Should().Be(2);
        pairs[3].ColorIndex.Should().Be(0);
    }

    // ── Additional coverage ───────────────────────────────────────────────────

    [Fact]
    public void MultipleUnmatchedClose()
    {
        var pairs = Pairs("))");
        pairs.Should().HaveCount(2);
        pairs.Should().OnlyContain(p => p.ColorIndex == -1 && p.OpenOffset == -1);
    }

    [Fact]
    public void MatchedThenUnmatched()
    {
        // "()(": 1 matched + 1 unmatched open
        var pairs = Pairs("()(");
        pairs.Where(p => p.ColorIndex >= 0).Should().HaveCount(1);
        pairs.Where(p => p.ColorIndex == -1).Should().HaveCount(1);
    }

    [Fact]
    public void DeepNestingWithCurly()
    {
        // "{{{}}}": depths 0,1,2
        var pairs = Pairs("{{{}}}");
        pairs.Should().HaveCount(3);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[1].ColorIndex.Should().Be(1);
        pairs[2].ColorIndex.Should().Be(2);
    }

    [Fact]
    public void BracketsAfterString_Included()
    {
        // "var s = \"x\"; foo();" → the () after the string should be included
        var pairs = Pairs("var s = \"x\"; foo();", withTokeniser: true);
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void LineRangeExcludesOutOfRange()
    {
        // 3-line doc: "(\n()\n)"
        // Line 0: ( — starts at offset 0
        // Line 1: () — starts at offset 2
        // Line 2: ) — starts at offset 5
        // Query only line 1 → should return 1 matched pair from ()
        var doc  = Doc("(\n()\n)");
        var pairs = doc.GetBracketPairs(1, 1);
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
        pairs[0].CloseOffset.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BlockCommentBrackets_Excluded()
    {
        // "/* ( */ foo()" → brackets in block comment excluded
        var pairs = Pairs("/* ( */ foo()", withTokeniser: true);
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void CharLiteralBracket_Excluded()
    {
        // "var c = '('; x()" → bracket inside char literal excluded
        // CSharpTokeniser handles char literals as "string" type
        var pairs = Pairs("var c = '('; x()", withTokeniser: true);
        pairs.Should().HaveCount(1);
        pairs[0].ColorIndex.Should().Be(0);
    }

    [Fact]
    public void OpenBracketOffset_CorrectDocumentOffset()
    {
        // "abc(def)" → '(' is at col 3, ')' at col 7
        var doc  = Doc("abc(def)");
        var pairs = doc.GetBracketPairs(0, 0);
        pairs.Should().HaveCount(1);
        pairs[0].OpenOffset.Should().Be(3);
        pairs[0].CloseOffset.Should().Be(7);
    }

    [Fact]
    public void ClampStartLine_BelowZero()
    {
        var doc  = Doc("()");
        // startLine = -5 should be clamped to 0
        var pairs = doc.GetBracketPairs(-5, 0);
        pairs.Should().HaveCount(1);
    }

    [Fact]
    public void ClampEndLine_AboveMax()
    {
        var doc  = Doc("()");
        // endLine = 999 should be clamped to LineCount-1
        var pairs = doc.GetBracketPairs(0, 999);
        pairs.Should().HaveCount(1);
    }
}
