using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using TextEditor.Core;
using TextEditor.Core.Cursor;

namespace TextEditor.Repl;

/// <summary>
/// A stateful C# REPL host backed by Roslyn.
///
/// Each <see cref="ExecuteAsync"/> call continues from where the previous one left off —
/// variables and using directives declared in earlier submissions remain in scope.
///
/// Usage:
/// <code>
///   var host = new CSharpScriptHost(doc, mc);
///   var r1 = await host.ExecuteAsync("var n = doc.LineCount;");
///   var r2 = await host.ExecuteAsync("Print(n);");   // n still in scope
/// </code>
///
/// Thread safety: not thread-safe. Submit one request at a time.
/// </summary>
public sealed class CSharpScriptHost
{
    private readonly ScriptGlobals _globals;
    private readonly ScriptOptions _options;
    private ScriptState<object>?   _state;

    public CSharpScriptHost(TextDocument doc, MultiCursor mc)
        : this(new ScriptGlobals(doc, mc)) { }

    // Internal constructor for testing
    internal CSharpScriptHost(ScriptGlobals globals)
    {
        _globals = globals;
        _options = BuildOptions();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Compile and execute <paramref name="code"/> in the current session context.
    /// All previous submissions' variables and types remain in scope.
    /// </summary>
    public async Task<ScriptExecutionResult> ExecuteAsync(
        string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new ScriptExecutionResult(true, null, string.Empty, null, false);

        // Fresh output buffer for this submission.
        // Use Print() / print() in scripts to write to this buffer.
        // Console.Write* goes to the process stdout as normal — the UI panel
        // should surface the Print() output, not try to intercept Console.
        _globals.Writer = new StringWriter();

        try
        {
            if (_state == null)
            {
                _state = await CSharpScript.RunAsync<object>(
                    code, _options, _globals, typeof(ScriptGlobals), cancellationToken);
            }
            else
            {
                _state = await _state.ContinueWithAsync<object>(
                    code, _options, cancellationToken);
            }

            string output    = _globals.Writer.ToString();
            string? retValue = _state.ReturnValue is null ? null
                : FormatReturnValue(_state.ReturnValue);

            return new ScriptExecutionResult(true, retValue, output, null, false);
        }
        catch (CompilationErrorException ex)
        {
            string output = _globals.Writer.ToString();
            string msg    = string.Join("\n", ex.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            return new ScriptExecutionResult(false, null, output, msg, isCompileError: true);
        }
        catch (Exception ex)
        {
            string output = _globals.Writer.ToString();
            return new ScriptExecutionResult(false, null, output, ex.Message, isCompileError: false);
        }
    }

    /// <summary>
    /// Reset session state — clears all variables from previous submissions.
    /// The document and cursor references are unchanged.
    /// </summary>
    public void Reset() => _state = null;

    /// <summary>True when at least one submission has been executed in this session.</summary>
    public bool HasState => _state != null;

    // ── Script options ────────────────────────────────────────────────────

    private static ScriptOptions BuildOptions()
    {
        // Collect assemblies the script needs to reference:
        //   TextEditor.Core  — the editor API
        //   System.Linq etc. — standard library conveniences
        var assemblies = new[]
        {
            typeof(TextDocument).Assembly,                       // TextEditor.Core
            typeof(object).Assembly,                              // System.Private.CoreLib
            typeof(Enumerable).Assembly,                          // System.Linq
            typeof(System.Text.StringBuilder).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly,
            typeof(Console).Assembly,                             // System.Console
        };

        return ScriptOptions.Default
            .WithReferences(assemblies)
            .WithImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Text",
                "System.Text.RegularExpressions",
                "TextEditor.Core",
                "TextEditor.Core.Cursor",
                "TextEditor.Core.Search",
                "TextEditor.Core.Decorations")
            .WithOptimizationLevel(OptimizationLevel.Debug)
            .WithAllowUnsafe(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string FormatReturnValue(object value)
    {
        // For collections, show a compact summary rather than the type name
        if (value is System.Collections.IEnumerable and not string)
        {
            var items = ((System.Collections.IEnumerable)value)
                .Cast<object>()
                .Take(51)
                .ToList();
            bool truncated = items.Count > 50;
            if (truncated) items = items.Take(50).ToList();
            string body = string.Join(", ", items.Select(x => x?.ToString() ?? "null"));
            return truncated ? $"[{body}, ...]" : $"[{body}]";
        }

        return value.ToString() ?? string.Empty;
    }
}
