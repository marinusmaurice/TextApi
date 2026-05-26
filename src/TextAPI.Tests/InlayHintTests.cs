using TextAPI.Core;
using TextAPI.Core.InlayHints;
using Xunit;

namespace TextAPI.Tests;

public class InlayHintTests
{
    private static TextDocument Doc(string content)
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }

    // ── Basic add/query ───────────────────────────────────────────────────

    [Fact]
    public void AddHint_ReturnsId()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var hint  = new InlayHint(5, "x:");
        var id    = model.AddHint(hint);
        Assert.Equal(hint.Id, id);
    }

    [Fact]
    public void AddHint_AppearsInAllHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var hint  = new InlayHint(3, "name:");
        model.AddHint(hint);
        Assert.Single(model.AllHints);
        Assert.Equal(hint, model.AllHints[0]);
    }

    [Fact]
    public void AddHint_MultipleHints_SortedByOffset()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(8, "c:"));
        model.AddHint(new InlayHint(2, "a:"));
        model.AddHint(new InlayHint(5, "b:"));
        Assert.Equal(3, model.AllHints.Count);
        Assert.Equal(2, model.AllHints[0].Offset);
        Assert.Equal(5, model.AllHints[1].Offset);
        Assert.Equal(8, model.AllHints[2].Offset);
    }

    [Fact]
    public void SetHints_ReplacesAll()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "old:"));
        model.SetHints(new[] { new InlayHint(3, "new:") });
        Assert.Single(model.AllHints);
        Assert.Equal("new:", model.AllHints[0].Text);
    }

    [Fact]
    public void SetHints_SortsOnOffset()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.SetHints(new[]
        {
            new InlayHint(10, "z:"),
            new InlayHint(1,  "a:"),
            new InlayHint(5,  "m:"),
        });
        Assert.Equal(1,  model.AllHints[0].Offset);
        Assert.Equal(5,  model.AllHints[1].Offset);
        Assert.Equal(10, model.AllHints[2].Offset);
    }

    [Fact]
    public void ClearHints_RemovesAll()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:"));
        model.AddHint(new InlayHint(5, "b:"));
        model.ClearHints();
        Assert.Empty(model.AllHints);
    }

    [Fact]
    public void RemoveHint_ById_RemovesCorrectOne()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var h1    = new InlayHint(0, "a:");
        var h2    = new InlayHint(5, "b:");
        model.AddHint(h1);
        model.AddHint(h2);
        bool removed = model.RemoveHint(h1.Id);
        Assert.True(removed);
        Assert.Single(model.AllHints);
        Assert.Equal(h2, model.AllHints[0]);
    }

    [Fact]
    public void RemoveHint_UnknownId_ReturnsFalse()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        bool result = model.RemoveHint(Guid.NewGuid());
        Assert.False(result);
    }

    // ── GetHintsInRange ───────────────────────────────────────────────────

    [Fact]
    public void GetHintsInRange_Empty_ReturnsEmpty()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var result = model.GetHintsInRange(0, 11);
        Assert.Empty(result);
    }

    [Fact]
    public void GetHintsInRange_AllInRange()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:"));
        model.AddHint(new InlayHint(5, "b:"));
        model.AddHint(new InlayHint(9, "c:"));
        var result = model.GetHintsInRange(0, 11);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void GetHintsInRange_PartialOverlap_IncludesOnlyInRange()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:"));
        model.AddHint(new InlayHint(5, "b:"));
        model.AddHint(new InlayHint(9, "c:"));
        var result = model.GetHintsInRange(3, 8);
        Assert.Single(result);
        Assert.Equal(5, result[0].Offset);
    }

    [Fact]
    public void GetHintsInRange_ExclusiveEnd()
    {
        // hint at endOffset is excluded
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "b:"));
        var result = model.GetHintsInRange(0, 5);
        Assert.Empty(result);
    }

    [Fact]
    public void GetHintsInRange_SingleHintAtStart()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:"));
        var result = model.GetHintsInRange(0, 5);
        Assert.Single(result);
        Assert.Equal(0, result[0].Offset);
    }

    // ── GetHintsByKind ────────────────────────────────────────────────────

    [Fact]
    public void GetHintsByKind_ParameterKind()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:", InlayHintKind.Parameter));
        model.AddHint(new InlayHint(5, "b:", InlayHintKind.Type));
        var result = model.GetHintsByKind(InlayHintKind.Parameter);
        Assert.Single(result);
        Assert.Equal(InlayHintKind.Parameter, result[0].Kind);
    }

    [Fact]
    public void GetHintsByKind_TypeKind()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:", InlayHintKind.Parameter));
        model.AddHint(new InlayHint(5, ": int", InlayHintKind.Type));
        model.AddHint(new InlayHint(8, ": string", InlayHintKind.Type));
        var result = model.GetHintsByKind(InlayHintKind.Type);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetHintsByKind_NoMatchingKind_ReturnsEmpty()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "a:", InlayHintKind.Parameter));
        var result = model.GetHintsByKind(InlayHintKind.Return);
        Assert.Empty(result);
    }

    // ── GetHintAt ─────────────────────────────────────────────────────────

    [Fact]
    public void GetHintAt_ExactOffset_ReturnsHint()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var hint  = new InlayHint(5, "x:");
        model.AddHint(hint);
        var result = model.GetHintAt(5);
        Assert.NotNull(result);
        Assert.Equal(hint.Id, result.Id);
    }

    [Fact]
    public void GetHintAt_NoHint_ReturnsNull()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "x:"));
        Assert.Null(model.GetHintAt(4));
    }

    // ── OnInsert: offsets shift ───────────────────────────────────────────

    [Fact]
    public void OnInsert_Before_ShiftsHint()
    {
        // insert before hint → hint offset increases
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "x:"));
        doc.Insert(2, "abc"); // insert 3 chars at offset 2
        Assert.Equal(8, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnInsert_After_NoShift()
    {
        // insert after hint → hint offset unchanged
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(3, "x:"));
        doc.Insert(7, "abcd"); // insert 4 chars after hint
        Assert.Equal(3, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnInsert_AtSameOffset_ShiftsHint()
    {
        // insert exactly at hint offset → hint shifts right
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "x:"));
        doc.Insert(5, "ab"); // insert 2 chars at exact hint offset
        Assert.Equal(7, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnInsert_MultipleHints_OnlySomeShift()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(2, "a:"));
        model.AddHint(new InlayHint(7, "b:"));
        doc.Insert(5, "abc"); // insert 3 chars at offset 5
        Assert.Equal(2, model.AllHints[0].Offset);  // before insertion, unchanged
        Assert.Equal(10, model.AllHints[1].Offset); // after insertion, shifted
    }

    // ── OnDelete: removal and shift ───────────────────────────────────────

    [Fact]
    public void OnDelete_Before_ShiftsHint()
    {
        // delete before hint → offset decreases
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(7, "x:"));
        doc.Delete(2, 3); // delete 3 chars at offset 2
        Assert.Equal(4, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnDelete_After_NoChange()
    {
        // delete after hint → unchanged
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(3, "x:"));
        doc.Delete(6, 3); // delete after the hint
        Assert.Equal(3, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnDelete_CoveringHint_RemovesIt()
    {
        // delete range containing hint → hint removed
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "x:"));
        doc.Delete(3, 5); // delete [3,8) which covers offset 5
        Assert.Empty(model.AllHints);
    }

    [Fact]
    public void OnDelete_PartialBefore_ShiftsHint()
    {
        // delete starting before hint but ending before it → shifts by deletedLength
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(8, "x:"));
        doc.Delete(3, 4); // deletes [3,7), hint at 8 shifts to 4
        Assert.Equal(4, model.AllHints[0].Offset);
    }

    [Fact]
    public void OnDelete_MultipleHints_SomeRemovedSomeShifted()
    {
        var doc   = Doc("hello world foo");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(1,  "a:")); // before delete, no change
        model.AddHint(new InlayHint(5,  "b:")); // inside deleted range, removed
        model.AddHint(new InlayHint(10, "c:")); // after, shifts
        doc.Delete(3, 5); // deletes [3,8)
        Assert.Equal(2, model.AllHints.Count);
        Assert.Equal(1, model.AllHints[0].Offset);  // unchanged
        Assert.Equal(5, model.AllHints[1].Offset);  // 10 - 5 = 5
    }

    // ── Events ────────────────────────────────────────────────────────────

    [Fact]
    public void HintsChanged_FiredOnAdd()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        model.AddHint(new InlayHint(0, "x:"));
        Assert.Equal(1, count);
    }

    [Fact]
    public void HintsChanged_FiredOnRemove()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        var hint  = new InlayHint(0, "x:");
        model.AddHint(hint);
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        model.RemoveHint(hint.Id);
        Assert.Equal(1, count);
    }

    [Fact]
    public void HintsChanged_FiredOnSetHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        model.SetHints(new[] { new InlayHint(0, "x:") });
        Assert.Equal(1, count);
    }

    [Fact]
    public void HintsChanged_FiredOnClear()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "x:"));
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        model.ClearHints();
        Assert.Equal(1, count);
    }

    [Fact]
    public void HintsChanged_FiredOnInsert_WhenHintsExist()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(5, "x:"));
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        doc.Insert(0, "abc"); // triggers OnInsert which shifts hint → fires HintsChanged
        Assert.True(count >= 1);
    }

    [Fact]
    public void HintsChanged_NotFiredOnInsert_WhenNoHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        doc.Insert(0, "abc"); // no hints → OnInsert does nothing → no event
        Assert.Equal(0, count);
    }

    [Fact]
    public void HintsChanged_FiredOnDelete_WhenHintsExist()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(8, "x:"));
        int count = 0;
        model.HintsChanged += (_, _) => count++;
        doc.Delete(0, 3); // shifts hint → fires HintsChanged
        Assert.True(count >= 1);
    }

    // ── Integration with TextDocument ─────────────────────────────────────

    [Fact]
    public void DocInsert_ShiftsHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(6, "x:"));
        doc.Insert(0, "foo ");  // insert 4 chars at start
        Assert.Equal(10, model.AllHints[0].Offset);
    }

    [Fact]
    public void DocDelete_RemovesAndShiftsHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(4,  "a:")); // inside deleted range
        model.AddHint(new InlayHint(9,  "b:")); // after deleted range
        doc.Delete(2, 5); // delete [2,7)
        // hint at 4 is removed; hint at 9 shifts to 4
        Assert.Single(model.AllHints);
        Assert.Equal(4, model.AllHints[0].Offset);
    }

    [Fact]
    public void DocLoad_ClearsHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0, "x:"));
        Assert.Single(model.AllHints);
        doc.Load("new content");
        Assert.Empty(model.AllHints);
    }

    [Fact]
    public void DocUndo_ClearsHints()
    {
        var doc   = Doc("hello world");
        var model = doc.GetInlayHintModel();
        doc.Insert(5, " beautiful");
        model.AddHint(new InlayHint(0, "x:"));
        Assert.Single(model.AllHints);
        doc.Undo();
        Assert.Empty(model.AllHints);
    }

    // ── Kind and properties ───────────────────────────────────────────────

    [Fact]
    public void HintProperties_Preserved()
    {
        // Id, Text, Kind, Tooltip all preserved
        var doc     = Doc("hello world");
        var model   = doc.GetInlayHintModel();
        var hint    = new InlayHint(3, "name:", InlayHintKind.Parameter, "tooltip text");
        model.AddHint(hint);
        var retrieved = model.AllHints[0];
        Assert.Equal(hint.Id,      retrieved.Id);
        Assert.Equal("name:",      retrieved.Text);
        Assert.Equal(InlayHintKind.Parameter, retrieved.Kind);
        Assert.Equal("tooltip text", retrieved.Tooltip);
    }

    [Fact]
    public void AllKinds_CanBeAdded()
    {
        var doc   = Doc("hello world foo bar");
        var model = doc.GetInlayHintModel();
        model.AddHint(new InlayHint(0,  "p:", InlayHintKind.Parameter));
        model.AddHint(new InlayHint(5,  "t:", InlayHintKind.Type));
        model.AddHint(new InlayHint(9,  "r:", InlayHintKind.Return));
        model.AddHint(new InlayHint(13, "o:", InlayHintKind.Other));
        Assert.Equal(4, model.AllHints.Count);
        Assert.Contains(model.AllHints, h => h.Kind == InlayHintKind.Parameter);
        Assert.Contains(model.AllHints, h => h.Kind == InlayHintKind.Type);
        Assert.Contains(model.AllHints, h => h.Kind == InlayHintKind.Return);
        Assert.Contains(model.AllHints, h => h.Kind == InlayHintKind.Other);
    }
}
