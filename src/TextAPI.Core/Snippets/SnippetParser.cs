namespace TextAPI.Core.Snippets;

internal enum SnippetPartKind { Literal, TabStop, Variable }

internal sealed class SnippetPart
{
    public SnippetPartKind Kind       { get; init; }
    public string          Text       { get; init; } = ""; // literal text or placeholder text
    public int             TabIndex   { get; init; }       // for TabStop; 0 = exit
    public string          VarName    { get; init; } = ""; // for Variable
}

public static class SnippetParser
{
    /// <summary>Parse a snippet body into an ordered list of parts.</summary>
    internal static IReadOnlyList<SnippetPart> Parse(string body)
    {
        var parts = new List<SnippetPart>();
        int i = 0;
        var sb = new System.Text.StringBuilder();

        void FlushLiteral()
        {
            if (sb.Length > 0)
            {
                parts.Add(new SnippetPart { Kind = SnippetPartKind.Literal, Text = sb.ToString() });
                sb.Clear();
            }
        }

        while (i < body.Length)
        {
            if (body[i] == '$' && i + 1 < body.Length)
            {
                FlushLiteral();
                i++; // skip '$'

                if (body[i] == '{')
                {
                    // ${n:placeholder} or ${n} or ${VAR}
                    i++; // skip '{'
                    int nameStart = i;
                    while (i < body.Length && body[i] != ':' && body[i] != '}') i++;
                    string name = body[nameStart..i];

                    string placeholder = "";
                    if (i < body.Length && body[i] == ':')
                    {
                        i++; // skip ':'
                        int plStart = i;
                        // Simple: read until matching '}' (no nesting support needed)
                        while (i < body.Length && body[i] != '}') i++;
                        placeholder = body[plStart..i];
                    }
                    if (i < body.Length) i++; // skip '}'

                    if (int.TryParse(name, out int tabIdx))
                        parts.Add(new SnippetPart { Kind = SnippetPartKind.TabStop, TabIndex = tabIdx, Text = placeholder });
                    else
                        parts.Add(new SnippetPart { Kind = SnippetPartKind.Variable, VarName = name, Text = placeholder });
                }
                else if (char.IsDigit(body[i]))
                {
                    // $n
                    int numStart = i;
                    while (i < body.Length && char.IsDigit(body[i])) i++;
                    int tabIdx = int.Parse(body[numStart..i]);
                    parts.Add(new SnippetPart { Kind = SnippetPartKind.TabStop, TabIndex = tabIdx, Text = "" });
                }
                else if (char.IsLetter(body[i]) || body[i] == '_')
                {
                    // $VARIABLE_NAME
                    int varStart = i;
                    while (i < body.Length && (char.IsLetterOrDigit(body[i]) || body[i] == '_')) i++;
                    string varName = body[varStart..i];
                    parts.Add(new SnippetPart { Kind = SnippetPartKind.Variable, VarName = varName, Text = "" });
                }
                else
                {
                    // Lone '$' — treat as literal
                    sb.Append('$');
                }
            }
            else if (body[i] == '\\' && i + 1 < body.Length)
            {
                // Escape: \$ \\ etc.
                i++;
                sb.Append(body[i]);
                i++;
            }
            else
            {
                sb.Append(body[i]);
                i++;
            }
        }
        FlushLiteral();
        return parts;
    }
}
