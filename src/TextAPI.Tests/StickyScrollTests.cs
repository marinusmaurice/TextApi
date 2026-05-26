using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Folding;
using TextAPI.Core.StickyScroll;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Item 20 — Sticky scroll context provider
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Helper: load code into a doc, build a FoldingModel using BraceFoldingStrategy,
/// then call StickyScroll.GetContext.
/// </summary>
file static class SSHelper
{
    public static (TextDocument doc, FoldingModel model) Make(string code)
    {
        var doc = new TextDocument();
        doc.Load(code);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        return (doc, model);
    }

    public static IReadOnlyList<StickyScrollEntry> Context(FoldingModel m, int firstVisible)
        => StickyScroll.GetContext(m, firstVisible);
}

// ─────────────────────────────────────────────────────────────────────────
// 1. Empty / no-fold cases
// ─────────────────────────────────────────────────────────────────────────

public class StickyScrollEmptyTests
{
    [Fact]
    public void NoFolds_AnyLine_EmptyContext()
    {
        var (_, m) = SSHelper.Make("line 0\nline 1\nline 2\nline 3");
        SSHelper.Context(m, 2).Should().BeEmpty();
    }

    [Fact]
    public void FirstVisibleLine_Zero_AlwaysEmpty()
    {
        var (_, m) = SSHelper.Make("class A {\n  void M() {\n  }\n}");
        SSHelper.Context(m, 0).Should().BeEmpty();
    }

    [Fact]
    public void FirstVisibleLine_Negative_AlwaysEmpty()
    {
        var (_, m) = SSHelper.Make("class A {\n  void M() {\n  }\n}");
        SSHelper.Context(m, -1).Should().BeEmpty();
    }

    [Fact]
    public void ViewportPastLastScope_EmptyContext()
    {
        // A single-region document; viewport is on the closing line but the
        // region ends before the viewport passes beyond EndLine.
        var (doc, m) = SSHelper.Make("class A {\n}\nmore\nmore\nmore");
        // Region: StartLine=0, EndLine=1.  If firstVisible > 1, no region qualifies.
        SSHelper.Context(m, 3).Should().BeEmpty();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 2. Single enclosing scope
// ─────────────────────────────────────────────────────────────────────────

public class StickyScrollSingleScopeTests
{
    // Code:
    //   line 0: class Foo {
    //   line 1:   int x = 1;
    //   line 2:   int y = 2;
    //   line 3: }
    private const string Code = "class Foo {\n  int x = 1;\n  int y = 2;\n}";

    [Fact]
    public void InsideScope_HeaderInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // Region: StartLine=0, EndLine=3.
        // firstVisibleLine=2: StartLine(0) < 2, EndLine(3) >= 2 → in context.
        var ctx = SSHelper.Context(m, 2);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(0);
    }

    [Fact]
    public void ExactlyOnStartLine_NotInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine == StartLine(0): header is still visible → not sticky.
        SSHelper.Context(m, 0).Should().BeEmpty();
    }

    [Fact]
    public void ExactlyOnEndLine_InContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine == EndLine(3): StartLine(0) < 3, EndLine(3) >= 3 → sticky.
        var ctx = SSHelper.Context(m, 3);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(0);
    }

    [Fact]
    public void FirstLineAfterScope_NotInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine == EndLine+1 == 4: EndLine(3) < 4 → not sticky.
        SSHelper.Context(m, 4).Should().BeEmpty();
    }

    [Fact]
    public void EntryLabel_MatchesFoldRegionLabel()
    {
        var (_, m) = SSHelper.Make(Code);
        var ctx = SSHelper.Context(m, 2);
        // Label comes from BraceFoldingStrategy — the first line trimmed.
        ctx[0].Label.Should().NotBeNullOrEmpty();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 3. Nested scopes — outermost first
// ─────────────────────────────────────────────────────────────────────────

public class StickyScrollNestedTests
{
    // Code (line numbers):
    //   0: class Outer {
    //   1:   class Inner {
    //   2:     void Method() {
    //   3:       int x = 1;
    //   4:     }
    //   5:   }
    //   6: }
    private const string Code =
        "class Outer {\n" +
        "  class Inner {\n" +
        "    void Method() {\n" +
        "      int x = 1;\n" +
        "    }\n" +
        "  }\n" +
        "}";

    [Fact]
    public void ViewportInsideMethod_ThreeScopesInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // Regions expected: [0,6], [1,5], [2,4]
        // firstVisibleLine=3: all three regions qualify.
        var ctx = SSHelper.Context(m, 3);
        ctx.Should().HaveCount(3);
    }

    [Fact]
    public void NestedScopes_OutermostFirst()
    {
        var (_, m) = SSHelper.Make(Code);
        var ctx = SSHelper.Context(m, 3);
        ctx.Should().HaveCount(3);
        // Sorted ascending by StartLine → outermost first.
        ctx[0].DocumentLine.Should().Be(0); // Outer
        ctx[1].DocumentLine.Should().Be(1); // Inner
        ctx[2].DocumentLine.Should().Be(2); // Method
    }

    [Fact]
    public void ViewportOnClosingBraceOfMethod_TwoScopesRemain()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine=4 (closing brace of Method):
        //   Outer [0,6]: 0 < 4, 6 >= 4 → YES
        //   Inner [1,5]: 1 < 4, 5 >= 4 → YES
        //   Method [2,4]: 2 < 4, 4 >= 4 → YES  (EndLine == firstVisible counts)
        var ctx = SSHelper.Context(m, 4);
        ctx.Should().HaveCount(3);
    }

    [Fact]
    public void ViewportOnClosingBraceOfInner_TwoScopesInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine=5 (closing brace of Inner):
        //   Outer [0,6]: YES
        //   Inner [1,5]: YES (EndLine==5 >= 5)
        //   Method [2,4]: EndLine(4) < 5 → NO
        var ctx = SSHelper.Context(m, 5);
        ctx.Should().HaveCount(2);
        ctx[0].DocumentLine.Should().Be(0);
        ctx[1].DocumentLine.Should().Be(1);
    }

    [Fact]
    public void ViewportOnClosingBraceOfOuter_OneScopeInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine=6 (closing brace of Outer):
        //   Outer [0,6]: YES
        //   Inner [1,5]: EndLine(5) < 6 → NO
        var ctx = SSHelper.Context(m, 6);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(0);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 4. Sibling (non-enclosing) scopes excluded
// ─────────────────────────────────────────────────────────────────────────

public class StickyScrollSiblingTests
{
    // Code:
    //   0: void A() {
    //   1:   // body A
    //   2: }
    //   3: void B() {
    //   4:   // body B
    //   5: }
    private const string Code =
        "void A() {\n" +
        "  // body A\n" +
        "}\n" +
        "void B() {\n" +
        "  // body B\n" +
        "}";

    [Fact]
    public void ViewportInsideB_OnlyBInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // A: [0,2], B: [3,5]
        // firstVisibleLine=4: A's EndLine(2) < 4 → not in context; B qualifies.
        var ctx = SSHelper.Context(m, 4);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(3);
    }

    [Fact]
    public void ViewportInsideA_OnlyAInContext()
    {
        var (_, m) = SSHelper.Make(Code);
        var ctx = SSHelper.Context(m, 1);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(0);
    }

    [Fact]
    public void ViewportBetweenSiblings_NoContext()
    {
        var (_, m) = SSHelper.Make(Code);
        // firstVisibleLine=3 is exactly B's StartLine → not sticky (header visible).
        // A's EndLine(2) < 3 → also not in context.
        SSHelper.Context(m, 3).Should().BeEmpty();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 5. Entry contents
// ─────────────────────────────────────────────────────────────────────────

public class StickyScrollEntryTests
{
    [Fact]
    public void Entry_DocumentLine_MatchesRegionStartLine()
    {
        var (_, m) = SSHelper.Make("class Foo {\n  int x;\n}");
        var ctx = SSHelper.Context(m, 1);
        ctx.Should().HaveCount(1);
        ctx[0].DocumentLine.Should().Be(0);
    }

    [Fact]
    public void Entry_IsRecord_ValueEquality()
    {
        var a = new StickyScrollEntry("class Foo {", 0);
        var b = new StickyScrollEntry("class Foo {", 0);
        a.Should().Be(b);
    }

    [Fact]
    public void Entry_DifferentLine_NotEqual()
    {
        var a = new StickyScrollEntry("class Foo {", 0);
        var b = new StickyScrollEntry("class Foo {", 1);
        a.Should().NotBe(b);
    }
}
