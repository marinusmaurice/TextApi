using TextEditor.Core;
using TextEditor.Core.Folding;
using TextEditor.Core.Language;
using FluentAssertions;
using Xunit;

namespace TextEditor.Tests;

// Helpers shared across auto-update tests
file static class FAH
{
    private static readonly BraceFoldingStrategy Strat = new();

    public static (TextDocument doc, FoldingModel model) Setup(string text)
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load(text);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(Strat);
        return (doc, model);
    }

    public static void Redetect(FoldingModel model, TextDocument doc)
        => model.UpdateRegions(Strat);
}

// ── OnInsert remap ────────────────────────────────────────────────────────────

public class FoldingAutoUpdate_InsertTests
{
    private const string TwoMethodDoc = """
        class C
        {
            void A()
            {
                int x = 1;
            }
            void B()
            {
                int y = 2;
            }
        }
        """;

    [Fact]
    public void InsertLinesBeforeRegion_ShiftsStartAndEnd()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        // Label carries "void A" even though StartLine points to the { line (Allman style)
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        int origStart = regionA.StartLine;
        int origEnd   = regionA.EndLine;

        // Insert 2 blank lines at beginning (before everything)
        doc.Insert(0, "\n\n");

        regionA.StartLine.Should().Be(origStart + 2);
        regionA.EndLine.Should().Be(origEnd + 2);
    }

    [Fact]
    public void InsertBetweenTwoInnerRegions_EarlierRegionUnchanged()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        var regionB = model.Regions.First(r => r.Label.Contains("void B"));
        int aStart = regionA.StartLine;
        int aEnd   = regionA.EndLine;

        // Insert a comment line between A's end and B's start
        int insertLine   = regionA.EndLine + 1; // the blank line between the two methods
        int insertOffset = doc.PositionToOffset(insertLine, 0);
        doc.Insert(insertOffset, "    // separator\n");

        // A should be unchanged; B should have shifted by 1
        regionA.StartLine.Should().Be(aStart);
        regionA.EndLine.Should().Be(aEnd);
        regionB.StartLine.Should().BeGreaterThan(aEnd);
    }

    [Fact]
    public void InsertLinesInsideRegion_ExpandsEndLine()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        int origEnd   = regionA.EndLine;
        int origStart = regionA.StartLine;

        // Insert a line inside the body of A (line between { and })
        int insertLine   = regionA.StartLine + 1;
        int insertOffset = doc.PositionToOffset(insertLine, 0);
        doc.Insert(insertOffset, "        int extra = 0;\n");

        regionA.StartLine.Should().Be(origStart);
        regionA.EndLine.Should().Be(origEnd + 1);
    }

    [Fact]
    public void InsertSameLine_NoLineCountChange_IsNoop()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        var snapshot = model.Regions.Select(r => (r.StartLine, r.EndLine)).ToList();

        // Insert text without newlines (same-line edit) — no region shift expected
        int offset = doc.PositionToOffset(0, 5);
        doc.Insert(offset, "/* */");

        for (int i = 0; i < snapshot.Count; i++)
        {
            model.Regions[i].StartLine.Should().Be(snapshot[i].StartLine);
            model.Regions[i].EndLine.Should().Be(snapshot[i].EndLine);
        }
    }

    [Fact]
    public void Insert_FoldedRegion_PreservesFoldState()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        model.Fold(regionA.StartLine);
        regionA.IsFolded.Should().BeTrue();

        // Insert before A — shifts its StartLine
        doc.Insert(0, "\n");

        // The region is still flagged as folded
        regionA.IsFolded.Should().BeTrue();
        // _folded set was rebuilt — folding at new StartLine should work
        model.Unfold(regionA.StartLine).Should().BeTrue();
    }

    [Fact]
    public void Insert_RaisesNoFoldStateChangedEvent()
    {
        var (doc, model) = FAH.Setup(TwoMethodDoc);
        int firedCount = 0;
        model.FoldStateChanged += (_, _) => firedCount++;

        doc.Insert(0, "\n");

        firedCount.Should().Be(0);
    }
}

// ── OnDelete remap ────────────────────────────────────────────────────────────

public class FoldingAutoUpdate_DeleteTests
{
    private const string SimpleDoc = """
        class C
        {
            void A()
            {
                return;
            }
        }
        """;

    [Fact]
    public void DeleteLinesBeforeRegion_ShiftsRegionUp()
    {
        var (doc, model) = FAH.Setup(SimpleDoc);
        var innerA = model.Regions.First(r => r.Label.Contains("void A"));
        int origStart = innerA.StartLine;
        int origEnd   = innerA.EndLine;

        // Delete the first line ("class C")
        int lineLen = doc.GetLine(0).Length + 1; // +1 for '\n'
        doc.Delete(0, lineLen);

        innerA.StartLine.Should().Be(origStart - 1);
        innerA.EndLine.Should().Be(origEnd - 1);
    }

    [Fact]
    public void DeleteLinesAfterRegion_NoChange()
    {
        var (doc, model) = FAH.Setup(SimpleDoc);
        var snapshot = model.Regions.Select(r => (r.StartLine, r.EndLine)).ToList();

        // Delete very last line (the trailing empty line or last "}")
        int lastLine = doc.LineCount - 1;
        int offset   = doc.PositionToOffset(lastLine, 0);
        doc.Delete(offset, doc.GetLine(lastLine).Length);

        for (int i = 0; i < model.Regions.Count && i < snapshot.Count; i++)
        {
            model.Regions[i].StartLine.Should().Be(snapshot[i].StartLine);
        }
    }

    [Fact]
    public void DeleteBodyLines_ShrinksRegion()
    {
        var (doc, model) = FAH.Setup(SimpleDoc);
        var innerA = model.Regions.First(r => r.Label.Contains("void A"));
        int origEnd = innerA.EndLine;

        // Delete the "return;" line inside A's body (StartLine+1 is the first body line)
        int bodyLine   = innerA.StartLine + 1;
        int bodyOffset = doc.PositionToOffset(bodyLine, 0);
        int bodyLen    = doc.GetLine(bodyLine).Length + 1;
        doc.Delete(bodyOffset, bodyLen);

        innerA.EndLine.Should().Be(origEnd - 1);
    }

    [Fact]
    public void DeleteEntireRegionContent_RemovesRegion()
    {
        // A tiny doc where deleting body collapses StartLine == EndLine
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("void F()\n{\n    x();\n}\n");
        var model = doc.GetFoldingModel();
        var strat  = new BraceFoldingStrategy();
        model.UpdateRegions(strat);
        model.Regions.Should().HaveCount(1);

        // Delete lines 1..3 (the { x(); } part except the closing })
        // i.e. delete "\n    x();" to make start == end
        // Actually delete lines so the region collapses to 0 span
        int from = doc.PositionToOffset(1, 0);
        int to   = doc.PositionToOffset(2, 0) + doc.GetLine(2).Length + 1;
        doc.Delete(from, to - from);

        // Region should be removed (newStart >= newEnd)
        model.Regions.Should().BeEmpty();
    }

    [Fact]
    public void Delete_SameLine_IsNoop()
    {
        var (doc, model) = FAH.Setup(SimpleDoc);
        var snapshot = model.Regions.Select(r => (r.StartLine, r.EndLine)).ToList();

        // Delete a single character (no newline) — should not shift any region
        doc.Delete(0, 1);

        for (int i = 0; i < snapshot.Count; i++)
        {
            model.Regions[i].StartLine.Should().Be(snapshot[i].StartLine);
            model.Regions[i].EndLine.Should().Be(snapshot[i].EndLine);
        }
    }
}

// ── Staleness / Invalidate ────────────────────────────────────────────────────

public class FoldingAutoUpdate_StalenessTests
{
    private const string Doc = "class C\n{\n    void F() { }\n}\n";

    [Fact]
    public void AfterLoad_IsStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        var model = doc.GetFoldingModel();    // create model BEFORE load
        doc.Load(Doc);                        // should call Invalidate()
        model.IsStale.Should().BeTrue();
    }

    [Fact]
    public void AfterUpdateRegions_NotStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        model.IsStale.Should().BeFalse();
    }

    [Fact]
    public void AfterUndo_IsStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        model.IsStale.Should().BeFalse();

        doc.Insert(0, "// comment\n");
        doc.Undo();

        model.IsStale.Should().BeTrue();
    }

    [Fact]
    public void AfterRedo_IsStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());

        doc.Insert(0, "// hi\n");
        doc.Undo();
        model.UpdateRegions(new BraceFoldingStrategy()); // clear stale
        doc.Redo();

        model.IsStale.Should().BeTrue();
    }

    [Fact]
    public void AfterReplaceAll_IsStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());

        doc.ReplaceAll("C", "D");

        model.IsStale.Should().BeTrue();
    }

    [Fact]
    public void AfterExecuteComposite_IsStale()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());

        // Use the multi-cursor API which internally calls ExecuteComposite
        var mc = new TextEditor.Core.Cursor.MultiCursor(doc);
        mc.AddCursor(0, 0);
        mc.InsertText("// x\n");

        model.IsStale.Should().BeTrue();
    }
}

// ── Event firing ──────────────────────────────────────────────────────────────

public class FoldingAutoUpdate_EventTests
{
    private const string Doc = "class C\n{\n    void F()\n    {\n        return;\n    }\n}\n";

    [Fact]
    public void UpdateRegions_FiresRegionsChanged()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        int fired = 0;
        model.RegionsChanged += (_, _) => fired++;

        model.UpdateRegions(new BraceFoldingStrategy());

        fired.Should().Be(1);
    }

    [Fact]
    public void Fold_FiresFoldStateChanged()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        int fired = 0;
        model.FoldStateChanged += (_, _) => fired++;

        model.Fold(model.Regions[0].StartLine);

        fired.Should().Be(1);
    }

    [Fact]
    public void Unfold_FiresFoldStateChanged()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        model.FoldAll();
        int fired = 0;
        model.FoldStateChanged += (_, _) => fired++;

        model.Unfold(model.Regions[0].StartLine);

        fired.Should().Be(1);
    }

    [Fact]
    public void FoldAll_FiresFoldStateChangedOnce()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        int fired = 0;
        model.FoldStateChanged += (_, _) => fired++;

        model.FoldAll();

        fired.Should().Be(1);
    }

    [Fact]
    public void UnfoldAll_FiresFoldStateChangedOnce()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        model.FoldAll();
        int fired = 0;
        model.FoldStateChanged += (_, _) => fired++;

        model.UnfoldAll();

        fired.Should().Be(1);
    }

    [Fact]
    public void Insert_DoesNotFireFoldStateChanged()
    {
        var doc   = new TextDocument(new CSharpTokeniser());
        doc.Load(Doc);
        var model = doc.GetFoldingModel();
        model.UpdateRegions(new BraceFoldingStrategy());
        int fired = 0;
        model.FoldStateChanged += (_, _) => fired++;

        doc.Insert(0, "// comment\n");

        fired.Should().Be(0);
    }
}

// ── Round-trip after insert/delete ────────────────────────────────────────────

public class FoldingAutoUpdate_RoundTripTests
{
    private const string MultiMethod = """
        class C
        {
            void A()
            {
                int a = 1;
            }
            void B()
            {
                int b = 2;
            }
        }
        """;

    [Fact]
    public void InsertLine_ThenRedetect_CorrectRegionCount()
    {
        var (doc, model) = FAH.Setup(MultiMethod);
        int origCount = model.Regions.Count;

        // Add a new method
        int insertLine = doc.LineCount - 1; // before last "}"
        int insertOffset = doc.PositionToOffset(insertLine, 0);
        doc.Insert(insertOffset, "    void C()\n    {\n        return;\n    }\n");
        model.UpdateRegions(new BraceFoldingStrategy());

        model.Regions.Count.Should().BeGreaterThan(origCount);
    }

    [Fact]
    public void FoldBeforeInsert_RegionRemainsFolded_AfterShift()
    {
        var (doc, model) = FAH.Setup(MultiMethod);
        var regionB = model.Regions.First(r => r.Label.Contains("void B"));
        model.Fold(regionB.StartLine);
        int origStart = regionB.StartLine;

        // Insert 3 lines on the line BEFORE B's opening { — clearly before the region
        int insertLine = regionB.StartLine - 1; // the "void B()" declaration line
        int offset = doc.PositionToOffset(insertLine, 0);
        doc.Insert(offset, "    // comment1\n    // comment2\n    // comment3\n");

        // Both StartLine and EndLine should have shifted by 3
        regionB.StartLine.Should().Be(origStart + 3);
        regionB.IsFolded.Should().BeTrue();
        // The _folded set was rebuilt — Unfold at new start should work
        model.Unfold(regionB.StartLine).Should().BeTrue();
        model.Fold(regionB.StartLine).Should().BeTrue(); // re-fold for cleanliness
    }

    [Fact]
    public void ToDisplayLine_CorrectAfterInsert()
    {
        var (doc, model) = FAH.Setup(MultiMethod);
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        model.Fold(regionA.StartLine);

        int visibleBefore = model.VisibleLineCount;

        // Insert a line after A's body (inside the class but after A ends)
        int insertLine   = regionA.EndLine + 1;
        int insertOffset = doc.PositionToOffset(insertLine, 0);
        doc.Insert(insertOffset, "    // separator\n");

        // Visible count should increase by 1 (inserted line is outside folded region)
        model.VisibleLineCount.Should().Be(visibleBefore + 1);
    }

    [Fact]
    public void DeleteLine_ThenToDocumentLine_RoundTrip()
    {
        var (doc, model) = FAH.Setup(MultiMethod);
        var regionA = model.Regions.First(r => r.Label.Contains("void A"));
        model.Fold(regionA.StartLine);

        // Delete a visible line before A (e.g. "class C" line at 0)
        doc.Delete(0, doc.GetLine(0).Length + 1);

        int visCount = model.VisibleLineCount;
        bool allOk = true;
        for (int d = 0; d < visCount; d++)
        {
            int docLine  = model.ToDocumentLine(d);
            int backDisp = docLine >= 0 ? model.ToDisplayLine(docLine) : -1;
            if (backDisp != d) { allOk = false; break; }
        }
        allOk.Should().BeTrue("display↔doc round-trip should be consistent after delete");
    }
}
