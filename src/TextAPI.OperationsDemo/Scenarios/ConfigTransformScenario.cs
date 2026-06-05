using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

namespace TextAPI.OperationsDemo.Scenarios;

public static class ConfigTransformScenario
{
    public static PipelineResult Run()
    {
        Console.WriteLine("\n[ConfigTransformScenario] Transforming config from dev to production settings...");

        var doc = new TextDocument();
        doc.Load("""
            # Application Configuration
            # Generated 2024-01-15

            [database]
            host=localhost
            port=5432
            name=myapp_db
            pool_size=10

            [cache]
            enabled=true
            ttl=300
            max_items=1000

            [logging]
            level=INFO
            file=app.log
            """);

        var result = new DocumentPipeline(doc)
            .Add(new ReplaceAllOperation
            {
                Find = "localhost",
                Replace = "db.production.internal"
            })
            .Add(new InsertAfterLineMatchOperation
            {
                Pattern = "pool_size",
                LineText = "timeout=30"
            })
            .Add(new ReplaceAllOperation
            {
                Find = "level=INFO",
                Replace = "level=WARN"
            })
            .Add(new InsertAfterLineMatchOperation
            {
                Pattern = "[logging]",
                LineText = "format=json"
            })
            .Add(new ReplaceAllOperation
            {
                Find = "# Generated 2024-01-15",
                Replace = "# Generated 2025-01-01"
            })
            .Execute();

        return result;
    }
}
