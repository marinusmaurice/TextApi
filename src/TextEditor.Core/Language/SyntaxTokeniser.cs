namespace TextEditor.Core.Language;

/// <summary>A single syntax token.</summary>
public sealed class SyntaxToken
{
    public int    Start { get; init; }
    public int    Length { get; init; }
    public string Type  { get; init; } = string.Empty;   // "keyword", "string", "comment", …
    public int    End   => Start + Length;
}

/// <summary>
/// Contract for a syntax tokeniser.
/// Implementations can be simple regex-based or backed by Tree-sitter.
/// </summary>
public interface ISyntaxTokeniser
{
    /// <summary>Tokenise a single line. Returns tokens with offsets relative to line start.</summary>
    IReadOnlyList<SyntaxToken> TokeniseLine(string lineText, int lineOffset = 0);

    /// <summary>Language identifier (e.g. "csharp", "json", "python").</summary>
    string LanguageId { get; }
}

/// <summary>
/// A lightweight regex-based tokeniser for C#.
/// In production, replace with a Tree-sitter binding for incremental,
/// AST-accurate tokenisation. This implementation is self-contained and
/// has no external dependencies.
/// </summary>
public sealed class CSharpTokeniser : ISyntaxTokeniser
{
    public string LanguageId => "csharp";

    private static readonly (System.Text.RegularExpressions.Regex Pattern, string Type)[] Rules =
    [
        (new(@"//[^\n]*",                                           System.Text.RegularExpressions.RegexOptions.Compiled), "comment"),
        (new(@"/\*[\s\S]*?\*/",                                     System.Text.RegularExpressions.RegexOptions.Compiled), "comment"),
        (new(@"""(?:[^""\\]|\\.)*""",                               System.Text.RegularExpressions.RegexOptions.Compiled), "string"),
        (new(@"'(?:[^'\\]|\\.)*'",                                  System.Text.RegularExpressions.RegexOptions.Compiled), "string"),
        (new(@"\$""(?:[^""\\]|\\.)*""",                             System.Text.RegularExpressions.RegexOptions.Compiled), "string"),
        (new(@"\b(abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|virtual|void|volatile|while|var|async|await|yield|record|init|with)\b",
                                                                    System.Text.RegularExpressions.RegexOptions.Compiled), "keyword"),
        (new(@"\b[A-Z][A-Za-z0-9_]*\b",                            System.Text.RegularExpressions.RegexOptions.Compiled), "type"),
        (new(@"\b\d+\.?\d*[fFdDmMlLuU]?\b",                       System.Text.RegularExpressions.RegexOptions.Compiled), "number"),
        (new(@"[+\-*/%&|^~<>=!?:.,;()\[\]{}]",                    System.Text.RegularExpressions.RegexOptions.Compiled), "operator"),
        (new(@"\b[a-z_][A-Za-z0-9_]*\b",                          System.Text.RegularExpressions.RegexOptions.Compiled), "identifier"),
    ];

    public IReadOnlyList<SyntaxToken> TokeniseLine(string lineText, int lineOffset = 0)
    {
        var tokens = new List<SyntaxToken>();
        var covered = new bool[lineText.Length];

        foreach (var (pattern, type) in Rules)
        {
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(lineText))
            {
                if (covered[m.Index]) continue;
                tokens.Add(new SyntaxToken
                {
                    Start  = lineOffset + m.Index,
                    Length = m.Length,
                    Type   = type
                });
                for (int i = m.Index; i < m.Index + m.Length && i < covered.Length; i++)
                    covered[i] = true;
            }
        }

        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return tokens;
    }
}

/// <summary>
/// Null tokeniser — returns no tokens. Use for plain-text documents.
/// </summary>
public sealed class NullTokeniser : ISyntaxTokeniser
{
    public string LanguageId => "plaintext";
    public IReadOnlyList<SyntaxToken> TokeniseLine(string lineText, int lineOffset = 0) => [];
}
