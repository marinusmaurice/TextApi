using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using TextAPI.Operations.Serialization;

namespace TextAPI.OperationsDemo.Scenarios;

public static class JsonPipelineScenario
{
    public static PipelineResult Run()
    {
        Console.WriteLine("\n[JsonPipelineScenario] Simulating full AI -> JSON -> engine flow...");

        // Simulates what an AI API would return
        string aiGeneratedJson = """
            {
              "operations": [
                { "type": "PREPEND", "text": "CONFIDENTIAL - DRAFT\n\n" },
                { "type": "REPLACE_ALL", "find": "TODO", "replace": "[ACTION REQUIRED]", "caseSensitive": false },
                { "type": "REGEX_REPLACE", "pattern": "\\b(\\d{4}-\\d{2}-\\d{2})\\b", "replacement": "[DATE REDACTED]" },
                { "type": "TRIM_TRAILING_WHITESPACE" },
                { "type": "APPEND", "text": "\n\n--- END OF DOCUMENT ---" }
              ]
            }
            """;

        var ops = OperationMapper.FromPipelineJson(aiGeneratedJson);
        Console.WriteLine($"\n  Deserialized {ops.Count} operations from AI-generated JSON.");

        var doc = new TextDocument();
        doc.Load("""
            Project Status Report

            Status: In Progress
            Last Updated: 2024-12-01

            Tasks:
            - TODO: Update API documentation
            - TODO: Fix performance issues noted on 2024-11-15
            - DONE: Deploy to staging environment (2024-11-30)

            Next Review: 2025-01-15
            """);

        var result = new DocumentPipeline(doc)
            .Add(ops)
            .Execute();

        Console.WriteLine("\n  Serialized back to JSON:");
        Console.WriteLine("  " + OperationMapper.ToPipelineJson(ops));

        return result;
    }
}
