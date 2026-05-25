namespace TextEditor.Core.Snippets;

/// <summary>
/// A parsed, immutable snippet template ready to be instantiated into a document.
/// Create via <see cref="SnippetEngine.Parse"/>.
/// </summary>
public sealed class Snippet
{
    internal IReadOnlyList<SnippetPart> Parts { get; }

    /// <summary>The ordered list of unique tab-stop indices (excluding 0), sorted numerically.</summary>
    public IReadOnlyList<int> TabStopIndices { get; }

    /// <summary>Whether this snippet has a final exit tab stop ($0).</summary>
    public bool HasExitStop { get; }

    internal Snippet(IReadOnlyList<SnippetPart> parts)
    {
        Parts = parts;
        var indices = parts
            .Where(p => p.Kind == SnippetPartKind.TabStop && p.TabIndex != 0)
            .Select(p => p.TabIndex)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        TabStopIndices = indices;
        HasExitStop    = parts.Any(p => p.Kind == SnippetPartKind.TabStop && p.TabIndex == 0);
    }
}
