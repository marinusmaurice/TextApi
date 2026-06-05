using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

namespace TextAPI.OperationsDemo.Scenarios;

public static class RollbackScenario
{
    public static PipelineResult Run()
    {
        Console.WriteLine("\n[RollbackScenario] Demonstrating atomic rollback on pipeline failure...");

        string originalText = """
            Employee Record

            Name: Alice Johnson
            Department: Engineering
            Salary: 75000
            Manager: Bob Smith
            """;

        var doc = new TextDocument();
        doc.Load(originalText);

        // Show what partial application would have looked like (without rollback)
        var partialDoc = new TextDocument();
        partialDoc.Load(originalText);
        new ReplaceAllOperation { Find = "Bob Smith", Replace = "Carol White" }.Execute(partialDoc);
        Console.WriteLine("\n  Without rollback, partial apply would yield:");
        foreach (var line in partialDoc.GetText().Split('\n'))
            Console.WriteLine($"    {line}");

        // Now run the full pipeline — op 2 will fail and trigger rollback
        var result = new DocumentPipeline(doc)
            .Add(new ReplaceAllOperation
            {
                Find = "Bob Smith",
                Replace = "Carol White"
            })
            .Add(new InsertAfterOperation
            {
                Anchor = "THIS ANCHOR DOES NOT EXIST",
                Text = "inserted text"
            })
            .Add(new ReplaceAllOperation
            {
                Find = "75000",
                Replace = "80000"
            })
            .Execute();

        Console.WriteLine("\n  Pipeline result after rollback:");
        Console.WriteLine($"  WasRolledBack        : {result.WasRolledBack}");
        Console.WriteLine($"  FailedOperationIndex : {result.FailedOperationIndex}");
        Console.WriteLine($"  Error                : {result.ErrorMessage}");
        Console.WriteLine($"  Doc restored to original: {doc.GetText() == originalText}");

        return result;
    }
}
