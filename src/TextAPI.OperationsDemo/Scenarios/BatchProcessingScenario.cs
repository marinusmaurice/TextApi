using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

namespace TextAPI.OperationsDemo.Scenarios;

public static class BatchProcessingScenario
{
    public static List<PipelineResult> Run()
    {
        Console.WriteLine("\n[BatchProcessingScenario] Applying the same pipeline to 3 different documents...");

        var documents = new List<(string Label, string Content)>
        {
            (
                "Config file v1.0",
                """
                app.name=MyApplication
                app.version=1.0
                db.host=localhost
                db.port=5432
                feature.new_dashboard=false
                """
            ),
            (
                "AuthService config v1.0",
                """
                service.name=AuthService
                service.version=1.0
                cache.enabled=true
                feature.new_dashboard=false
                """
            ),
            (
                "Deployment manifest",
                """
                name: my-app
                version: 1.0
                replicas: 2
                image: myapp:1.0
                """
            )
        };

        var results = new List<PipelineResult>();

        foreach (var (label, content) in documents)
        {
            Console.WriteLine($"\n  Processing: {label}");

            var doc = new TextDocument();
            doc.Load(content);

            var result = new DocumentPipeline(doc)
                .Add(new ReplaceAllOperation { Find = "1.0", Replace = "2.0" })
                .Add(new TrimTrailingWhitespaceOperation())
                .Add(new ReplaceAllOperation { Find = "false", Replace = "true" })
                .Execute();

            Console.WriteLine($"  Status  : {(result.Success ? "SUCCESS" : "FAILED")}");
            Console.WriteLine($"  Audit   : {result.AuditLog.Summary}");

            results.Add(result);
        }

        return results;
    }
}
