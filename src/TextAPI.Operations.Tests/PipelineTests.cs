using FluentAssertions;
using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using Xunit;

namespace TextAPI.Operations.Tests;

file static class Helpers
{
    public static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        d.Load(text);
        return d;
    }

    public static PipelineResult Run(TextDocument doc, params IDocumentOperation[] ops)
    {
        var p = new DocumentPipeline(doc);
        foreach (var op in ops) p.Add(op);
        return p.Execute();
    }
}

public class PipelineTests
{
    // ── Offset drift correction ───────────────────────────────────────────────

    [Fact]
    public void Pipeline_TwoInsertAtOps_SecondOffsetAdjustsForFirstInsertion()
    {
        var doc = Helpers.Doc("Hello World");

        // Insert "AAA" at offset 0: "AAAHello World"
        // Second op is at original offset 0; drift = +3, so adjusted to offset 3
        // Inserting "BBB" at offset 3 gives: "AAABBBHello World"
        var result = Helpers.Run(doc,
            new InsertAtOperation(0, "AAA"),
            new InsertAtOperation(0, "BBB"));

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("AAABBBHello World");
    }

    [Fact]
    public void Pipeline_InsertThenDelete_DeleteOffsetAdjustsForInsertion()
    {
        var doc = Helpers.Doc("Hello World");

        // Insert "XYZ" at 5: "HelloXYZ World" (delta = +3)
        // Delete op originally at offset 5, drift = +3 → adjusted to offset 8
        // "HelloXYZ World"[8] = ' ', delete 1 char → "HelloXYZWorld"
        var result = Helpers.Run(doc,
            new InsertAtOperation(5, "XYZ"),
            new DeleteAtOperation(5, 1));

        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("HelloXYZWorld");
    }

    // ── Atomic rollback ───────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_SecondOpFails_DocumentIsUnchanged()
    {
        var doc = Helpers.Doc("Hello World");
        var original = doc.GetText();

        var result = Helpers.Run(doc,
            new InsertAtOperation(0, "PREFIX "),
            new InsertAfterOperation { Anchor = "[MISSING]", Text = "X" },
            new AppendOperation { Text = " SUFFIX" });

        result.Success.Should().BeFalse();
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void Pipeline_FailedOp_WasRolledBackIsTrue()
    {
        var doc = Helpers.Doc("content");

        var result = Helpers.Run(doc,
            new InsertAfterOperation { Anchor = "[DOES_NOT_EXIST]", Text = "x" });

        result.WasRolledBack.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_SecondOpFails_FailedOperationIndexIsOne()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " !" },
            new InsertAfterOperation { Anchor = "[NOPE]", Text = "x" },
            new PrependOperation { Text = "y" });

        result.FailedOperationIndex.Should().Be(1);
    }

    [Fact]
    public void Pipeline_AllSucceeded_FailedOperationIndexIsMinusOne()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " World" });

        result.FailedOperationIndex.Should().Be(-1);
    }

    [Fact]
    public void Pipeline_ThirdOpFails_DocRestoredToOriginal()
    {
        var doc = Helpers.Doc("data");
        var original = doc.GetText();

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " appended" },
            new PrependOperation { Text = "prepended " },
            new InsertAfterOperation { Anchor = "[NOT_THERE]", Text = "x" });

        result.Success.Should().BeFalse();
        doc.GetText().Should().Be(original);
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_SuccessfulRun_AuditLogEntriesCountEqualsOpsCount()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " A" },
            new AppendOperation { Text = " B" },
            new AppendOperation { Text = " C" });

        result.AuditLog.Entries.Should().HaveCount(3);
    }

    [Fact]
    public void Pipeline_AllSucceeded_AuditLogAllSucceededIsTrue()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " World" },
            new PrependOperation { Text = "Say: " });

        result.AuditLog.AllSucceeded.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_PartialFailure_AuditLogAllSucceededIsFalse()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " World" },
            new InsertAfterOperation { Anchor = "[NOPE]", Text = "x" });

        result.AuditLog.AllSucceeded.Should().BeFalse();
    }

    [Fact]
    public void Pipeline_SuccessfulInsert_AuditLogTotalCharsInsertedIsCorrect()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " World" },   // 6 chars
            new PrependOperation { Text = "Say: " });  // 5 chars

        result.AuditLog.TotalCharsInserted.Should().Be(11);
    }

    [Fact]
    public void Pipeline_Summary_IsNonEmptyString()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc, new AppendOperation { Text = " World" });

        result.AuditLog.Summary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Pipeline_AuditTimestamps_AreUtc()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc, new AppendOperation { Text = " World" });

        foreach (var entry in result.AuditLog.Entries)
            entry.TimestampUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    // ── TextBefore / TextAfter ────────────────────────────────────────────────

    [Fact]
    public void Pipeline_TextBefore_EqualsOriginalDocText()
    {
        var doc = Helpers.Doc("original content");

        var result = Helpers.Run(doc, new AppendOperation { Text = " extra" });

        result.TextBefore.Should().Be("original content");
    }

    [Fact]
    public void Pipeline_TextAfter_EqualsModifiedTextOnSuccess()
    {
        var doc = Helpers.Doc("Hello");

        var result = Helpers.Run(doc, new AppendOperation { Text = " World" });

        result.TextAfter.Should().Be("Hello World");
    }

    [Fact]
    public void Pipeline_TextAfter_EqualsTextBeforeOnRollback()
    {
        var doc = Helpers.Doc("original");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = " modified" },
            new InsertAfterOperation { Anchor = "[NOPE]", Text = "x" });

        result.TextAfter.Should().Be(result.TextBefore);
    }

    // ── Cancellation ─────────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_CancellationTokenNone_Succeeds()
    {
        var doc = Helpers.Doc("Hello");
        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(new AppendOperation { Text = " World" });

        var result = pipeline.Execute(CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_AlreadyCancelledToken_RollsBackImmediately()
    {
        var doc = Helpers.Doc("Hello");
        var original = doc.GetText();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(new AppendOperation { Text = " World" });
        var result = pipeline.Execute(cts.Token);

        result.Success.Should().BeFalse();
        doc.GetText().Should().Be(original);
    }

    // ── Multi-op success ──────────────────────────────────────────────────────

    [Fact]
    public void Pipeline_FiveOpsAllSucceed_AllResultsInOperationResults()
    {
        var doc = Helpers.Doc("line1\nline2\nline3");

        var result = Helpers.Run(doc,
            new AppendOperation { Text = "\nline4" },
            new AppendOperation { Text = "\nline5" },
            new PrependOperation { Text = "line0\n" },
            new ReplaceAllOperation { Find = "line1", Replace = "LINE1" },
            new ReplaceAllOperation { Find = "line5", Replace = "LINE5" });

        result.Success.Should().BeTrue();
        result.OperationResults.Should().HaveCount(5);
        result.OperationResults.Should().AllSatisfy(r => r.Success.Should().BeTrue());
    }
}
