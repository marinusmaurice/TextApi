using FluentAssertions;
using TextEditor.Core;
using TextEditor.Core.ReadOnly;
using Xunit;

namespace TextEditor.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Item 19 — Read-only regions
// ═══════════════════════════════════════════════════════════════════════════

file static class ROHelper
{
    public static (TextDocument doc, ReadOnlyRegionModel model) Make(string text = "")
    {
        var doc = new TextDocument();
        if (!string.IsNullOrEmpty(text)) doc.Load(text);
        return (doc, doc.GetReadOnlyModel());
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 1. Protect / Unprotect / IsReadOnly
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyProtectionTests
{
    [Fact]
    public void Protect_ReturnsNewGuid()
    {
        var (_, m) = ROHelper.Make("hello world");
        var id1 = m.Protect(0, 5);
        var id2 = m.Protect(6, 11);
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void IsReadOnly_InsideRegion_True()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        m.IsReadOnly(0).Should().BeTrue();
        m.IsReadOnly(2).Should().BeTrue();
        m.IsReadOnly(4).Should().BeTrue();
    }

    [Fact]
    public void IsReadOnly_AtEnd_False()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        m.IsReadOnly(5).Should().BeFalse(); // end is exclusive
    }

    [Fact]
    public void IsReadOnly_OutsideRegion_False()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        m.IsReadOnly(6).Should().BeFalse();
        m.IsReadOnly(10).Should().BeFalse();
    }

    [Fact]
    public void Unprotect_RemovesRegion()
    {
        var (_, m) = ROHelper.Make("hello world");
        var id = m.Protect(0, 5);
        m.IsReadOnly(2).Should().BeTrue();
        m.Unprotect(id).Should().BeTrue();
        m.IsReadOnly(2).Should().BeFalse();
    }

    [Fact]
    public void Unprotect_UnknownId_ReturnsFalse()
    {
        var (_, m) = ROHelper.Make("hello");
        m.Unprotect(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void UnprotectAll_ClearsAllRegions()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        m.Protect(6, 11);
        m.IsReadOnly(2).Should().BeTrue();
        m.UnprotectAll();
        m.IsReadOnly(2).Should().BeFalse();
        m.IsReadOnly(8).Should().BeFalse();
    }

    [Fact]
    public void IsRangeReadOnly_OverlapsRegion_True()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        m.IsRangeReadOnly(0, 5).Should().BeTrue(); // [0,5) overlaps [3,8)
        m.IsRangeReadOnly(5, 3).Should().BeTrue(); // [5,8) overlaps [3,8)
        m.IsRangeReadOnly(3, 5).Should().BeTrue(); // exact match
    }

    [Fact]
    public void IsRangeReadOnly_NoOverlap_False()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        m.IsRangeReadOnly(0, 3).Should().BeFalse(); // [0,3) ends at 3, doesn't touch
        m.IsRangeReadOnly(8, 3).Should().BeFalse(); // [8,11) starts at 8, doesn't touch
    }

    [Fact]
    public void GetRegions_ReturnsAllProtected()
    {
        var (_, m) = ROHelper.Make("hello world");
        var id1 = m.Protect(0, 5);
        var id2 = m.Protect(6, 11);
        var regions = m.GetRegions();
        regions.Should().HaveCount(2);
        regions.Should().Contain(r => r.Id == id1 && r.Start == 0 && r.End == 5);
        regions.Should().Contain(r => r.Id == id2 && r.Start == 6 && r.End == 11);
    }

    [Fact]
    public void OverlappingRegions_BothProtected()
    {
        var (_, m) = ROHelper.Make("hello world");
        m.Protect(0, 7);
        m.Protect(4, 11);
        // Both regions active; offset 5 is in both
        m.IsReadOnly(5).Should().BeTrue();
        m.IsRangeReadOnly(0, 11).Should().BeTrue();
    }

    [Fact]
    public void Protect_InvalidArgs_Throws()
    {
        var (_, m) = ROHelper.Make("hello");
        m.Invoking(x => x.Protect(-1, 3)).Should().Throw<ArgumentOutOfRangeException>();
        m.Invoking(x => x.Protect(3, 2)).Should().Throw<ArgumentOutOfRangeException>();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 2. Enforce mode — inserts blocked
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyEnforceInsertTests
{
    [Fact]
    public void InsertInsideRegion_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5); // protect "hello"
        doc.Invoking(d => d.Insert(2, "X"))
           .Should().Throw<ReadOnlyViolationException>()
           .Which.EditOffset.Should().Be(2);
    }

    [Fact]
    public void InsertAtRegionStart_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        // Insert at offset 0 (exactly at start): goes before protected region
        doc.Insert(0, ">>>");
        doc.GetText(0, doc.Length).Should().StartWith(">>>hello");
    }

    [Fact]
    public void InsertAtRegionEnd_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        // Insert at offset 5 (exactly at end): goes after protected region
        doc.Insert(5, "<<<");
        doc.GetText(0, doc.Length).Should().Be("hello<<<"  + " world");
    }

    [Fact]
    public void InsertBeforeRegion_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(6, 11);
        doc.Insert(0, ">>>");
        doc.GetText(0, doc.Length).Should().StartWith(">>>hello");
    }

    [Fact]
    public void InsertAfterRegion_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.Insert(11, "!");
        doc.GetText(0, doc.Length).Should().Be("hello world!");
    }

    [Fact]
    public void ViolationException_CarriesRegionInfo()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        var ex = Assert.Throws<ReadOnlyViolationException>(() => doc.Insert(5, "X"));
        ex.RegionStart.Should().Be(3);
        ex.RegionEnd.Should().Be(8);
        ex.EditOffset.Should().Be(5);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 3. Enforce mode — deletes blocked
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyEnforceDeleteTests
{
    [Fact]
    public void DeleteInsideRegion_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.Invoking(d => d.Delete(1, 3))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void DeleteSpanningRegionStart_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        // Delete [1,5) spans the region start at 3
        doc.Invoking(d => d.Delete(1, 4))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void DeleteSpanningRegionEnd_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        // Delete [5,10) spans the region end at 8
        doc.Invoking(d => d.Delete(5, 5))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void DeleteEntirelyContainingRegion_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(3, 8);
        doc.Invoking(d => d.Delete(0, 11))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void DeleteBeforeRegion_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(5, 11);
        doc.Delete(0, 3);
        doc.GetText(0, doc.Length).Should().Be("lo world");
    }

    [Fact]
    public void DeleteAfterRegion_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.Delete(5, 6); // deletes " world"
        doc.GetText(0, doc.Length).Should().Be("hello");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 4. Enforce mode — Replace blocked
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyEnforceReplaceTests
{
    [Fact]
    public void ReplaceOverlappingRegion_Throws()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.Invoking(d => d.Replace(2, 3, "XYZ"))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void ReplaceBeforeRegion_Allowed()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(6, 11);
        doc.Replace(0, 5, "greet");
        doc.GetText(0, doc.Length).Should().Be("greet world");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 5. Silent mode (EnforceReadOnly = false)
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlySilentTests
{
    [Fact]
    public void SilentInsertInside_NoChange()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.EnforceReadOnly = false;
        doc.Insert(2, "X");
        doc.GetText(0, doc.Length).Should().Be("hello world"); // unchanged
    }

    [Fact]
    public void SilentDeleteOverlap_NoChange()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.EnforceReadOnly = false;
        doc.Delete(1, 3);
        doc.GetText(0, doc.Length).Should().Be("hello world"); // unchanged
    }

    [Fact]
    public void SilentAllowedEdits_StillWork()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.EnforceReadOnly = false;
        doc.Insert(5, "!!!"); // at boundary → allowed
        doc.GetText(0, doc.Length).Should().Be("hello!!! world");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 6. Offset remapping after allowed edits
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyRemapTests
{
    [Fact]
    public void InsertBeforeRegion_ShiftsRegion()
    {
        // "hello world": protect "world" at [6,11)
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(6, 11);

        // Insert ">>> " before, adding 4 chars at offset 0
        doc.Insert(0, ">>> ");
        // Region should shift to [10, 15)
        m.IsReadOnly(10).Should().BeTrue();
        m.IsReadOnly(14).Should().BeTrue();
        m.IsReadOnly(15).Should().BeFalse();
        m.IsReadOnly(9).Should().BeFalse();

        // Verify the insert inside the now-shifted region is still blocked
        doc.Invoking(d => d.Insert(12, "X"))
           .Should().Throw<ReadOnlyViolationException>();
    }

    [Fact]
    public void InsertAtRegionStart_ShiftsRegion()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(6, 11);
        // Insert at exactly the start (offset 6) → goes before region, region shifts
        doc.Insert(6, "--");
        // Region should now be [8, 13)
        m.IsReadOnly(8).Should().BeTrue();
        m.IsReadOnly(12).Should().BeTrue();
        m.IsReadOnly(13).Should().BeFalse();
    }

    [Fact]
    public void InsertAfterRegion_NoShift()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(0, 5);
        doc.Insert(6, "xyz");
        // Region stays at [0, 5)
        m.IsReadOnly(0).Should().BeTrue();
        m.IsReadOnly(4).Should().BeTrue();
        m.IsReadOnly(5).Should().BeFalse();
    }

    [Fact]
    public void DeleteBeforeRegion_ShiftsRegion()
    {
        var (doc, m) = ROHelper.Make("hello world");
        m.Protect(6, 11);
        // Delete "hello " (6 chars at 0)
        doc.Delete(0, 6);
        // Region should shift to [0, 5)
        m.IsReadOnly(0).Should().BeTrue();
        m.IsReadOnly(4).Should().BeTrue();
        m.IsReadOnly(5).Should().BeFalse();
    }

    [Fact]
    public void MultipleRegions_AllShift()
    {
        var (doc, m) = ROHelper.Make("aaa bbb ccc");
        //                            0123456789 10
        m.Protect(0, 3);   // "aaa"
        m.Protect(4, 7);   // "bbb"
        m.Protect(8, 11);  // "ccc"

        // Insert "XX" at offset 0 (before all regions)
        doc.Insert(0, "XX");
        // All regions shift right by 2
        m.IsReadOnly(2).Should().BeTrue();  // "aaa" now at [2,5)
        m.IsReadOnly(6).Should().BeTrue();  // "bbb" now at [6,9)
        m.IsReadOnly(10).Should().BeTrue(); // "ccc" now at [10,13)
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 7. Load clears all protections
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyLoadTests
{
    [Fact]
    public void Load_ClearsProtections()
    {
        var (doc, m) = ROHelper.Make("hello");
        m.Protect(0, 5);
        m.IsReadOnly(2).Should().BeTrue();

        doc.Load("new content");
        m.IsReadOnly(2).Should().BeFalse();
        m.GetRegions().Should().BeEmpty();
    }

    [Fact]
    public void AfterLoad_EditsWork()
    {
        var (doc, m) = ROHelper.Make("hello");
        m.Protect(0, 5);
        doc.Load("world");
        doc.Insert(2, "X"); // now allowed since Load cleared regions
        doc.GetText(0, doc.Length).Should().Be("woXrld");
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 8. RegionsChanged event
// ─────────────────────────────────────────────────────────────────────────

public class ReadOnlyEventsTests
{
    [Fact]
    public void Protect_FiresRegionsChanged()
    {
        var (_, m) = ROHelper.Make("hello");
        int fired = 0;
        m.RegionsChanged += (_, _) => fired++;
        m.Protect(0, 3);
        fired.Should().Be(1);
    }

    [Fact]
    public void Unprotect_FiresRegionsChanged()
    {
        var (_, m) = ROHelper.Make("hello");
        int fired = 0;
        var id = m.Protect(0, 3);
        m.RegionsChanged += (_, _) => fired++;
        m.Unprotect(id);
        fired.Should().Be(1);
    }

    [Fact]
    public void UnprotectAll_FiresRegionsChanged()
    {
        var (_, m) = ROHelper.Make("hello");
        m.Protect(0, 3);
        int fired = 0;
        m.RegionsChanged += (_, _) => fired++;
        m.UnprotectAll();
        fired.Should().Be(1);
    }

    [Fact]
    public void UnprotectAll_EmptyModel_NoEvent()
    {
        var (_, m) = ROHelper.Make("hello");
        int fired = 0;
        m.RegionsChanged += (_, _) => fired++;
        m.UnprotectAll(); // nothing to clear
        fired.Should().Be(0);
    }
}
