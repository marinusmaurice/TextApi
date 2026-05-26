using TextAPI.Core;
using TextAPI.Core.ChangeTracking;
using Xunit;
using FluentAssertions;

namespace TextAPI.Tests;

/// <summary>
/// Tests for ChangeTracker — per-line change status (Added / Modified / Clean)
/// and deletion-above markers, mirroring the gutter change bar in VS Code.
/// </summary>
public sealed class ChangeTrackingTests
{
    private static TextDocument Doc(string content)
    {
        var d = new TextDocument();
        d.Load(content);
        return d;
    }

    // ── Baseline: fresh load → everything Clean ───────────────────────────

    [Fact]
    public void FreshLoad_AllLinesClean()
    {
        var doc     = Doc("line0\nline1\nline2");
        var tracker = doc.GetChangeTracker();

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void FreshLoad_NoDeletions()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        tracker.DeletionPoints().Should().BeEmpty();
    }

    [Fact]
    public void FreshLoad_HasAnyChanges_IsFalse()
    {
        var doc     = Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();

        tracker.HasAnyChanges.Should().BeFalse();
    }

    // ── Modified: edit existing line ──────────────────────────────────────

    [Fact]
    public void EditExistingLine_MarksModified()
    {
        var doc     = Doc("hello\nworld\nfoo");
        var tracker = doc.GetChangeTracker();

        // Replace "world" with "WORLD" on line 1 (offset 6, length 5)
        doc.Replace(6, 5, "WORLD");

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void EditFirstLine_MarksModified()
    {
        var doc     = Doc("abc\ndef");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 3, "XYZ");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void EditLastLine_MarksModified()
    {
        var doc     = Doc("abc\ndef\nghi");
        var tracker = doc.GetChangeTracker();

        doc.Replace(8, 3, "ZZZ");

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void MultipleLineEdits_EachMarkedModified()
    {
        var doc     = Doc("a\nb\nc\nd");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 1, "A");   // line 0
        doc.Replace(4, 1, "C");   // line 2

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
        tracker.GetStatus(3).Should().Be(LineStatus.Clean);
    }

    // ── Added: insert new lines ───────────────────────────────────────────

    [Fact]
    public void InsertNewLine_MarksAdded()
    {
        var doc     = Doc("line0\nline1");
        var tracker = doc.GetChangeTracker();

        // Insert a new line between line0 and line1
        doc.Insert(6, "new\n");

        doc.LineCount.Should().Be(3);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Added);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void InsertMultipleNewLines_AllMarkedAdded()
    {
        var doc     = Doc("top\nbottom");
        var tracker = doc.GetChangeTracker();

        doc.Insert(4, "a\nb\nc\n");

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Added);
        tracker.GetStatus(2).Should().Be(LineStatus.Added);
        tracker.GetStatus(3).Should().Be(LineStatus.Added);
        tracker.GetStatus(4).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void AppendNewLineAtEnd_MarksAdded()
    {
        var doc     = Doc("only");
        var tracker = doc.GetChangeTracker();

        doc.Insert(doc.Length, "\nnewline");

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Added);
    }

    // ── Deleted: remove existing lines ────────────────────────────────────

    [Fact]
    public void DeleteExistingLine_HasDeletionAbove()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        // Delete line 1 ("b\n") — offset 2, length 2
        doc.Delete(2, 2);

        doc.LineCount.Should().Be(2);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.HasDeletionAbove(1).Should().BeTrue();
    }

    [Fact]
    public void DeleteFirstLine_HasDeletionAboveZero()
    {
        var doc     = Doc("first\nsecond\nthird");
        var tracker = doc.GetChangeTracker();

        doc.Delete(0, 6); // delete "first\n"

        tracker.HasDeletionAbove(0).Should().BeTrue();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean); // "second"
        tracker.GetStatus(1).Should().Be(LineStatus.Clean); // "third"
    }

    [Fact]
    public void DeleteLastLine_HasDeletionAboveAtEnd()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        // Delete "\nc" (newline + last line)
        doc.Delete(3, 2);

        doc.LineCount.Should().Be(2);
        tracker.HasDeletionAbove(2).Should().BeTrue();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void DeleteMiddleLines_HasDeletionAboveAtCorrectPosition()
    {
        var doc     = Doc("a\nb\nc\nd\ne");
        var tracker = doc.GetChangeTracker();

        // Delete lines 1 and 2 ("b\nc\n") = offset 2, length 4
        doc.Delete(2, 4);

        doc.LineCount.Should().Be(3);
        tracker.HasDeletionAbove(1).Should().BeTrue();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean); // a
        tracker.GetStatus(1).Should().Be(LineStatus.Clean); // d
        tracker.GetStatus(2).Should().Be(LineStatus.Clean); // e
    }

    // ── Mixed edits: replace lines (delete+insert same position) ──────────

    [Fact]
    public void ReplaceLineWithMoreLines_CorrectMixOfModifiedAndAdded()
    {
        var doc     = Doc("before\nmiddle\nafter");
        var tracker = doc.GetChangeTracker();

        // Replace "middle" with "new1\nnew2" — same start line, now 2 lines
        doc.Replace(7, 6, "new1\nnew2");

        doc.LineCount.Should().Be(4);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);   // before
        tracker.GetStatus(1).Should().Be(LineStatus.Modified); // new1  (replaced middle)
        tracker.GetStatus(2).Should().Be(LineStatus.Added);    // new2  (extra)
        tracker.GetStatus(3).Should().Be(LineStatus.Clean);   // after
    }

    [Fact]
    public void ReplaceMultipleLinesWithFewer_ModifiedAndDeletion()
    {
        var doc     = Doc("a\nb\nc\nd");
        var tracker = doc.GetChangeTracker();

        // Replace lines 1+2 ("b\nc\n") with single "X" — offset 2, length 4
        doc.Replace(2, 4, "X\n");

        doc.LineCount.Should().Be(3);
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);   // a
        tracker.GetStatus(1).Should().Be(LineStatus.Modified); // X  (replaces b)
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);   // d
        // c was deleted → deletion marker above line 2
        tracker.HasDeletionAbove(2).Should().BeTrue();
    }

    // ── ChangedLines enumeration ──────────────────────────────────────────

    [Fact]
    public void ChangedLines_ReturnsOnlyNonClean()
    {
        var doc     = Doc("a\nb\nc\nd");
        var tracker = doc.GetChangeTracker();

        doc.Replace(2, 1, "B");  // modify line 1
        doc.Insert(6, "\nnew");  // add line 3

        var changed = tracker.ChangedLines().ToList();
        changed.Should().Contain(1);  // Modified
        changed.Should().Contain(3);  // Added
        changed.Should().NotContain(0);
        changed.Should().NotContain(2);
    }

    [Fact]
    public void ChangedLines_EmptyWhenClean()
    {
        var doc     = Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();

        tracker.ChangedLines().Should().BeEmpty();
    }

    // ── Undo restores Clean status ────────────────────────────────────────

    [Fact]
    public void UndoEdit_RestoredToClean()
    {
        var doc     = Doc("hello\nworld");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 5, "HELLO");
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);

        doc.Undo();
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
    }

    [Fact]
    public void UndoInsert_RestoredToClean()
    {
        var doc     = Doc("a\nb");
        var tracker = doc.GetChangeTracker();

        doc.Insert(2, "mid\n");
        tracker.GetStatus(1).Should().Be(LineStatus.Added);

        doc.Undo();
        doc.LineCount.Should().Be(2);
        tracker.ChangedLines().Should().BeEmpty();
    }

    [Fact]
    public void UndoDelete_RestoredToClean()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        doc.Delete(2, 2); // delete "b\n"
        tracker.HasDeletionAbove(1).Should().BeTrue();

        doc.Undo();
        doc.LineCount.Should().Be(3);
        tracker.HasDeletionAbove(1).Should().BeFalse();
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    // ── Save resets baseline → all Clean ─────────────────────────────────

    [Fact]
    public async Task Save_ResetsAllToClean()
    {
        var doc     = Doc("line0\nline1\nline2");
        var tracker = doc.GetChangeTracker();

        doc.Replace(6, 5, "MODIFIED");
        doc.Insert(0, "new\n");
        tracker.HasAnyChanges.Should().BeTrue();

        await using var stream = new System.IO.MemoryStream();
        await doc.SaveAsync(stream);

        tracker.HasAnyChanges.Should().BeFalse();
        tracker.ChangedLines().Should().BeEmpty();
        tracker.DeletionPoints().Should().BeEmpty();
    }

    [Fact]
    public async Task SaveThenEdit_ShowsChangesRelativeToSavedVersion()
    {
        var doc     = Doc("original");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 8, "modified");
        await using var stream = new System.IO.MemoryStream();
        await doc.SaveAsync(stream);

        // Baseline is now "modified"
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);

        doc.Replace(0, 8, "changed again");
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
    }

    // ── SetBaseline explicitly ────────────────────────────────────────────

    [Fact]
    public void ExplicitSetBaseline_ClearsAllMarks()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        doc.Replace(2, 1, "B");
        doc.Insert(0, "new\n");
        tracker.HasAnyChanges.Should().BeTrue();

        tracker.SetBaseline();

        tracker.HasAnyChanges.Should().BeFalse();
        tracker.ChangedLines().Should().BeEmpty();
    }

    [Fact]
    public void SetBaseline_ThenEdit_ShowsNewChanges()
    {
        var doc     = Doc("foo\nbar");
        var tracker = doc.GetChangeTracker();

        doc.Replace(4, 3, "BAR");
        tracker.SetBaseline(); // "foo\nBAR" is now baseline

        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);

        doc.Replace(0, 3, "FOO"); // modify line 0 relative to new baseline
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
    }

    // ── HasAnyChanges ─────────────────────────────────────────────────────

    [Fact]
    public void HasAnyChanges_TrueAfterModify()
    {
        var doc = Doc("unchanged");
        doc.GetChangeTracker().HasAnyChanges.Should().BeFalse();

        doc.Replace(0, 9, "changed");
        doc.GetChangeTracker().HasAnyChanges.Should().BeTrue();
    }

    [Fact]
    public void HasAnyChanges_TrueAfterDelete()
    {
        var doc     = Doc("a\nb");
        var tracker = doc.GetChangeTracker();

        doc.Delete(1, 2); // delete "\nb"
        tracker.HasAnyChanges.Should().BeTrue();
    }

    // ── DeletionPoints enumeration ────────────────────────────────────────

    [Fact]
    public void DeletionPoints_ReturnsAllDeletedPositions()
    {
        var doc     = Doc("a\nb\nc\nd\ne");
        var tracker = doc.GetChangeTracker();

        doc.Delete(2, 2); // delete "b\n"
        doc.Delete(4, 2); // delete "d\n" (now at offset 4)

        var pts = tracker.DeletionPoints().ToList();
        pts.Should().HaveCount(2);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void SingleLine_NoNewline_EditMarksModified()
    {
        var doc     = Doc("hello");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 5, "world");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void EmptyDocument_NoChanges()
    {
        var doc     = Doc(string.Empty);
        var tracker = doc.GetChangeTracker();

        tracker.HasAnyChanges.Should().BeFalse();
        tracker.ChangedLines().Should().BeEmpty();
    }

    [Fact]
    public void OutOfRangeLineIndex_ReturnsClean()
    {
        var doc     = Doc("one line");
        var tracker = doc.GetChangeTracker();

        tracker.GetStatus(999).Should().Be(LineStatus.Clean);
        tracker.HasDeletionAbove(999).Should().BeFalse();
    }

    [Fact]
    public void ChangesUpdated_FiredOnEdit()
    {
        var doc     = Doc("hello");
        var tracker = doc.GetChangeTracker();
        int fired   = 0;
        tracker.ChangesUpdated += (_, _) => fired++;

        doc.Replace(0, 5, "world");

        fired.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ChangesUpdated_FiredOnSetBaseline()
    {
        var doc     = Doc("hello");
        var tracker = doc.GetChangeTracker();
        int fired   = 0;
        tracker.ChangesUpdated += (_, _) => fired++;

        tracker.SetBaseline();

        fired.Should().Be(1);
    }

    [Fact]
    public void ReplaceAll_MarksAllChangedLinesModified()
    {
        var doc     = Doc("foo\nfoo\nfoo");
        var tracker = doc.GetChangeTracker();

        doc.ReplaceAll("foo", "bar");

        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
        tracker.GetStatus(2).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void MultipleUndoRedo_TrackerFollows()
    {
        var doc     = Doc("alpha\nbeta");
        var tracker = doc.GetChangeTracker();

        doc.Replace(6, 4, "BETA"); // modify line 1
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);

        doc.Undo();
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);

        doc.Redo();
        tracker.GetStatus(1).Should().Be(LineStatus.Modified);
    }

    [Fact]
    public void Load_ReplacesBaseline()
    {
        var doc     = new TextDocument();
        doc.Load("version1\nline2");
        var tracker = doc.GetChangeTracker();

        doc.Replace(0, 8, "CHANGED");
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);

        // Load again → new baseline
        doc.Load("brand\nnew\ncontent");
        tracker.GetStatus(0).Should().Be(LineStatus.Clean);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean);
        tracker.GetStatus(2).Should().Be(LineStatus.Clean);
        tracker.HasAnyChanges.Should().BeFalse();
    }

    [Fact]
    public void InsertAtBeginning_ShiftsExistingLinesDown()
    {
        var doc     = Doc("existing");
        var tracker = doc.GetChangeTracker();

        doc.Insert(0, "new\n");

        tracker.GetStatus(0).Should().Be(LineStatus.Added);
        tracker.GetStatus(1).Should().Be(LineStatus.Clean); // "existing" shifted down, unchanged
    }

    [Fact]
    public void DeleteAllLines_AllDeletionsReported()
    {
        var doc     = Doc("a\nb\nc");
        var tracker = doc.GetChangeTracker();

        doc.Delete(0, doc.Length);

        // After deleting all content, the document has 1 empty line.
        // The empty line replaces baseline line "a" → Modified.
        // Baseline lines "b" and "c" are deleted → deletion marker above line 1.
        doc.LineCount.Should().Be(1);
        tracker.GetStatus(0).Should().Be(LineStatus.Modified);
        tracker.HasDeletionAbove(1).Should().BeTrue();
    }
}
