using TextAPI.Operations.Pipeline;
using TextAPI.OperationsDemo.Scenarios;

static void PrintResult(string scenarioName, PipelineResult result)
{
    Console.WriteLine($"\n{"".PadLeft(60, '=')}");
    Console.WriteLine($"  {scenarioName}");
    Console.WriteLine($"{"".PadLeft(60, '=')}");
    Console.WriteLine($"  Status : {(result.Success ? "SUCCESS" : "FAILED")}");
    if (!result.Success)
        Console.WriteLine($"  Error  : {result.ErrorMessage} (op #{result.FailedOperationIndex})");
    Console.WriteLine($"  Audit  : {result.AuditLog.Summary}");
    Console.WriteLine($"  Before : {result.TextBefore.Length} chars, {result.TextBefore.Split('\n').Length} lines");
    Console.WriteLine($"  After  : {result.TextAfter.Length} chars, {result.TextAfter.Split('\n').Length} lines");
    Console.WriteLine();
    Console.WriteLine("  --- Result text ---");
    foreach (var line in result.TextAfter.Split('\n').Take(30))
        Console.WriteLine($"  {line}");
    if (result.TextAfter.Split('\n').Length > 30)
        Console.WriteLine($"  ... ({result.TextAfter.Split('\n').Length - 30} more lines)");
}

Console.WriteLine("TextAPI.Operations — Feature Demo");
Console.WriteLine("==================================");

int total = 0;
int passed = 0;
int failed = 0;

// Scenario 1: CV Editor
var cvResult = CvEditorScenario.Run();
PrintResult("Scenario 1: CV Editor (AI-driven CV enhancement)", cvResult);
total++;
if (cvResult.Success) passed++; else failed++;

// Scenario 2: Contract Redlining
var contractResult = ContractRedliningScenario.Run();
PrintResult("Scenario 2: Contract Redlining (AI-generated legal edits)", contractResult);
total++;
if (contractResult.Success) passed++; else failed++;

// Scenario 3: Batch Processing
var batchResults = BatchProcessingScenario.Run();
Console.WriteLine($"\n{"".PadLeft(60, '=')}");
Console.WriteLine($"  Scenario 3: Batch Processing (same pipeline, 3 documents)");
Console.WriteLine($"{"".PadLeft(60, '=')}");
for (int i = 0; i < batchResults.Count; i++)
{
    var br = batchResults[i];
    Console.WriteLine($"\n  Document {i + 1}:");
    Console.WriteLine($"  Status : {(br.Success ? "SUCCESS" : "FAILED")}");
    Console.WriteLine($"  Audit  : {br.AuditLog.Summary}");
    Console.WriteLine($"  After  : {br.TextAfter.Length} chars");
    Console.WriteLine("  --- Result text ---");
    foreach (var line in br.TextAfter.Split('\n'))
        Console.WriteLine($"  {line}");
    total++;
    if (br.Success) passed++; else failed++;
}

// Scenario 4: Config Transform
var configResult = ConfigTransformScenario.Run();
PrintResult("Scenario 4: Config Transform (dev -> production)", configResult);
total++;
if (configResult.Success) passed++; else failed++;

// Scenario 5: JSON Pipeline
var jsonResult = JsonPipelineScenario.Run();
PrintResult("Scenario 5: JSON Pipeline (AI -> JSON -> engine)", jsonResult);
total++;
if (jsonResult.Success) passed++; else failed++;

// Scenario 6: Rollback
var rollbackResult = RollbackScenario.Run();
PrintResult("Scenario 6: Atomic Rollback (intentional failure)", rollbackResult);
total++;
// For rollback scenario, "success" means the pipeline correctly rolled back (i.e., WasRolledBack == true)
bool rollbackDemoPassed = rollbackResult.WasRolledBack && rollbackResult.FailedOperationIndex == 1;
if (rollbackDemoPassed) passed++; else failed++;

// Final summary
Console.WriteLine($"\n{"".PadLeft(60, '=')}");
Console.WriteLine($"  FINAL SUMMARY");
Console.WriteLine($"{"".PadLeft(60, '=')}");
Console.WriteLine($"  Total scenarios : {total}");
Console.WriteLine($"  Passed          : {passed}");
Console.WriteLine($"  Failed          : {failed}");
Console.WriteLine($"{"".PadLeft(60, '=')}");
