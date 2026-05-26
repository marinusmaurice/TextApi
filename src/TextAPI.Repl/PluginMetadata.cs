namespace TextAPI.Repl;

/// <summary>
/// Parsed metadata for a plugin script file.
/// Populated from the <c>// @plugin … // @end</c> frontmatter comment block.
/// </summary>
public sealed record PluginMetadata(
    string               FilePath,
    string               Name,
    string               Description,
    IReadOnlyList<string> Tags
);
