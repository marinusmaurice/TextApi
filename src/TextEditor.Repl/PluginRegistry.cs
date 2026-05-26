using TextEditor.Core;
using TextEditor.Core.Cursor;

namespace TextEditor.Repl;

/// <summary>
/// Indexes and executes plugin scripts (.csx files with frontmatter metadata).
///
/// <para>
/// A plugin file has this shape:
/// <code>
/// // @plugin
/// // Name: My Formatter
/// // Description: Normalises indentation across the document.
/// // Tags: format, cleanup
/// // @end
///
/// doc.ReplaceAll("  ", "\t");
/// Print("Done.");
/// </code>
/// </para>
///
/// <para>
/// Each execution is completely isolated — a fresh <see cref="CSharpScriptHost"/>
/// is spun up per run so plugins cannot leak state into the interactive REPL session
/// or into each other.
/// </para>
/// </summary>
public sealed class PluginRegistry
{
    private readonly List<PluginMetadata> _plugins = [];

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>All registered plugins in registration order.</summary>
    public IReadOnlyList<PluginMetadata> GetAll() => _plugins;

    /// <summary>
    /// Parse the frontmatter of a single <c>.csx</c> file and add it to the registry.
    /// Files without a valid frontmatter block are silently ignored.
    /// </summary>
    public void Register(string filePath)
    {
        var meta = ParseFrontmatter(filePath);
        if (meta is not null) _plugins.Add(meta);
    }

    /// <summary>
    /// Scan <paramref name="directory"/> for <c>*.csx</c> files and register each one.
    /// Non-plugin files (missing frontmatter) are silently skipped.
    /// </summary>
    public void ScanDirectory(string directory)
    {
        foreach (string path in Directory.GetFiles(
            directory, "*.csx", SearchOption.TopDirectoryOnly))
            Register(path);
    }

    /// <summary>
    /// Return all plugins whose name, description, or any tag contains
    /// <paramref name="query"/> (case-insensitive substring match).
    /// Pass an empty string to return everything.
    /// </summary>
    public IReadOnlyList<PluginMetadata> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return _plugins;
        string q = query.Trim().ToLowerInvariant();
        return _plugins
            .Where(p =>
                p.Name.ToLowerInvariant().Contains(q) ||
                p.Description.ToLowerInvariant().Contains(q) ||
                p.Tags.Any(t => t.ToLowerInvariant().Contains(q)))
            .ToList();
    }

    /// <summary>
    /// Execute <paramref name="plugin"/> in an isolated script session.
    /// <c>doc</c> and <c>mc</c> are wired to the supplied instances, just as in the REPL.
    /// </summary>
    public async Task<ScriptExecutionResult> ExecuteAsync(
        PluginMetadata plugin,
        TextDocument   doc,
        MultiCursor    mc,
        CancellationToken cancellationToken = default)
    {
        string code = await File.ReadAllTextAsync(plugin.FilePath, cancellationToken);
        code = StripFrontmatter(code);

        // Fresh host per execution — no state leakage between plugins or into the REPL.
        var host = new CSharpScriptHost(doc, mc);
        return await host.ExecuteAsync(code, cancellationToken);
    }

    // ── Frontmatter parsing ───────────────────────────────────────────────

    private static PluginMetadata? ParseFrontmatter(string filePath)
    {
        string[] lines;
        try   { lines = File.ReadAllLines(filePath); }
        catch { return null; }

        int start = -1, end = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t == "// @plugin" && start < 0)  start = i;
            else if (t == "// @end" && start >= 0) { end = i; break; }
        }
        if (start < 0 || end < 0) return null;

        string name        = "";
        string description = "";
        var    tags        = new List<string>();

        for (int i = start + 1; i < end; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("//")) continue;
            string kv = line[2..].Trim();

            if (kv.StartsWith("Name:", StringComparison.OrdinalIgnoreCase))
                name = kv[5..].Trim();
            else if (kv.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                description = kv[12..].Trim();
            else if (kv.StartsWith("Tags:", StringComparison.OrdinalIgnoreCase))
                tags.AddRange(kv[5..].Split(',')
                    .Select(t => t.Trim())
                    .Where(t => t.Length > 0));
        }

        if (string.IsNullOrWhiteSpace(name)) return null;
        return new PluginMetadata(filePath, name, description, tags);
    }

    private static string StripFrontmatter(string code)
    {
        int startIdx = code.IndexOf("// @plugin", StringComparison.Ordinal);
        if (startIdx < 0) return code;
        int endIdx = code.IndexOf("// @end", startIdx, StringComparison.Ordinal);
        if (endIdx < 0) return code;

        int after = endIdx + "// @end".Length;
        if (after < code.Length && code[after] == '\r') after++;
        if (after < code.Length && code[after] == '\n') after++;

        return code[..startIdx] + code[after..];
    }
}
