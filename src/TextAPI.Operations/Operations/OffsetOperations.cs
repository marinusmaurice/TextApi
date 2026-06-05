using System.Text.Json.Serialization;
using TextAPI.Core;

namespace TextAPI.Operations;

/// <summary>
/// Inserts text at an absolute document offset.
/// </summary>
public sealed class InsertAtOperation : IOffsetAwareOperation
{
    [JsonPropertyName("type")]
    public string Type => "INSERT_AT";

    public int Offset { get; private set; }
    public string Text { get; init; } = "";

    [JsonConstructor]
    public InsertAtOperation() { }

    public InsertAtOperation(int offset, string text)
    {
        Offset = offset;
        Text = text;
    }

    public void ApplyDrift(int delta) => Offset += delta;

    public OperationResult Execute(TextDocument doc)
    {
        if (Offset < 0)
            return OperationResult.Fail("Offset must be >= 0");
        if (Offset > doc.Length)
            return OperationResult.Fail("Range exceeds document length");

        doc.Insert(Offset, Text);
        return OperationResult.Ok(charsInserted: Text.Length, resolvedOffset: Offset);
    }
}

/// <summary>
/// Deletes a run of characters starting at an absolute document offset.
/// </summary>
public sealed class DeleteAtOperation : IOffsetAwareOperation
{
    [JsonPropertyName("type")]
    public string Type => "DELETE_AT";

    public int Offset { get; private set; }
    public int Length { get; init; }

    [JsonConstructor]
    public DeleteAtOperation() { }

    public DeleteAtOperation(int offset, int length)
    {
        Offset = offset;
        Length = length;
    }

    public void ApplyDrift(int delta) => Offset += delta;

    public OperationResult Execute(TextDocument doc)
    {
        if (Offset < 0)
            return OperationResult.Fail("Offset must be >= 0");
        if (Offset + Length > doc.Length)
            return OperationResult.Fail("Range exceeds document length");

        doc.Delete(Offset, Length);
        return OperationResult.Ok(charsDeleted: Length, resolvedOffset: Offset);
    }
}

/// <summary>
/// Replaces a run of characters at an absolute document offset with new text.
/// </summary>
public sealed class ReplaceAtOperation : IOffsetAwareOperation
{
    [JsonPropertyName("type")]
    public string Type => "REPLACE_AT";

    public int Offset { get; private set; }
    public int DeleteLength { get; init; }
    public string Text { get; init; } = "";

    [JsonConstructor]
    public ReplaceAtOperation() { }

    public ReplaceAtOperation(int offset, int deleteLength, string text)
    {
        Offset = offset;
        DeleteLength = deleteLength;
        Text = text;
    }

    public void ApplyDrift(int delta) => Offset += delta;

    public OperationResult Execute(TextDocument doc)
    {
        if (Offset < 0)
            return OperationResult.Fail("Offset must be >= 0");
        if (Offset + DeleteLength > doc.Length)
            return OperationResult.Fail("Range exceeds document length");

        doc.Replace(Offset, DeleteLength, Text);
        return OperationResult.Ok(charsInserted: Text.Length, charsDeleted: DeleteLength, resolvedOffset: Offset);
    }
}
