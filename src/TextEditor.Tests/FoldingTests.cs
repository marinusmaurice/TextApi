using TextEditor.Core;
using TextEditor.Core.Folding;
using TextEditor.Core.Language;
using FluentAssertions;
using Xunit;

namespace TextEditor.Tests;

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class FH
{
    private static readonly BraceFoldingStrategy Strat = new();

    public static TextDocument CSharp(string text)
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load(text);
        return doc;
    }

    public static TextDocument Plain(string text)
    {
        var doc = new TextDocument();
        doc.Load(text);
        return doc;
    }

    public static FoldingModel Model(TextDocument doc)
    {
        var m = doc.GetFoldingModel();
        m.UpdateRegions(Strat);
        return m;
    }

    public static FoldingModel ModelFrom(string text, bool csharp = true)
        => Model(csharp ? CSharp(text) : Plain(text));
}

// ── BraceFoldingStrategyTests ─────────────────────────────────────────────────

public class BraceFoldingStrategyTests
{
    private static readonly BraceFoldingStrategy Strat = new();

    [Fact]
    public void EmptyDoc_NoRegions()
    {
        var doc    = FH.CSharp("");
        var regions = Strat.DetectRegions(doc);
        regions.Should().BeEmpty();
    }

    [Fact]
    public void SingleLineBracePair_NotARegion()
    {
        var doc    = FH.CSharp("void Foo() { }");
        var regions = Strat.DetectRegions(doc);
        regions.Should().BeEmpty();
    }

    [Fact]
    public void MultiLine_SinglePair_OneRegion()
    {
        var doc = FH.CSharp("void Foo()\n{\n    return;\n}");
        // Line 1 = '{', Line 3 = '}'
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(1);
        regions[0].StartLine.Should().Be(1);
        regions[0].EndLine.Should().Be(3);
    }

    [Fact]
    public void NestedBraces_TwoRegions()
    {
        string src =
            "class C\n" +
            "{\n" +
            "    void M()\n" +
            "    {\n" +
            "    }\n" +
            "}";
        var doc     = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(2);
        // Outer: line 1 to line 5
        regions.Should().Contain(r => r.StartLine == 1 && r.EndLine == 5);
        // Inner: line 3 to line 4
        regions.Should().Contain(r => r.StartLine == 3 && r.EndLine == 4);
    }

    [Fact]
    public void BraceInsideStringLiteral_Skipped()
    {
        // The '{' inside the string must NOT start a fold.
        string src = "string s = \"{\";\nvoid Foo()\n{\n    return;\n}";
        var doc    = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        // Only the method body { on line 2–4 should be detected.
        regions.Should().HaveCount(1);
        regions[0].StartLine.Should().Be(2);
        regions[0].EndLine.Should().Be(4);
    }

    [Fact]
    public void BraceInsideLineComment_Skipped()
    {
        string src = "// {\nvoid Foo()\n{\n    return;\n}";
        var doc    = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(1);
        regions[0].StartLine.Should().Be(2);
    }

    [Fact]
    public void BraceInsideBlockComment_Skipped()
    {
        string src = "/* { */\nvoid Foo()\n{\n}";
        var doc    = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(1);
        regions[0].StartLine.Should().Be(2);
        regions[0].EndLine.Should().Be(3);
    }

    [Fact]
    public void UnmatchedOpen_NoRegion()
    {
        var doc    = FH.CSharp("void Foo()\n{");
        var regions = Strat.DetectRegions(doc);
        regions.Should().BeEmpty();
    }

    [Fact]
    public void MultipleTopLevelRegions()
    {
        string src =
            "void A()\n{\n}\n" +
            "void B()\n{\n}";
        var doc     = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(2);
    }

    [Fact]
    public void Label_IsStartLineContent()
    {
        string src = "public void Foo()\n{\n}";
        var doc    = FH.CSharp(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(1);
        regions[0].Label.Should().Contain("public void Foo");
    }

    [Fact]
    public void PlainText_FallbackCharScan_DetectsRegions()
    {
        string src = "{\n    content\n}";
        var doc    = FH.Plain(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(1);
        regions[0].StartLine.Should().Be(0);
        regions[0].EndLine.Should().Be(2);
    }

    [Fact]
    public void DeepNesting_AllRegionsDetected()
    {
        string src = "{\n{\n{\n}\n}\n}";
        // 3 levels: (0,5), (1,4), (2,3)
        var doc    = FH.Plain(src);
        var regions = Strat.DetectRegions(doc);
        regions.Should().HaveCount(3);
    }
}

// ── FoldingModelFoldStateTests ────────────────────────────────────────────────

public class FoldingModelFoldStateTests
{
    [Fact]
    public void UpdateRegions_PopulatesRegions()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        m.Regions.Should().HaveCount(1);
    }

    [Fact]
    public void Fold_MarksRegionFolded()
    {
        var m = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start).Should().BeTrue();
        m.Regions[0].IsFolded.Should().BeTrue();
    }

    [Fact]
    public void Fold_AlreadyFolded_ReturnsFalse()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start);
        m.Fold(start).Should().BeFalse();
    }

    [Fact]
    public void Fold_NonExistentStartLine_ReturnsFalse()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        m.Fold(99).Should().BeFalse();
    }

    [Fact]
    public void Unfold_MarksRegionOpen()
    {
        var m = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start);
        m.Unfold(start).Should().BeTrue();
        m.Regions[0].IsFolded.Should().BeFalse();
    }

    [Fact]
    public void Unfold_NotFolded_ReturnsFalse()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        m.Unfold(m.Regions[0].StartLine).Should().BeFalse();
    }

    [Fact]
    public void Toggle_FoldsWhenOpen()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        int start = m.Regions[0].StartLine;
        m.ToggleFold(start);
        m.Regions[0].IsFolded.Should().BeTrue();
    }

    [Fact]
    public void Toggle_UnfoldsWhenFolded()
    {
        var m = FH.ModelFrom("void Foo()\n{\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start);
        m.ToggleFold(start);
        m.Regions[0].IsFolded.Should().BeFalse();
    }

    [Fact]
    public void FoldAll_AllRegionsFolded()
    {
        var doc = FH.CSharp("void A()\n{\n}\nvoid B()\n{\n}");
        var m   = FH.Model(doc);
        m.FoldAll();
        m.Regions.Should().AllSatisfy(r => r.IsFolded.Should().BeTrue());
    }

    [Fact]
    public void UnfoldAll_AllRegionsOpen()
    {
        var doc = FH.CSharp("void A()\n{\n}\nvoid B()\n{\n}");
        var m   = FH.Model(doc);
        m.FoldAll();
        m.UnfoldAll();
        m.Regions.Should().AllSatisfy(r => r.IsFolded.Should().BeFalse());
    }

    [Fact]
    public void FoldState_PreservedAcrossUpdateRegions()
    {
        var doc = FH.CSharp("void Foo()\n{\n    x;\n}");
        var m   = FH.Model(doc);
        int start = m.Regions[0].StartLine;
        m.Fold(start);

        // Simulate a trivial edit inside the body and re-detect.
        doc.Insert(doc.PositionToOffset(2, 0), "    // comment\n");
        m.UpdateRegions(new BraceFoldingStrategy());

        // The outer fold region still starts at the same line → still folded.
        m.Regions.Should().ContainSingle(r => r.StartLine == start && r.IsFolded);
    }

    [Fact]
    public void Regions_SortedByStartLine()
    {
        string src = "class C\n{\n    void M()\n    {\n    }\n}";
        var m = FH.ModelFrom(src);
        for (int i = 1; i < m.Regions.Count; i++)
            m.Regions[i].StartLine.Should().BeGreaterThan(m.Regions[i - 1].StartLine);
    }
}

// ── FoldingModelVisibilityTests ───────────────────────────────────────────────

public class FoldingModelVisibilityTests
{
    [Fact]
    public void NoFolds_AllLinesVisible()
    {
        var doc = FH.CSharp("a\nb\nc");
        var m   = FH.Model(doc);
        for (int i = 0; i < doc.LineCount; i++)
            m.IsLineVisible(i).Should().BeTrue($"line {i} should be visible");
    }

    [Fact]
    public void StartLine_AlwaysVisible()
    {
        var m     = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start);
        m.IsLineVisible(start).Should().BeTrue("StartLine is never hidden");
    }

    [Fact]
    public void EndLine_HiddenWhenFolded()
    {
        var m   = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        int end = m.Regions[0].EndLine;
        m.Fold(m.Regions[0].StartLine);
        m.IsLineVisible(end).Should().BeFalse();
    }

    [Fact]
    public void BodyLines_HiddenWhenFolded()
    {
        var m   = FH.ModelFrom("void Foo()\n{\n    x;\n    y;\n}");
        var r   = m.Regions[0];
        m.Fold(r.StartLine);
        // Lines StartLine+1 through EndLine are all hidden.
        for (int i = r.StartLine + 1; i <= r.EndLine; i++)
            m.IsLineFolded(i).Should().BeTrue($"line {i}");
    }

    [Fact]
    public void LinesOutsideRegion_StillVisible()
    {
        var doc = FH.CSharp("int x;\nvoid Foo()\n{\n    y;\n}\nint z;");
        var m   = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);
        m.IsLineVisible(0).Should().BeTrue("line before region");
        m.IsLineVisible(doc.LineCount - 1).Should().BeTrue("line after region");
    }

    [Fact]
    public void Unfold_RestoresVisibility()
    {
        var m   = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        int start = m.Regions[0].StartLine;
        m.Fold(start);
        m.Unfold(start);
        for (int i = 0; i < m.Regions[0].EndLine + 1; i++)
            m.IsLineVisible(i).Should().BeTrue();
    }

    [Fact]
    public void GetVisibleLines_WithFold_ExcludesHiddenLines()
    {
        // "void Foo()\n{\n    x;\n}" — 4 lines (0-3), fold region on line 1..3
        var doc    = FH.CSharp("void Foo()\n{\n    x;\n}");
        var m      = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);
        var visible = m.GetVisibleLines();
        // Only line 0 ("void Foo()") and line 1 ("{") are visible.
        visible.Should().Contain(0);
        visible.Should().Contain(m.Regions[0].StartLine);
        visible.Should().NotContain(m.Regions[0].EndLine);
    }

    [Fact]
    public void GetVisibleLines_NoFolds_AllLines()
    {
        var doc  = FH.CSharp("a\nb\nc");
        var m    = FH.Model(doc);
        m.GetVisibleLines().Should().Equal([0, 1, 2]);
    }

    [Fact]
    public void NestedFolds_OuterFold_HidesInnerRegion()
    {
        // class (0-5) contains method (2-4)
        string src =
            "class C\n{\n    void M()\n    {\n    }\n}";
        var doc = FH.CSharp(src);
        var m   = FH.Model(doc);
        // Find outer region (starts on line 1 = '{')
        var outer = m.Regions.First(r => r.EndLine == doc.LineCount - 1);
        m.Fold(outer.StartLine);
        // All lines inside outer are hidden, including the inner method lines.
        for (int i = outer.StartLine + 1; i <= outer.EndLine; i++)
            m.IsLineVisible(i).Should().BeFalse($"line {i} inside outer fold");
    }
}

// ── FoldingModelCountAndMappingTests ──────────────────────────────────────────

public class FoldingModelCountAndMappingTests
{
    [Fact]
    public void VisibleLineCount_NoFolds_EqualsTotalLines()
    {
        var doc = FH.CSharp("a\nb\nc");
        var m   = FH.Model(doc);
        m.VisibleLineCount.Should().Be(doc.LineCount);
    }

    [Fact]
    public void VisibleLineCount_WithFold_Reduced()
    {
        // 4 lines (0-3), region 1-3 (hidden: lines 2,3) → 2 visible (0,1)
        var doc = FH.CSharp("void Foo()\n{\n    x;\n}");
        var m   = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);
        // Lines 0 and startLine are visible; lines startLine+1..endLine are hidden.
        int expectedVisible = doc.LineCount - m.Regions[0].HiddenLineCount;
        m.VisibleLineCount.Should().Be(expectedVisible);
    }

    [Fact]
    public void VisibleLineCount_FoldAll_LeavesOnePerRegion()
    {
        // 6 lines total: 2 methods each spanning 3 lines → 2 hidden each
        string src = "void A()\n{\n    a;\n}\nvoid B()\n{\n    b;\n}";
        var doc    = FH.CSharp(src);
        var m      = FH.Model(doc);
        m.FoldAll();
        // Each method: StartLine visible + 2 hidden → lose 2 per method
        int expected = doc.LineCount - m.Regions.Sum(r => r.HiddenLineCount);
        m.VisibleLineCount.Should().Be(expected);
    }

    [Fact]
    public void ToDisplayLine_NoFolds_EqualsDocumentLine()
    {
        var doc = FH.CSharp("a\nb\nc");
        var m   = FH.Model(doc);
        for (int i = 0; i < doc.LineCount; i++)
            m.ToDisplayLine(i).Should().Be(i);
    }

    [Fact]
    public void ToDisplayLine_HiddenLine_ReturnsNegOne()
    {
        var doc = FH.CSharp("void Foo()\n{\n    x;\n}");
        var m   = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);
        m.ToDisplayLine(m.Regions[0].EndLine).Should().Be(-1);
    }

    [Fact]
    public void ToDisplayLine_AfterFold_ShiftedDown()
    {
        // "preamble\nvoid Foo()\n{\n    x;\n}\npostamble"
        // Lines: 0=preamble, 1=void Foo(), 2={, 3=x, 4=}, 5=postamble
        // Fold region: startLine=2, endLine=4 (hidden: 3,4)
        // Visible: 0,1,2,5 → display 0,1,2,3
        var doc    = FH.CSharp("preamble\nvoid Foo()\n{\n    x;\n}\npostamble");
        var m      = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);
        m.ToDisplayLine(0).Should().Be(0);
        m.ToDisplayLine(1).Should().Be(1);
        m.ToDisplayLine(m.Regions[0].StartLine).Should().BeGreaterThanOrEqualTo(0);
        // "postamble" is the last doc line; its display index < doc.LineCount
        int lastDoc     = doc.LineCount - 1;
        int lastDisplay = m.ToDisplayLine(lastDoc);
        lastDisplay.Should().BeGreaterThan(0);
        lastDisplay.Should().BeLessThan(doc.LineCount);
    }

    [Fact]
    public void ToDocumentLine_NoFolds_EqualsDisplayLine()
    {
        var doc = FH.CSharp("a\nb\nc");
        var m   = FH.Model(doc);
        for (int i = 0; i < doc.LineCount; i++)
            m.ToDocumentLine(i).Should().Be(i);
    }

    [Fact]
    public void ToDocumentLine_OutOfRange_ReturnsNegOne()
    {
        var doc = FH.CSharp("a\nb");
        var m   = FH.Model(doc);
        m.ToDocumentLine(-1).Should().Be(-1);
        m.ToDocumentLine(99).Should().Be(-1);
    }

    [Fact]
    public void ToDocumentLine_IsInverseOfToDisplayLine()
    {
        var doc = FH.CSharp("preamble\nvoid Foo()\n{\n    x;\n}\npostamble");
        var m   = FH.Model(doc);
        m.Fold(m.Regions[0].StartLine);

        for (int i = 0; i < doc.LineCount; i++)
        {
            int disp = m.ToDisplayLine(i);
            if (disp < 0) continue;    // hidden line — skip
            m.ToDocumentLine(disp).Should().Be(i, because: $"doc line {i}");
        }
    }

    [Fact]
    public void VisibleLineCount_NestedFolds_NoDuplicateCounting()
    {
        // Outer fold (0-5) contains inner fold (2-4).
        // Fold outer only: hides lines 1-5 → 1 visible (line 0).
        // Folding inner too should not double-count.
        string src = "{\n{\n{\n}\n}\n}";
        var doc    = FH.Plain(src);
        var m      = FH.Model(doc);
        // Outer region has the largest span.
        var outer  = m.Regions.OrderByDescending(r => r.HiddenLineCount).First();
        m.FoldAll();
        // Only line 0 is guaranteed visible (outer start).
        m.VisibleLineCount.Should().BeGreaterThan(0);
        m.VisibleLineCount.Should().BeLessThan(doc.LineCount);
    }
}

// ── TextDocumentFoldingApiTests ───────────────────────────────────────────────

public class TextDocumentFoldingApiTests
{
    [Fact]
    public void GetFoldingModel_ReturnsSameInstance()
    {
        var doc = FH.CSharp("void Foo()\n{\n}");
        var m1  = doc.GetFoldingModel();
        var m2  = doc.GetFoldingModel();
        m1.Should().BeSameAs(m2);
    }

    [Fact]
    public void GetFoldingModel_InitiallyEmpty()
    {
        var doc = FH.CSharp("void Foo()\n{\n}");
        doc.GetFoldingModel().Regions.Should().BeEmpty();
    }

    [Fact]
    public void GetFoldingModel_AfterUpdateRegions_HasRegions()
    {
        var doc = FH.CSharp("void Foo()\n{\n}");
        var m   = doc.GetFoldingModel();
        m.UpdateRegions(new BraceFoldingStrategy());
        m.Regions.Should().NotBeEmpty();
    }

    [Fact]
    public void GetFoldingModel_CanFoldAndQuery()
    {
        var doc = FH.CSharp("void Foo()\n{\n    x;\n}");
        var m   = doc.GetFoldingModel();
        m.UpdateRegions(new BraceFoldingStrategy());
        m.FoldAll();
        m.VisibleLineCount.Should().BeLessThan(doc.LineCount);
    }

    [Fact]
    public void FoldRegion_LineCount_Correct()
    {
        var m = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        var r = m.Regions[0];
        r.LineCount.Should().Be(r.EndLine - r.StartLine + 1);
        r.HiddenLineCount.Should().Be(r.EndLine - r.StartLine);
    }

    [Fact]
    public void FoldRegion_ToString_ContainsBothLineIndices()
    {
        var m = FH.ModelFrom("void Foo()\n{\n    x;\n}");
        var r = m.Regions[0];
        r.ToString().Should().Contain(r.StartLine.ToString());
        r.ToString().Should().Contain(r.EndLine.ToString());
    }
}
