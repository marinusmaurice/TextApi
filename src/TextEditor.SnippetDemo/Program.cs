using TextEditor.Core;
using TextEditor.Core.Snippets;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var doc = new TextDocument();
doc.Load("");

// -- Scenario 1: Simple for-loop snippet ---------------------------------
Console.WriteLine("══════════════════════════════════════════════");
Console.WriteLine(" Scenario 1: Expand a for-loop snippet");
Console.WriteLine("══════════════════════════════════════════════\n");

var forSnippet = SnippetEngine.Parse("for (int ${1:i} = 0; ${1:i} < ${2:count}; ${1:i}++)\n{\n    $0\n}");
Console.WriteLine($"  Tab stops: [{string.Join(", ", forSnippet.TabStopIndices)}]");
Console.WriteLine($"  Has exit stop ($0): {forSnippet.HasExitStop}\n");

var session1 = SnippetEngine.BeginSnippet(doc, forSnippet, 0);
Console.WriteLine("  After expansion:");
Console.WriteLine("  " + doc.GetText().Replace("\n", "\n  "));

// Navigate tab stops
var stop1 = session1.NextTabStop()!;
Console.WriteLine($"\n  -> Tab stop $1: offset={stop1.Offset}, length={stop1.Length}, text=\"{doc.GetText(stop1.Offset, stop1.Length)}\"");

// Simulate user typing "index" for $1 (updates all 3 mirrors)
session1.UpdateTabStop(1, "index");
Console.WriteLine($"\n  After typing 'index' for $1 (mirrors updated):");
Console.WriteLine("  " + doc.GetText().Replace("\n", "\n  "));

var stop2 = session1.NextTabStop()!;
Console.WriteLine($"\n  -> Tab stop $2: offset={stop2.Offset}, length={stop2.Length}, text=\"{doc.GetText(stop2.Offset, stop2.Length)}\"");
session1.UpdateTabStop(2, "items.Length");

var stop0 = session1.NextTabStop()!;
Console.WriteLine($"\n  -> Tab stop $0 (exit): offset={stop0.Offset}");
session1.Commit();
Console.WriteLine("\n  Final document:");
Console.WriteLine("  " + doc.GetText().Replace("\n", "\n  "));

// -- Scenario 2: Snippet with variable substitution ----------------------
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 2: Variable substitution");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc2    = new TextDocument();
doc2.Load("", "MyFile.cs");
var varSnip = SnippetEngine.Parse("// File: $TM_FILENAME\n// Author: ${1:Your Name}\n$0");
var session2 = SnippetEngine.BeginSnippet(doc2, varSnip, 0, filename: "MyFile.cs");
Console.WriteLine("  Expanded with $TM_FILENAME = 'MyFile.cs':");
Console.WriteLine("  " + doc2.GetText().Replace("\n", "\n  "));

// -- Scenario 3: Cancel removes inserted text ----------------------------
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 3: Cancel restores document");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc3 = new TextDocument();
doc3.Load("before ");
int insertAt = doc3.Length;
var snap = SnippetEngine.Parse("${1:PLACEHOLDER} after");
var session3 = SnippetEngine.BeginSnippet(doc3, snap, insertAt);
Console.WriteLine($"  After expansion: \"{doc3.GetText()}\"");
session3.Cancel();
Console.WriteLine($"  After cancel:    \"{doc3.GetText()}\"");

// -- Scenario 4: Tab-stop ordering ---------------------------------------
Console.WriteLine("\n══════════════════════════════════════════════");
Console.WriteLine(" Scenario 4: Tab-stop navigation order");
Console.WriteLine("══════════════════════════════════════════════\n");

var doc4   = new TextDocument();
doc4.Load("");
var method = SnippetEngine.Parse("${3:ReturnType} ${1:MethodName}(${2:params})\n{\n    $0\n}");
Console.WriteLine($"  Snippet: ${{3:ReturnType}} ${{1:MethodName}}(${{2:params}})");
Console.WriteLine($"  Navigation order: {string.Join(" -> ", method.TabStopIndices)} -> $0");
var s4 = SnippetEngine.BeginSnippet(doc4, method, 0);
int step = 0;
TabStop? ts;
while ((ts = s4.NextTabStop()) != null)
{
    step++;
    Console.WriteLine($"  Step {step}: ${{{ts.Index}}} at offset {ts.Offset}, text=\"{doc4.GetText(ts.Offset, ts.Length)}\"");
}
s4.Commit();
