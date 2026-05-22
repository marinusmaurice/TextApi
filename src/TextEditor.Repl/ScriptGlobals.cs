using TextEditor.Core;
using TextEditor.Core.Cursor;

namespace TextEditor.Repl;

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
}
