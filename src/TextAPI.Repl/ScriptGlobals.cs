using TextAPI.Core;
using TextAPI.Core.Cursor;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using TextAPI.Operations.Serialization;

namespace TextAPI.Repl;

/// <summary>
/// The host-side globals object injected into every script execution.
///
/// In a script the user writes:
///   doc.Insert(0, "hello");
///   var n = doc.LineCount;
///   Print(n);
///   Console.WriteLine(doc.GetText());
///
/// Both Print() and Console.Write* are captured and returned in
/// <see cref="ScriptExecutionResult.Output"/>.
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>The active document. Directly accessible as <c>doc</c> in scripts.</summary>
    public TextDocument doc { get; }

    /// <summary>The multi-cursor for the document. Accessible as <c>mc</c> in scripts.</summary>
    public MultiCursor mc { get; }

    internal StringWriter Writer = new();

    internal ScriptGlobals(TextDocument document, MultiCursor cursor)
    {
        doc = document;
        mc  = cursor;
    }

    /// <summary>Write a value to the REPL output panel.</summary>
    public void Print(object? value = null) => Writer.WriteLine(value?.ToString() ?? "");

    /// <summary>Alias for <see cref="Print"/> — matches Python/F# convention.</summary>
    public void print(object? value = null) => Print(value);

    // ── TextAPI.Operations helpers ────────────────────────────────────────

    /// <summary>
    /// Create a fresh <see cref="DocumentPipeline"/> bound to <c>doc</c>.
    /// Chain <c>.Add(...).Execute()</c> to run it.
    /// <code>
    /// var result = NewPipeline()
    ///     .Add(new ReplaceAllOperation { Find = "TODO", Replace = "DONE" })
    ///     .Execute();
    /// Print(result.AuditLog.Summary);
    /// </code>
    /// </summary>
    public DocumentPipeline NewPipeline() => new DocumentPipeline(doc);

    /// <summary>
    /// Parse and execute a JSON operation pipeline in one call.
    /// Accepts both a bare array <c>[{...},{...}]</c> and a pipeline object
    /// <c>{"operations":[...]}</c>.
    /// Returns a <see cref="PipelineResult"/> — check <c>.Success</c> and
    /// <c>.AuditLog.Summary</c>.
    /// <code>
    /// var result = RunJson("""
    ///   { "operations": [
    ///       { "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
    ///       { "type": "TRIM_TRAILING_WHITESPACE" }
    ///   ]}
    /// """);
    /// Print(result.AuditLog.Summary);
    /// </code>
    /// </summary>
    public PipelineResult RunJson(string json)
    {
        var ops      = json.TrimStart().StartsWith('[')
            ? OperationMapper.FromJson(json)
            : OperationMapper.FromPipelineJson(json);
        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(ops);
        return pipeline.Execute();
    }

    /// <summary>
    /// Deserialise a JSON array or pipeline object into a list of
    /// <see cref="IDocumentOperation"/> objects without executing them.
    /// Useful for inspecting or modifying operations before running.
    /// </summary>
    public IReadOnlyList<IDocumentOperation> ParseOps(string json)
        => json.TrimStart().StartsWith('[')
            ? OperationMapper.FromJson(json)
            : OperationMapper.FromPipelineJson(json);
}
