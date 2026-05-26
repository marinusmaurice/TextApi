using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Navigation;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Item 22 — Cursor position history (Back / Forward)
// ═══════════════════════════════════════════════════════════════════════════

// ─────────────────────────────────────────────────────────────────────────
// 1. Initial state
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryInitialTests
{
    [Fact] public void Empty_CountIsZero()           => new CursorHistory().Count.Should().Be(0);
    [Fact] public void Empty_CurrentIsNull()         => new CursorHistory().Current.Should().BeNull();
    [Fact] public void Empty_CanGoBack_False()       => new CursorHistory().CanGoBack.Should().BeFalse();
    [Fact] public void Empty_CanGoForward_False()    => new CursorHistory().CanGoForward.Should().BeFalse();
    [Fact] public void Empty_Back_ReturnsNull()      => new CursorHistory().Back().Should().BeNull();
    [Fact] public void Empty_Forward_ReturnsNull()   => new CursorHistory().Forward().Should().BeNull();
    [Fact] public void DefaultCapacity_Is100()       => new CursorHistory().Capacity.Should().Be(100);
    [Fact] public void CustomCapacity_Stored()       => new CursorHistory(42).Capacity.Should().Be(42);

    [Fact]
    public void ZeroCapacity_Throws()
        => new Action(() => new CursorHistory(0))
           .Should().Throw<ArgumentOutOfRangeException>();
}

// ─────────────────────────────────────────────────────────────────────────
// 2. Push
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryPushTests
{
    [Fact]
    public void Push_IncrementsCount()
    {
        var h = new CursorHistory();
        h.Push(10);
        h.Count.Should().Be(1);
    }

    [Fact]
    public void Push_CurrentReflectsLatest()
    {
        var h = new CursorHistory();
        h.Push(10);
        h.Current!.Value.Offset.Should().Be(10);
    }

    [Fact]
    public void PushMultiple_CurrentIsLast()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Current!.Value.Offset.Should().Be(30);
    }

    [Fact]
    public void Push_SameOffsetAsCurrentFilePath_NoOp()
    {
        var h = new CursorHistory();
        h.Push(10, "file.cs");
        h.Push(10, "file.cs"); // duplicate
        h.Count.Should().Be(1);
    }

    [Fact]
    public void Push_SameOffsetDifferentFile_Recorded()
    {
        var h = new CursorHistory();
        h.Push(10, "a.cs");
        h.Push(10, "b.cs"); // same offset but different file
        h.Count.Should().Be(2);
    }

    [Fact]
    public void Push_WithFilePath_Stored()
    {
        var h = new CursorHistory();
        h.Push(50, "Program.cs");
        h.Current!.Value.FilePath.Should().Be("Program.cs");
    }

    [Fact]
    public void Push_TruncatesForwardHistory()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Back(); // current = 20
        h.Push(99); // truncates 30, adds 99
        h.Count.Should().Be(3); // [10, 20, 99]
        h.Current!.Value.Offset.Should().Be(99);
        h.CanGoForward.Should().BeFalse();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 3. Back navigation
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryBackTests
{
    [Fact]
    public void Back_ReturnsOneStepEarlier()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Back()!.Value.Offset.Should().Be(20);
    }

    [Fact]
    public void Back_TwiceReturnsTwo()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Back();
        h.Back()!.Value.Offset.Should().Be(10);
    }

    [Fact]
    public void Back_AtStart_ReturnsNull()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20);
        h.Back();
        h.Back().Should().BeNull(); // already at start
    }

    [Fact]
    public void Back_AtStart_DoesNotChangeState()
    {
        var h = new CursorHistory();
        h.Push(10);
        h.Back(); // no-op
        h.Current!.Value.Offset.Should().Be(10);
        h.Count.Should().Be(1);
    }

    [Fact]
    public void BackEnablesForward()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20);
        h.CanGoForward.Should().BeFalse();
        h.Back();
        h.CanGoForward.Should().BeTrue();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 4. Forward navigation
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryForwardTests
{
    [Fact]
    public void ForwardAfterBack_ReturnsNext()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Back(); h.Back();          // now at 10
        h.Forward()!.Value.Offset.Should().Be(20);
    }

    [Fact]
    public void ForwardTwice_AfterBackTwice()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20); h.Push(30);
        h.Back(); h.Back();
        h.Forward(); h.Forward()!.Value.Offset.Should().Be(30);
    }

    [Fact]
    public void Forward_AtEnd_ReturnsNull()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20);
        h.Forward().Should().BeNull(); // already at end
    }

    [Fact]
    public void Forward_AtEnd_DoesNotChangeState()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20);
        h.Forward(); // no-op
        h.Current!.Value.Offset.Should().Be(20);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 5. Capacity eviction
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryCapacityTests
{
    [Fact]
    public void AtCapacity_OldestEvicted()
    {
        var h = new CursorHistory(capacity: 3);
        h.Push(10); h.Push(20); h.Push(30); // full
        h.Push(40); // evicts 10
        h.Count.Should().Be(3);
        // Navigate all the way back — oldest should be 20.
        h.Back(); h.Back();
        h.Current!.Value.Offset.Should().Be(20);
        h.CanGoBack.Should().BeFalse();
    }

    [Fact]
    public void CapacityOne_OnlyLatestKept()
    {
        var h = new CursorHistory(capacity: 1);
        h.Push(10); h.Push(20); h.Push(30);
        h.Count.Should().Be(1);
        h.Current!.Value.Offset.Should().Be(30);
    }

    [Fact]
    public void CapacityEviction_CurrentAdjustedCorrectly()
    {
        var h = new CursorHistory(capacity: 3);
        h.Push(1); h.Push(2); h.Push(3); // full: [1,2,3] current=2
        h.Back();                          // current=1 (offset 2)
        h.Push(99);                        // truncates 3, adds 99: [1,2,99] current=2
        h.Push(100);                       // full at 3, adds 100, evicts 1: [2,99,100] current=2
        h.Back()!.Value.Offset.Should().Be(99);
        h.Back()!.Value.Offset.Should().Be(2);
        h.CanGoBack.Should().BeFalse();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 6. Clear
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryClearTests
{
    [Fact]
    public void Clear_ResetsAll()
    {
        var h = new CursorHistory();
        h.Push(10); h.Push(20);
        h.Clear();
        h.Count.Should().Be(0);
        h.Current.Should().BeNull();
        h.CanGoBack.Should().BeFalse();
        h.CanGoForward.Should().BeFalse();
    }

    [Fact]
    public void ClearThenPush_Works()
    {
        var h = new CursorHistory();
        h.Push(10); h.Clear();
        h.Push(99);
        h.Count.Should().Be(1);
        h.Current!.Value.Offset.Should().Be(99);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 7. TextDocument integration
// ─────────────────────────────────────────────────────────────────────────

public class CursorHistoryDocumentTests
{
    [Fact]
    public void GetCursorHistory_ReturnsSameInstance()
    {
        var doc = new TextDocument();
        var h1 = doc.GetCursorHistory();
        var h2 = doc.GetCursorHistory();
        h1.Should().BeSameAs(h2);
    }

    [Fact]
    public void Load_ClearsHistory()
    {
        var doc = new TextDocument();
        var h = doc.GetCursorHistory();
        h.Push(100); h.Push(200);
        doc.Load("new content");
        h.Count.Should().Be(0);
    }

    [Fact]
    public void AfterLoad_CanPushAgain()
    {
        var doc = new TextDocument();
        var h = doc.GetCursorHistory();
        h.Push(100);
        doc.Load("new content");
        h.Push(50);
        h.Count.Should().Be(1);
        h.Current!.Value.Offset.Should().Be(50);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// 8. HistoryEntry record
// ─────────────────────────────────────────────────────────────────────────

public class HistoryEntryTests
{
    [Fact]
    public void SameOffsetAndFile_AreEqual()
    {
        var a = new HistoryEntry(10, "file.cs");
        var b = new HistoryEntry(10, "file.cs");
        a.Should().Be(b);
    }

    [Fact]
    public void DifferentOffset_NotEqual()
        => new HistoryEntry(10).Should().NotBe(new HistoryEntry(20));

    [Fact]
    public void NullFilePath_DefaultValue()
        => new HistoryEntry(5).FilePath.Should().BeNull();
}
