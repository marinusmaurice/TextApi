using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using TextAPI.Repl;
using Xunit;

namespace TextAPI.Operations.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class RH
{
    public static (TextDocument doc, MultiCursor mc, CSharpScriptHost host) Make(string text = "")
    {
        var doc = new TextDocument();
        if (!string.IsNullOrEmpty(text)) doc.Load(text);
        var mc   = new MultiCursor(doc);
        var host = new CSharpScriptHost(doc, mc);
        return (doc, mc, host);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Tests
// ═══════════════════════════════════════════════════════════════════════════

public class ReplOperationsTests
{
    [Fact]
    public async Task NewPipeline_ReplaceAll_ChangesDocument()
    {
        var (doc, mc, host) = RH.Make("hello world hello");
        var code = """var r = NewPipeline().Add(new ReplaceAllOperation { Find = "hello", Replace = "hi" }).Execute();""";
        var result = await host.ExecuteAsync(code);
        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("hi world hi");
        var r2 = await host.ExecuteAsync("r.AuditLog.Summary");
        r2.Success.Should().BeTrue();
        r2.ReturnValue.Should().Contain("succeeded");
    }

    [Fact]
    public async Task NewPipeline_Insert_AppendsText()
    {
        var (doc, mc, host) = RH.Make("start");
        var code = """NewPipeline().Add(new AppendOperation { Text = " end" }).Execute();""";
        var result = await host.ExecuteAsync(code);
        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("start end");
    }

    [Fact]
    public async Task NewPipeline_MultipleOps_AllApplied()
    {
        var (doc, mc, host) = RH.Make("  hello world  ");
        var code = """
NewPipeline()
    .Add(new TrimTrailingWhitespaceOperation())
    .Add(new ReplaceAllOperation { Find = "hello", Replace = "hi" })
    .Execute();
""";
        var result = await host.ExecuteAsync(code);
        result.Success.Should().BeTrue();
        doc.GetText().Should().Be("  hi world");
    }

    [Fact]
    public async Task NewPipeline_FailingOp_RollsBackDocument()
    {
        var (doc, mc, host) = RH.Make("foo bar");
        var code = """
var result = NewPipeline()
    .Add(new ReplaceAllOperation { Find = "foo", Replace = "baz" })
    .Add(new InsertAfterOperation { Anchor = "MISSING", Text = "x" })
    .Execute();
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue(); // Script execution succeeded; the pipeline result has the failure
        var r2 = await host.ExecuteAsync("result.Success");
        r2.ReturnValue.Should().Be("False");
        doc.GetText().Should().Be("foo bar");
    }

    [Fact]
    public async Task RunJson_ValidPipeline_ExecutesOps()
    {
        var (doc, mc, host) = RH.Make("TODO fix this\nTODO add tests");
        var code = """
var result = RunJson("{\"operations\":[{\"type\":\"REPLACE_ALL\",\"find\":\"TODO\",\"replace\":\"DONE\"}]}");
Print(result.Success.ToString());
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.Output.Should().Contain("True");
        doc.GetText().Should().NotContain("TODO");
        doc.GetText().Should().Contain("DONE");
    }

    [Fact]
    public async Task RunJson_ArrayFormat_Works()
    {
        var (doc, mc, host) = RH.Make("alpha beta");
        var code = "RunJson(\"[{\\\"type\\\":\\\"REPLACE_ALL\\\",\\\"find\\\":\\\"alpha\\\",\\\"replace\\\":\\\"OMEGA\\\"}]\")";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("OMEGA beta");
    }

    [Fact]
    public async Task RunJson_InvalidAnchor_RollsBack()
    {
        var (doc, mc, host) = RH.Make("some text");
        var code = """var result = RunJson("{\"operations\":[{\"type\":\"INSERT_AFTER\",\"anchor\":\"NOTHERE\",\"text\":\"x\"}]}");""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        var r2 = await host.ExecuteAsync("result.WasRolledBack");
        r2.ReturnValue.Should().Be("True");
        doc.GetText().Should().Be("some text");
    }

    [Fact]
    public async Task ParseOps_ReturnsOperationList()
    {
        var (doc, mc, host) = RH.Make();
        var code = """
var ops = ParseOps("[{\"type\":\"REPLACE_ALL\",\"find\":\"x\",\"replace\":\"y\"}]");
Print(ops.Count.ToString());
Print(ops[0].Type);
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.Output.Should().Contain("1");
        r.Output.Should().Contain("REPLACE_ALL");
    }

    [Fact]
    public async Task ParseOps_ThenManualExecute_Works()
    {
        var (doc, mc, host) = RH.Make("x x x");
        var code = """
var ops = ParseOps("[{\"type\":\"REPLACE_ALL\",\"find\":\"x\",\"replace\":\"y\"}]");
var p = NewPipeline();
foreach (var op in ops) p.Add(op);
p.Execute();
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("y y y");
    }

    [Fact]
    public async Task ReplSession_PipelineResultInspection_Works()
    {
        var (doc, mc, host) = RH.Make("line one\nline two\nline three");
        var code1 = """var r = NewPipeline().Add(new ReplaceAllOperation { Find = "line", Replace = "row" }).Execute();""";
        var r1 = await host.ExecuteAsync(code1);
        r1.Success.Should().BeTrue();

        var code2 = """
Print(r.AuditLog.TotalCharsInserted.ToString());
Print(r.TextBefore.Substring(0, 8));
""";
        var r2 = await host.ExecuteAsync(code2);
        r2.Success.Should().BeTrue();
        r2.Output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task OperationTypes_DirectlyUsable_InScript()
    {
        var (doc, mc, host) = RH.Make();
        var code = """var op = new InsertAtOperation(0, "hello"); op.Type""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.ReturnValue.Should().Be("INSERT_AT");
    }

    [Fact]
    public async Task OperationTypes_AllCategories_Accessible()
    {
        var (doc, mc, host) = RH.Make();
        var code = """
var ops = new IDocumentOperation[] {
    new InsertAtOperation(0, "a"),
    new ReplaceAllOperation { Find = "a", Replace = "b" },
    new AppendOperation { Text = "!" },
    new TrimTrailingWhitespaceOperation(),
    new FindAllOperation { Pattern = "b" }
};
Print(ops.Length.ToString());
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.Output.Should().Contain("5");
    }

    [Fact]
    public async Task DocumentPipeline_DirectConstruction_Works()
    {
        var (doc, mc, host) = RH.Make("test");
        var code = """var p = new DocumentPipeline(doc); p.Add(new AppendOperation { Text = "!" }); var r = p.Execute(); r.Success""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.ReturnValue.Should().Be("True");
        doc.GetText().Should().Be("test!");
    }

    [Fact]
    public async Task AuditLog_AccessibleFromRepl_AfterRun()
    {
        var (doc, mc, host) = RH.Make("a b c a b c");
        var code = """
var r = NewPipeline().Add(new ReplaceAllOperation { Find = "a", Replace = "X" }).Execute();
Print(r.AuditLog.Entries.Count.ToString());
Print(r.AuditLog.AllSucceeded.ToString());
""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        r.Output.Should().Contain("1");
        r.Output.Should().Contain("True");
    }

    [Fact]
    public async Task RegexReplace_ViaRepl_Works()
    {
        var (doc, mc, host) = RH.Make("Phone: 123-456-7890");
        var code = """NewPipeline().Add(new RegexReplaceOperation { Pattern = @"\d{3}-\d{3}-\d{4}", Replacement = "[REDACTED]" }).Execute();""";
        var r = await host.ExecuteAsync(code);
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("Phone: [REDACTED]");
    }
}
