namespace TextAPI.Core.Language.TmLanguage;

using System.Text.Json;

internal static class TmGrammarParser
{
    public static TmGrammar Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root      = doc.RootElement;
        var grammar   = new TmGrammar();

        if (root.TryGetProperty("scopeName", out var sn))
            grammar.ScopeName = sn.GetString() ?? "";

        // Try to derive languageId from scopeName (e.g. "source.cs" → "csharp")
        grammar.LanguageId = DeriveLanguageId(grammar.ScopeName);

        if (root.TryGetProperty("patterns", out var patterns))
            grammar.Patterns = ParsePatterns(patterns);

        if (root.TryGetProperty("repository", out var repo))
        {
            foreach (var prop in repo.EnumerateObject())
                grammar.Repository[prop.Name] = ParseRule(prop.Value);
        }

        return grammar;
    }

    private static List<TmRule> ParsePatterns(JsonElement el)
    {
        var list = new List<TmRule>();
        if (el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
            list.Add(ParseRule(item));
        return list;
    }

    private static TmRule ParseRule(JsonElement el)
    {
        var rule = new TmRule();
        if (el.TryGetProperty("name",        out var n))  rule.Name        = n.GetString();
        if (el.TryGetProperty("contentName", out var cn)) rule.ContentName = cn.GetString();
        if (el.TryGetProperty("match",       out var m))  rule.Match       = m.GetString();
        if (el.TryGetProperty("begin",       out var b))  rule.Begin       = b.GetString();
        if (el.TryGetProperty("end",         out var e))  rule.End         = e.GetString();
        if (el.TryGetProperty("include",     out var i))  rule.Include     = i.GetString();
        if (el.TryGetProperty("patterns",    out var p))  rule.Patterns    = ParsePatterns(p);
        return rule;
    }

    private static string DeriveLanguageId(string scopeName)
    {
        if (scopeName.EndsWith(".cs"))      return "csharp";
        if (scopeName.EndsWith(".python"))  return "python";
        if (scopeName.EndsWith(".js"))      return "javascript";
        if (scopeName.EndsWith(".ts"))      return "typescript";
        if (scopeName.EndsWith(".json"))    return "json";
        if (scopeName.EndsWith(".xml"))     return "xml";
        var parts = scopeName.Split('.');
        return parts.Length > 1 ? parts[^1] : scopeName;
    }
}
