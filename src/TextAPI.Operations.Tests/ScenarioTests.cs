using FluentAssertions;
using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using TextAPI.Operations.Serialization;
using Xunit;

namespace TextAPI.Operations.Tests;

file static class Helpers
{
    public static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        d.Load(text);
        return d;
    }

    public static PipelineResult Run(TextDocument doc, params IDocumentOperation[] ops)
    {
        var p = new DocumentPipeline(doc);
        foreach (var op in ops) p.Add(op);
        return p.Execute();
    }
}

public class ScenarioTests
{
    // ── Scenario 1: CV/Resume AI editing ──────────────────────────────────────

    [Fact]
    public void CvEditing_InsertNewJob_NewJobAppearsInWorkExperience()
    {
        string cv = """
            John Smith
            Work Experience
            Software Engineer at Acme Corp 2020-2023
            Education
            BSc Computer Science
            """;

        var doc = Helpers.Doc(cv);
        string newJob = "\nSenior Engineer at Globex 2023-2025";

        var result = Helpers.Run(doc,
            new InsertAfterOperation { Anchor = "Work Experience", Text = newJob });

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("Senior Engineer at Globex 2023-2025");
    }

    [Fact]
    public void CvEditing_ReplaceCompanyName_UpdatesAllOccurrences()
    {
        string cv = """
            Work Experience
            Software Engineer at Acme Corp 2020-2023
            Reference: Acme Corp HR
            """;

        var doc = Helpers.Doc(cv);

        var result = Helpers.Run(doc,
            new ReplaceAllOperation { Find = "Acme Corp", Replace = "Globex" });

        result.Success.Should().BeTrue();
        doc.GetText().Should().NotContain("Acme Corp");
        doc.GetText().Should().Contain("Globex");
    }

    [Fact]
    public void CvEditing_RegexUpdateDate_UpdatesYearPattern()
    {
        string cv = "Software Engineer at Acme Corp 2020-2023";
        var doc = Helpers.Doc(cv);

        var result = Helpers.Run(doc,
            new RegexReplaceOperation { Pattern = @"2020-2023", Replacement = "2020-2024" });

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("2020-2024");
    }

    [Fact]
    public void CvEditing_FullPipeline_AllEditsApplied()
    {
        string cv = "John Smith\nWork Experience\nEngineer at Acme Corp 2020-2023\nEducation\nBSc";
        var doc = Helpers.Doc(cv);

        var result = Helpers.Run(doc,
            new InsertAfterOperation { Anchor = "Work Experience", Text = "\nSenior at Globex 2023-2025" },
            new ReplaceAllOperation { Find = "Acme Corp", Replace = "TechCo" },
            new RegexReplaceOperation { Pattern = @"2020-2023", Replacement = "2020-2024" });

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("Senior at Globex 2023-2025");
        doc.GetText().Should().Contain("TechCo");
        doc.GetText().Should().Contain("2020-2024");
    }

    // ── Scenario 2: Contract redlining ────────────────────────────────────────

    [Fact]
    public void Contract_ReplaceTerminationClause_NewClauseInPlace()
    {
        string contract = """
            AGREEMENT
            Termination
            Either party may terminate with 30 days notice.
            Indemnification
            Standard indemnification applies.
            """;

        var doc = Helpers.Doc(contract);

        var result = Helpers.Run(doc,
            new ReplaceSectionOperation
            {
                StartAnchor = "Termination\n",
                EndAnchor = "Indemnification",
                Text = "Either party may terminate with 90 days written notice.\n"
            });

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("90 days written notice");
        doc.GetText().Should().NotContain("30 days notice");
    }

    [Fact]
    public void Contract_AppendAnnotation_AnnotationAppearsAtEnd()
    {
        string contract = "AGREEMENT\nTerms apply.\n";
        var doc = Helpers.Doc(contract);

        var result = Helpers.Run(doc,
            new AppendOperation { Text = "\n[REVIEWED BY LEGAL 2025-01-01]" });

        result.Success.Should().BeTrue();
        doc.GetText().Should().EndWith("[REVIEWED BY LEGAL 2025-01-01]");
    }

    [Fact]
    public void Contract_RedlinePipeline_RestOfContractUnchanged()
    {
        string contract = "AGREEMENT\n[S]old clause[E]\nSignature Line\n";
        var doc = Helpers.Doc(contract);

        var result = Helpers.Run(doc,
            new ReplaceSectionOperation
            {
                StartAnchor = "[S]",
                EndAnchor = "[E]",
                Text = "new clause",
                IncludeAnchors = true
            });

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("AGREEMENT");
        doc.GetText().Should().Contain("Signature Line");
    }

    // ── Scenario 3: Batch config transformation ───────────────────────────────

    [Fact]
    public void BatchConfig_ReplaceVersion_AllDocsUpdated()
    {
        string[] configs =
        [
            "app_name=myapp\nversion=v1.0\nenabled=true",
            "service=api\nversion=v1.0\nport=8080",
            "module=worker\nversion=v1.0\nthreads=4"
        ];

        foreach (var config in configs)
        {
            var doc = Helpers.Doc(config);
            var result = Helpers.Run(doc, new ReplaceAllOperation { Find = "v1.0", Replace = "v2.0" });
            result.Success.Should().BeTrue();
            doc.GetText().Should().Contain("v2.0");
            doc.GetText().Should().NotContain("v1.0");
        }
    }

    [Fact]
    public void BatchConfig_TrimTrailingWhitespace_AllDocsClean()
    {
        string[] configs =
        [
            "key=val  \nother=x   ",
            "name=app\t\nenv=prod  "
        ];

        foreach (var config in configs)
        {
            var doc = Helpers.Doc(config);
            var result = Helpers.Run(doc, new TrimTrailingWhitespaceOperation());
            result.Success.Should().BeTrue();
            var lines = doc.GetText().Split('\n');
            foreach (var line in lines)
                line.Should().NotEndWith(" ").And.NotEndWith("\t");
        }
    }

    [Fact]
    public void BatchConfig_InsertAfterLineMatch_UpdatedFlagAddedToAllDocs()
    {
        string[] configs =
        [
            "version=v1.0\nother=x",
            "name=app\nversion=v1.0\nenv=prod"
        ];

        foreach (var config in configs)
        {
            var doc = Helpers.Doc(config);
            var result = Helpers.Run(doc,
                new InsertAfterLineMatchOperation { Pattern = "version", LineText = "updated=true" });
            result.Success.Should().BeTrue();
            doc.GetText().Should().Contain("updated=true");
        }
    }

    // ── Scenario 4: Rollback on bad AI output ─────────────────────────────────

    [Fact]
    public void RollbackScenario_InvalidSecondOp_DocCompletelyUnchanged()
    {
        string original = "Important document content\nDo not modify this.";
        var doc = Helpers.Doc(original);

        var result = Helpers.Run(doc,
            new AppendOperation { Text = "\nExtra line" },
            new InsertAfterOperation { Anchor = "[ANCHOR_THAT_DOES_NOT_EXIST]", Text = "injected" });

        result.Success.Should().BeFalse();
        result.WasRolledBack.Should().BeTrue();
        result.FailedOperationIndex.Should().Be(1);
        doc.GetText().Should().Be(original);
    }

    [Fact]
    public void RollbackScenario_TextBeforeEqualsTextAfterOnFailure()
    {
        var doc = Helpers.Doc("original text");

        var result = Helpers.Run(doc,
            new InsertAfterOperation { Anchor = "[NOPE]", Text = "x" });

        result.TextBefore.Should().Be(result.TextAfter);
        result.TextBefore.Should().Be("original text");
    }

    // ── Scenario 5: Query then mutate ─────────────────────────────────────────

    [Fact]
    public void QueryThenMutate_FindAllTodosReturnsMatches()
    {
        string text = "TODO: fix login\nDone: refactor\nTODO: update tests\nDone: deploy";
        var doc = Helpers.Doc(text);

        var findResult = Helpers.Run(doc, new FindAllOperation { Pattern = "TODO" });

        findResult.Success.Should().BeTrue();
        findResult.OperationResults[0].MatchCount.Should().Be(2);
        findResult.OperationResults[0].FoundMatches.Should().HaveCount(2);
    }

    [Fact]
    public void QueryThenMutate_ReplaceAllResolvesTodos()
    {
        string text = "TODO: fix login\nDone: refactor\nTODO: update tests";
        var doc = Helpers.Doc(text);

        var replaceResult = Helpers.Run(doc, new ReplaceAllOperation { Find = "TODO", Replace = "DONE" });

        replaceResult.Success.Should().BeTrue();
        doc.GetText().Should().NotContain("TODO");
        doc.GetText().Should().Contain("DONE: fix login");
        doc.GetText().Should().Contain("DONE: update tests");
    }

    // ── Scenario 6: JSON-driven pipeline (AI simulation) ──────────────────────

    [Fact]
    public void JsonDrivenPipeline_ReplaceAllAndTrim_ExecutesCorrectly()
    {
        string aiOutput = """
            {
              "operations": [
                { "type": "REPLACE_ALL", "find": "TODO", "replace": "DONE" },
                { "type": "TRIM_TRAILING_WHITESPACE" }
              ]
            }
            """;

        var doc = Helpers.Doc("TODO: task one  \nDone: task two   \nTODO: task three  ");
        var ops = OperationMapper.FromPipelineJson(aiOutput);

        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(ops);
        var result = pipeline.Execute();

        result.Success.Should().BeTrue();
        doc.GetText().Should().NotContain("TODO");
        doc.GetText().Should().NotEndWith(" ");
    }

    [Fact]
    public void JsonDrivenPipeline_ComplexPipeline_CorrectlyTransformsDocument()
    {
        string aiOutput = """
            {
              "operations": [
                { "type": "PREPEND", "text": "# Document\n" },
                { "type": "APPEND", "text": "\n# End" },
                { "type": "REPLACE_ALL", "find": "v1", "replace": "v2" }
              ]
            }
            """;

        var doc = Helpers.Doc("Content v1 here\nMore v1 content");
        var ops = OperationMapper.FromPipelineJson(aiOutput);

        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(ops);
        var result = pipeline.Execute();

        result.Success.Should().BeTrue();
        doc.GetText().Should().StartWith("# Document\n");
        doc.GetText().Should().EndWith("\n# End");
        doc.GetText().Should().NotContain("v1");
        doc.GetText().Should().Contain("v2");
    }

    [Fact]
    public void JsonDrivenPipeline_OperationResultsCountMatchesOpsCount()
    {
        string aiOutput = """
            {
              "operations": [
                { "type": "APPEND", "text": " !" },
                { "type": "PREPEND", "text": "Hi " },
                { "type": "NORMALISE_WHITESPACE" }
              ]
            }
            """;

        var doc = Helpers.Doc("World");
        var ops = OperationMapper.FromPipelineJson(aiOutput);

        var pipeline = new DocumentPipeline(doc);
        pipeline.Add(ops);
        var result = pipeline.Execute();

        result.Success.Should().BeTrue();
        result.OperationResults.Should().HaveCount(3);
    }
}
