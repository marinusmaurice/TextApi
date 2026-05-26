using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Folding;
using TextAPI.Core.Outline;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Item 21 — Document outline
// ═══════════════════════════════════════════════════════════════════════════

file static class OLHelper
{
    public static FoldingModel MakeModel(string code)
    {
        var doc = new TextDocument();
        doc.Load(code);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        return model;
    }

    /// <summary>Flatten the tree depth-first, returning all nodes in visit order.</summary>
    public static List<OutlineNode> Flatten(IReadOnlyList<OutlineNode> roots)
    {
        var result = new List<OutlineNode>();
        void Visit(OutlineNode n)
        {
            result.Add(n);
            foreach (var c in n.Children) Visit(c);
        }
        foreach (var r in roots) Visit(r);
        return result;
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 1. Empty model
// ─────────────────────────────────────────────────────────────────────────

public class OutlineEmptyTests
{
    [Fact]
    public void NoRegions_ReturnsEmptyTree()
    {
        var model = OLHelper.MakeModel("line 0\nline 1\nline 2");
        OutlineProvider.GetOutline(model).Should().BeEmpty();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 2. Flat regions (no nesting) — all depth-0
// ─────────────────────────────────────────────────────────────────────────

public class OutlineFlatTests
{
    // Code:
    //   0: void A() {
    //   1:   // body
    //   2: }
    //   3: void B() {
    //   4:   // body
    //   5: }
    private const string FlatCode =
        "void A() {\n  // body\n}\nvoid B() {\n  // body\n}";

    [Fact]
    public void TwoSiblingRegions_TwoRootNodes()
    {
        var model = OLHelper.MakeModel(FlatCode);
        var outline = OutlineProvider.GetOutline(model);
        outline.Should().HaveCount(2);
    }

    [Fact]
    public void FlatNodes_AllDepthZero()
    {
        var model = OLHelper.MakeModel(FlatCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Depth.Should().Be(0);
        outline[1].Depth.Should().Be(0);
    }

    [Fact]
    public void FlatNodes_NoChildren()
    {
        var model = OLHelper.MakeModel(FlatCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Children.Should().BeEmpty();
        outline[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void FlatNodes_StartLinesAscending()
    {
        var model = OLHelper.MakeModel(FlatCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].StartLine.Should().BeLessThan(outline[1].StartLine);
    }

    [Fact]
    public void FlatNodes_LabelMatchesFoldRegion()
    {
        var model = OLHelper.MakeModel(FlatCode);
        var outline = OutlineProvider.GetOutline(model);
        var regions = model.Regions;
        // The outline nodes' labels should match the corresponding fold region labels.
        outline[0].Label.Should().Be(regions[0].Label);
        outline[1].Label.Should().Be(regions[1].Label);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 3. Nested regions — parent/child tree
// ─────────────────────────────────────────────────────────────────────────

public class OutlineNestedTests
{
    // Code (6 lines):
    //   0: class Outer {
    //   1:   class Inner {
    //   2:     void M() {
    //   3:       int x;
    //   4:     }
    //   5:   }
    //   6: }
    private const string NestedCode =
        "class Outer {\n  class Inner {\n    void M() {\n      int x;\n    }\n  }\n}";

    [Fact]
    public void ThreeNested_OneRootNode()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline.Should().HaveCount(1);
    }

    [Fact]
    public void RootNode_HasOneChild()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Children.Should().HaveCount(1);
    }

    [Fact]
    public void InnerNode_HasOneChild()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Children[0].Children.Should().HaveCount(1);
    }

    [Fact]
    public void LeafNode_HasNoChildren()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Children[0].Children[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void Depths_Correct()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        var all = OLHelper.Flatten(outline);
        all[0].Depth.Should().Be(0); // Outer
        all[1].Depth.Should().Be(1); // Inner
        all[2].Depth.Should().Be(2); // M
    }

    [Fact]
    public void StartLines_Correct()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        var all = OLHelper.Flatten(outline);
        all[0].StartLine.Should().Be(0);
        all[1].StartLine.Should().Be(1);
        all[2].StartLine.Should().Be(2);
    }

    [Fact]
    public void Labels_MatchFoldRegions()
    {
        var model = OLHelper.MakeModel(NestedCode);
        var outline = OutlineProvider.GetOutline(model);
        var all     = OLHelper.Flatten(outline);
        var regions = model.Regions; // sorted by StartLine
        all.Select(n => n.Label).Should().BeEquivalentTo(regions.Select(r => r.Label));
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 4. Mixed: siblings at multiple levels
// ─────────────────────────────────────────────────────────────────────────

public class OutlineMixedTests
{
    // Code:
    //   0: class A {
    //   1:   void M1() {
    //   2:   }
    //   3:   void M2() {
    //   4:   }
    //   5: }
    private const string MixedCode =
        "class A {\n  void M1() {\n  }\n  void M2() {\n  }\n}";

    [Fact]
    public void OneRoot_TwoChildren()
    {
        var model = OLHelper.MakeModel(MixedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline.Should().HaveCount(1);
        outline[0].Children.Should().HaveCount(2);
    }

    [Fact]
    public void RootDepth_Zero_ChildrenDepth_One()
    {
        var model = OLHelper.MakeModel(MixedCode);
        var outline = OutlineProvider.GetOutline(model);
        outline[0].Depth.Should().Be(0);
        outline[0].Children[0].Depth.Should().Be(1);
        outline[0].Children[1].Depth.Should().Be(1);
    }

    [Fact]
    public void Children_SortedByStartLine()
    {
        var model = OLHelper.MakeModel(MixedCode);
        var outline = OutlineProvider.GetOutline(model);
        var children = outline[0].Children;
        children[0].StartLine.Should().BeLessThan(children[1].StartLine);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 5. Node properties
// ─────────────────────────────────────────────────────────────────────────

public class OutlineNodePropertyTests
{
    [Fact]
    public void EndLine_MatchesFoldRegion()
    {
        var model = OLHelper.MakeModel("class A {\n  int x;\n}");
        var outline = OutlineProvider.GetOutline(model);
        outline[0].EndLine.Should().Be(model.Regions[0].EndLine);
    }

    [Fact]
    public void ToString_ContainsLabelAndRange()
    {
        // Use the real API to get a node, then verify ToString output.
        var model = OLHelper.MakeModel("class Foo {\n  int x;\n}");
        var outline = OutlineProvider.GetOutline(model);
        var str = outline[0].ToString();
        str.Should().Contain(outline[0].Label);
        str.Should().Contain(outline[0].StartLine.ToString());
        str.Should().Contain(outline[0].EndLine.ToString());
    }

    [Fact]
    public void ToString_DepthOne_Indented()
    {
        // Two-level nesting: the inner node is at depth 1 → 2 leading spaces.
        var model = OLHelper.MakeModel("class A {\n  void M() {\n  }\n}");
        var outline = OutlineProvider.GetOutline(model);
        var child = outline[0].Children[0];
        child.Depth.Should().Be(1);
        child.ToString().Should().StartWith("  "); // 1 depth × 2 spaces
    }
}
