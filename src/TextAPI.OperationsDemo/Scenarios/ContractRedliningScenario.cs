using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

namespace TextAPI.OperationsDemo.Scenarios;

public static class ContractRedliningScenario
{
    public static PipelineResult Run()
    {
        Console.WriteLine("\n[ContractRedliningScenario] Simulating AI-generated contract redlining...");

        var doc = new TextDocument();
        doc.Load("""
            SERVICE AGREEMENT

            1. PARTIES
            This agreement is between Vendor Inc. and Client Corp.

            2. SERVICES
            Vendor will provide software development services.

            3. TERMINATION
            Either party may terminate this agreement with 14 days written notice.

            4. PAYMENT
            Payment is due within 30 days of invoice.

            5. INDEMNIFICATION
            Each party indemnifies the other against negligence claims.
            """);

        var result = new DocumentPipeline(doc)
            .Add(new ReplaceAllOperation
            {
                Find = "14 days",
                Replace = "60 days"
            })
            .Add(new ReplaceAllOperation
            {
                Find = "30 days of invoice",
                Replace = "45 days of invoice"
            })
            .Add(new InsertAfterOperation
            {
                Anchor = "Either party may terminate this agreement with 60 days written notice.",
                Text = "\nTermination for cause requires 5 days notice with written documentation.\nAll outstanding work will be completed within the notice period."
            })
            .Add(new AppendOperation
            {
                Text = "\n\n6. GOVERNING LAW\nThis agreement is governed by the laws of England and Wales.\n"
            })
            .Execute();

        return result;
    }
}
