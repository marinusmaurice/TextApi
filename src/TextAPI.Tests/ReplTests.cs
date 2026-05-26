using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using TextAPI.Repl;
using Xunit;

namespace TextAPI.Tests;

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
// 1. Basic execution
// ═══════════════════════════════════════════════════════════════════════════

public class ReplBasicTests
{
    [Fact] public async Task Empty_Code_Succeeds()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("");
        r.Success.Should().BeTrue();
    }

    [Fact] public async Task Expression_ReturnsValue()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("1 + 1");
        r.Success.Should().BeTrue();
        r.ReturnValue.Should().Be("2");
    }

    [Fact] public async Task StringExpression_ReturnsValue()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("\"hello\"");
        r.ReturnValue.Should().Be("hello");
    }

    [Fact] public async Task VoidStatement_HasNoReturnValue()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("var x = 42;");
        r.Success.Should().BeTrue();
        r.ReturnValue.Should().BeNull();
    }

    [Fact] public async Task Print_CapturesOutput()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("Print(\"hello\");");
        r.Success.Should().BeTrue();
        r.Output.Should().Contain("hello");
    }

    [Fact] public async Task Print_Lowercase_Works()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("print(42);");
        r.Output.Should().Contain("42");
    }

    [Fact] public async Task CompileError_ReportsIsCompileError()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("this is not valid C#!!! @@@");
        r.Success.Should().BeFalse();
        r.IsCompileError.Should().BeTrue();
        r.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact] public async Task RuntimeException_CapturedAsError()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("throw new InvalidOperationException(\"boom\");");
        r.Success.Should().BeFalse();
        r.IsCompileError.Should().BeFalse();
        r.ErrorMessage.Should().Contain("boom");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Document access
// ═══════════════════════════════════════════════════════════════════════════

public class ReplDocumentTests
{
    [Fact] public async Task Doc_IsAccessible()
    {
        var (_, _, host) = RH.Make("hello world");
        var r = await host.ExecuteAsync("doc.Length");
        r.ReturnValue.Should().Be("11");
    }

    [Fact] public async Task Doc_Insert_MutatesDocument()
    {
        var (doc, _, host) = RH.Make("hello");
        await host.ExecuteAsync("doc.Insert(5, \" world\");");
        doc.GetText().Should().Be("hello world");
    }

    [Fact] public async Task Doc_Delete_MutatesDocument()
    {
        var (doc, _, host) = RH.Make("hello world");
        await host.ExecuteAsync("doc.Delete(5, 6);");
        doc.GetText().Should().Be("hello");
    }

    [Fact] public async Task Doc_ReplaceAll_ReturnsCount()
    {
        var (doc, _, host) = RH.Make("foo foo foo");
        var r = await host.ExecuteAsync("doc.ReplaceAll(\"foo\", \"bar\")");
        r.ReturnValue.Should().Be("3");
        doc.GetText().Should().Be("bar bar bar");
    }

    [Fact] public async Task Doc_GetText_Expression()
    {
        var (_, _, host) = RH.Make("hello");
        var r = await host.ExecuteAsync("doc.GetText()");
        r.ReturnValue.Should().Be("hello");
    }

    [Fact] public async Task Doc_LineCount_Expression()
    {
        var (_, _, host) = RH.Make("a\nb\nc");
        var r = await host.ExecuteAsync("doc.LineCount");
        r.ReturnValue.Should().Be("3");
    }

    [Fact] public async Task Doc_GetLine_Expression()
    {
        var (_, _, host) = RH.Make("hello\nworld");
        var r = await host.ExecuteAsync("doc.GetLine(1)");
        r.ReturnValue.Should().Be("world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. MultiCursor access
// ═══════════════════════════════════════════════════════════════════════════

public class ReplCursorTests
{
    [Fact] public async Task Mc_IsAccessible()
    {
        var (_, _, host) = RH.Make("hello");
        var r = await host.ExecuteAsync("mc.Count");
        r.ReturnValue.Should().Be("1");
    }

    [Fact] public async Task Mc_AddCursor_Works()
    {
        var (_, mc, host) = RH.Make("hello world");
        await host.ExecuteAsync("mc.AddCursor(6);");
        mc.Count.Should().Be(2);
    }

    [Fact] public async Task Mc_InsertText_Works()
    {
        var (doc, _, host) = RH.Make("hello");
        await host.ExecuteAsync("mc.SetSingle(5); mc.InsertText(\" world\");");
        doc.GetText().Should().Be("hello world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. State persistence across submissions
// ═══════════════════════════════════════════════════════════════════════════

public class ReplStateTests
{
    [Fact] public async Task Variable_PersistsAcrossSubmissions()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("var x = 42;");
        var r = await host.ExecuteAsync("x");
        r.ReturnValue.Should().Be("42");
    }

    [Fact] public async Task MultipleVars_AllPersist()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("var a = 1; var b = 2;");
        var r = await host.ExecuteAsync("a + b");
        r.ReturnValue.Should().Be("3");
    }

    [Fact] public async Task Using_DeclarationPersists()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("using System.Text;");
        // StringBuilder should now be available without qualification
        var r = await host.ExecuteAsync("new StringBuilder(\"hi\").Append(\"!\").ToString()");
        r.ReturnValue.Should().Be("hi!");
    }

    [Fact] public async Task Reset_ClearsState()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("var x = 99;");
        host.Reset();
        var r = await host.ExecuteAsync("x");   // x is no longer defined
        r.Success.Should().BeFalse();
        r.IsCompileError.Should().BeTrue();
    }

    [Fact] public async Task HasState_FalseBeforeFirstExecution()
    {
        var (_, _, host) = RH.Make();
        host.HasState.Should().BeFalse();
    }

    [Fact] public async Task HasState_TrueAfterExecution()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("1");
        host.HasState.Should().BeTrue();
    }

    [Fact] public async Task Reset_ClearsHasState()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("1");
        host.Reset();
        host.HasState.Should().BeFalse();
    }

    [Fact] public async Task DocMutations_PersistAcrossSubmissions()
    {
        var (doc, _, host) = RH.Make("hello");
        await host.ExecuteAsync("doc.Insert(5, \" world\");");
        var r = await host.ExecuteAsync("doc.GetText()");
        r.ReturnValue.Should().Be("hello world");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. Pre-imported namespaces
// ═══════════════════════════════════════════════════════════════════════════

public class ReplImportsTests
{
    [Fact] public async Task Linq_Available()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("Enumerable.Range(1, 3).Sum()");
        r.ReturnValue.Should().Be("6");
    }

    [Fact] public async Task Regex_Available()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("Regex.IsMatch(\"hello123\", @\"\\d+\")");
        r.ReturnValue.Should().Be("True");
    }

    [Fact] public async Task SearchOptions_Available()
    {
        var (_, _, host) = RH.Make("hello HELLO");
        var r = await host.ExecuteAsync(
            "doc.FindAll(\"hello\", new SearchOptions { CaseSensitive = false }).Count()");
        r.ReturnValue.Should().Be("2");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. DisplayText helper
// ═══════════════════════════════════════════════════════════════════════════

public class ReplDisplayTextTests
{
    [Fact] public async Task DisplayText_ShowsReturnValue()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("42");
        r.DisplayText.Should().Be("42");
    }

    [Fact] public async Task DisplayText_CombinesOutputAndReturn()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("Print(\"line1\"); 42");
        r.DisplayText.Should().Contain("line1").And.Contain("42");
    }

    [Fact] public async Task DisplayText_OnError_ShowsErrorMessage()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("throw new Exception(\"oops\");");
        r.DisplayText.Should().Contain("oops");
    }

    [Fact] public async Task Collection_ReturnValue_IsFormatted()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("new[] { 1, 2, 3 }");
        r.ReturnValue.Should().Be("[1, 2, 3]");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Output isolation between submissions
// ═══════════════════════════════════════════════════════════════════════════

public class ReplOutputIsolationTests
{
    [Fact] public async Task OutputIsolated_PerSubmission()
    {
        var (_, _, host) = RH.Make();
        await host.ExecuteAsync("Print(\"first\");");
        var r = await host.ExecuteAsync("Print(\"second\");");
        r.Output.Should().Contain("second").And.NotContain("first");
    }

    [Fact] public async Task NoOutput_EmptyString()
    {
        var (_, _, host) = RH.Make();
        var r = await host.ExecuteAsync("var x = 5;");
        r.Output.Should().BeEmpty();
    }
}
