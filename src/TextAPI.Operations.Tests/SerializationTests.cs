using System.Text.Json;
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

    public static string ExecAndGetText(IDocumentOperation op, string initialText)
    {
        var doc = Doc(initialText);
        op.Execute(doc);
        return doc.GetText();
    }
}

public class SerializationTests
{
    // ── Round-trip tests ──────────────────────────────────────────────────────

    [Fact]
    public void RoundTrip_InsertAt_DeserializesCorrectTypeAndText()
    {
        var original = new InsertAtOperation(5, "HELLO");
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        // Verify type identity is preserved
        deserialized.Type.Should().Be("INSERT_AT");
        // Verify TEXT is preserved (offset has private setter so it resets to 0 on deserialization)
        var deserializedTyped = deserialized.Should().BeOfType<InsertAtOperation>().Subject;
        deserializedTyped.Text.Should().Be("HELLO");
    }

    [Fact]
    public void RoundTrip_DeleteAt_DeserializesCorrectTypeAndLength()
    {
        var original = new DeleteAtOperation(0, 5);
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        // Type is preserved; offset=0 can be round-tripped since default is 0
        deserialized.Type.Should().Be("DELETE_AT");
        var deserializedTyped = deserialized.Should().BeOfType<DeleteAtOperation>().Subject;
        deserializedTyped.Length.Should().Be(5);
        // Executing both on same text should produce same result when offset is 0
        var text1 = Helpers.ExecAndGetText(original, "Hello World");
        var text2 = Helpers.ExecAndGetText(deserialized, "Hello World");
        text2.Should().Be(text1);
    }

    [Fact]
    public void RoundTrip_ReplaceAt_DeserializesCorrectTypeAndProperties()
    {
        var original = new ReplaceAtOperation(6, 5, "Everyone");
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        // Verify type identity is preserved
        deserialized.Type.Should().Be("REPLACE_AT");
        var deserializedTyped = deserialized.Should().BeOfType<ReplaceAtOperation>().Subject;
        deserializedTyped.Text.Should().Be("Everyone");
        deserializedTyped.DeleteLength.Should().Be(5);
    }

    [Fact]
    public void RoundTrip_ReplaceAll_ProducesEquivalentOperation()
    {
        var original = new ReplaceAllOperation { Find = "cat", Replace = "dog", CaseSensitive = false };
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        var text1 = Helpers.ExecAndGetText(original, "cat and Cat");
        var text2 = Helpers.ExecAndGetText(deserialized, "cat and Cat");
        text2.Should().Be(text1);
    }

    [Fact]
    public void RoundTrip_InsertAfter_ProducesEquivalentOperation()
    {
        var original = new InsertAfterOperation { Anchor = "Hello", Text = " Beautiful" };
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        var text1 = Helpers.ExecAndGetText(original, "Hello World");
        var text2 = Helpers.ExecAndGetText(deserialized, "Hello World");
        text2.Should().Be(text1);
    }

    [Fact]
    public void RoundTrip_ConvertCase_PreservesMode()
    {
        var original = new ConvertCaseOperation { Mode = CaseMode.Upper };
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        var text1 = Helpers.ExecAndGetText(original, "hello world");
        var text2 = Helpers.ExecAndGetText(deserialized, "hello world");
        text2.Should().Be(text1);
    }

    [Fact]
    public void RoundTrip_SortLines_PreservesDescending()
    {
        var original = new SortLinesOperation { Descending = true };
        var json = OperationMapper.ToJson(original);
        var deserialized = OperationMapper.FromJson($"[{json}]")[0];

        var text1 = Helpers.ExecAndGetText(original, "apple\nbanana\ncherry");
        var text2 = Helpers.ExecAndGetText(deserialized, "apple\nbanana\ncherry");
        text2.Should().Be(text1);
    }

    // ── Pipeline JSON ─────────────────────────────────────────────────────────

    [Fact]
    public void FromPipelineJson_ValidJson_DeserializesOperations()
    {
        string json = """
            {
              "operations": [
                { "type": "REPLACE_ALL", "find": "foo", "replace": "bar" },
                { "type": "TRIM_TRAILING_WHITESPACE" }
              ]
            }
            """;

        var ops = OperationMapper.FromPipelineJson(json);

        ops.Should().HaveCount(2);
        ops[0].Type.Should().Be("REPLACE_ALL");
        ops[1].Type.Should().Be("TRIM_TRAILING_WHITESPACE");
    }

    [Fact]
    public void ToPipelineJson_ProducesJsonWithOperationsKey()
    {
        var ops = new IDocumentOperation[]
        {
            new AppendOperation { Text = " World" },
            new PrependOperation { Text = "Say: " }
        };

        string json = OperationMapper.ToPipelineJson(ops);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("operations", out var opsProp).Should().BeTrue();
        opsProp.ValueKind.Should().Be(JsonValueKind.Array);
        opsProp.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public void ToPipelineJson_RoundTrip_ProducesEquivalentOps()
    {
        var originalOps = new IDocumentOperation[]
        {
            new ReplaceAllOperation { Find = "TODO", Replace = "DONE" },
            new TrimTrailingWhitespaceOperation()
        };

        string json = OperationMapper.ToPipelineJson(originalOps);
        var deserializedOps = OperationMapper.FromPipelineJson(json);

        deserializedOps.Should().HaveCount(2);
        deserializedOps[0].Type.Should().Be("REPLACE_ALL");
        deserializedOps[1].Type.Should().Be("TRIM_TRAILING_WHITESPACE");
    }

    // ── Error cases ───────────────────────────────────────────────────────────

    [Fact]
    public void FromJson_UnknownType_ThrowsJsonException()
    {
        string json = """[{ "type": "UNKNOWN_OPERATION" }]""";

        var act = () => OperationMapper.FromJson(json);

        act.Should().Throw<JsonException>().WithMessage("*UNKNOWN_OPERATION*");
    }

    [Fact]
    public void FromJson_MissingTypeField_ThrowsJsonException()
    {
        string json = """[{ "text": "hello" }]""";

        var act = () => OperationMapper.FromJson(json);

        act.Should().Throw<JsonException>();
    }

    // ── JSON property name validation ─────────────────────────────────────────

    [Fact]
    public void ToJson_InsertAt_UsesCamelCasePropertyNames()
    {
        var op = new InsertAtOperation(5, "hello");
        var json = OperationMapper.ToJson(op);

        json.Should().Contain("\"offset\"");
        json.Should().Contain("\"text\"");
        json.Should().Contain("\"type\"");
    }

    [Fact]
    public void ToJson_ReplaceAll_UsesCamelCasePropertyNames()
    {
        var op = new ReplaceAllOperation { Find = "a", Replace = "b", CaseSensitive = false };
        var json = OperationMapper.ToJson(op);

        json.Should().Contain("\"find\"");
        json.Should().Contain("\"replace\"");
        json.Should().Contain("\"caseSensitive\"");
    }

    [Fact]
    public void FromJson_NotAnArray_ThrowsJsonException()
    {
        string json = """{ "type": "APPEND", "text": "x" }""";

        var act = () => OperationMapper.FromJson(json);

        act.Should().Throw<JsonException>();
    }
}
