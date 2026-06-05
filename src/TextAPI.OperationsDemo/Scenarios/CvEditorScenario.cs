using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;

namespace TextAPI.OperationsDemo.Scenarios;

public static class CvEditorScenario
{
    public static PipelineResult Run()
    {
        Console.WriteLine("\n[CvEditorScenario] Simulating AI-driven CV editing...");

        var doc = new TextDocument();
        doc.Load("""
            JOHN SMITH
            john.smith@email.com | LinkedIn: linkedin.com/in/jsmith

            WORK EXPERIENCE

            Software Engineer – Acme Corp (2019–2022)
            • Developed backend APIs using C#
            • Managed team of 3 developers
            • Responsible for database maintenance

            Education

            BSc Computer Science – State University (2015–2019)

            Skills
            C#, SQL, Docker
            """);

        var result = new DocumentPipeline(doc)
            .Add(new InsertAfterOperation
            {
                Anchor = "WORK EXPERIENCE",
                Text = "\n\nSenior Engineer – Microsoft (2022–Present)\n• Architected high-performance distributed systems\n• Led team of 8 engineers across 3 time zones\n• Reduced API latency by 60%\n"
            })
            .Add(new ReplaceAllOperation
            {
                Find = "Responsible for database maintenance",
                Replace = "Designed and optimised relational database schemas for high-availability systems"
            })
            .Add(new RegexReplaceOperation
            {
                Pattern = @"Managed team of (\d+) developers",
                Replacement = "Led cross-functional team of $1 developers"
            })
            .Add(new InsertAfterOperation
            {
                Anchor = "Skills\n",
                Text = "Python, Kubernetes, Azure, "
            })
            .Execute();

        return result;
    }
}
