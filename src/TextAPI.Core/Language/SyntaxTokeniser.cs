namespace TextAPI.Core.Language;

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
/// A lightweight regex-based tokeniser for C# that also implements
/// <see cref="IStatefulSyntaxTokeniser"/>, carrying inter-line state for
/// multi-line block comments (<c>/* … */</c>).
///
/// State values:
///   <see cref="NormalState"/> (0) — default / outside any multi-line construct.
///   <see cref="BlockCommentState"/> (1) — inside an unclosed <c>/* … */</c>.
///
/// In production, replace with a Tree-sitter binding for incremental,
/// AST-accurate tokenisation. This implementation is self-contained and
/// has no external dependencies.
/// </summary>
public sealed class CSharpTokeniser : IStatefulSyntaxTokeniser
{
    public string LanguageId    => "csharp";
    public int    InitialState  => NormalState;

    private const int NormalState       = 0;
    private const int BlockCommentState = 1;

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

    // ── ISyntaxTokeniser (stateless) ──────────────────────────────────────

    /// <summary>
    /// Tokenise without inter-line state (treats the line as starting in
    /// <see cref="NormalState"/>).  Delegates to the stateful overload.
    /// </summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(string lineText, int lineOffset = 0)
        => TokeniseLine(lineText, lineOffset, NormalState, out _);

    // ── IStatefulSyntaxTokeniser ──────────────────────────────────────────

    /// <summary>
    /// Tokenise with inter-line state.
    ///
    /// Algorithm:
    ///  1. If <paramref name="stateIn"/> is <see cref="BlockCommentState"/>,
    ///     consume up to the first <c>*/</c> as a comment token; if none is
    ///     found the entire line is a comment and <paramref name="stateOut"/>
    ///     stays <see cref="BlockCommentState"/>.
    ///  2. Apply the normal regex rules to the remaining (non-comment) portion.
    ///  3. Scan for any uncovered <c>/*</c> that has no matching <c>*/</c> on
    ///     this line; if found, mark the rest of the line as a comment token
    ///     and set <paramref name="stateOut"/> to <see cref="BlockCommentState"/>.
    /// </summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(
        string lineText, int lineOffset, int stateIn, out int stateOut)
    {
        var tokens = new List<SyntaxToken>();

        if (lineText.Length == 0)
        {
            stateOut = stateIn;   // empty line carries state forward unchanged
            return tokens;
        }

        var covered     = new bool[lineText.Length];
        int normalStart = 0;          // first index where regex rules may apply

        // ── Phase 1: handle block-comment continuation ────────────────────
        if (stateIn == BlockCommentState)
        {
            int closeIdx = lineText.IndexOf("*/", StringComparison.Ordinal);
            if (closeIdx == -1)
            {
                // Entire line is inside the block comment.
                tokens.Add(new SyntaxToken
                    { Start = lineOffset, Length = lineText.Length, Type = "comment" });
                stateOut = BlockCommentState;
                return tokens;
            }

            // Comment closes on this line.
            int commentLen = closeIdx + 2;
            tokens.Add(new SyntaxToken
                { Start = lineOffset, Length = commentLen, Type = "comment" });
            for (int i = 0; i < commentLen; i++) covered[i] = true;
            normalStart = commentLen;
        }

        // ── Phase 2: apply regex rules to the remainder ───────────────────
        foreach (var (pattern, type) in Rules)
        {
            foreach (System.Text.RegularExpressions.Match m in pattern.Matches(lineText))
            {
                if (m.Index < normalStart) continue;  // inside continuation comment
                if (m.Index >= covered.Length || covered[m.Index]) continue;

                tokens.Add(new SyntaxToken
                    { Start = lineOffset + m.Index, Length = m.Length, Type = type });
                for (int i = m.Index; i < m.Index + m.Length && i < covered.Length; i++)
                    covered[i] = true;
            }
        }

        // ── Phase 3: detect unclosed block comment ────────────────────────
        // The operator regex marks individual '/' and '*' characters as covered,
        // so we cannot use the covered[] array here — it would falsely block
        // detection of "/*" sequences.  Instead we build a protection mask from
        // only STRING and COMMENT tokens, which genuinely shield their content.
        stateOut = NormalState;

        var shielded = new bool[lineText.Length];
        foreach (var tok in tokens)
        {
            if (tok.Type is not ("string" or "comment")) continue;
            int rs = tok.Start - lineOffset;
            int re = tok.End   - lineOffset;
            for (int k = Math.Max(rs, 0); k < re && k < shielded.Length; k++)
                shielded[k] = true;
        }

        for (int i = normalStart; i < lineText.Length - 1; i++)
        {
            if (shielded[i] || lineText[i] != '/' || lineText[i + 1] != '*') continue;

            // Found "/*" not inside a string or comment.
            // Does it close on this same line?
            bool closes = false;
            for (int j = i + 2; j < lineText.Length - 1; j++)
            {
                if (!shielded[j] && lineText[j] == '*' && lineText[j + 1] == '/')
                {
                    closes = true;
                    break;
                }
            }

            if (!closes)
            {
                // Unclosed: mark from here to end-of-line as a single comment token,
                // removing any overlapping tokens added by Phase 2.
                int len = lineText.Length - i;
                tokens.RemoveAll(t => t.Start >= lineOffset + i);
                tokens.Add(new SyntaxToken
                    { Start = lineOffset + i, Length = len, Type = "comment" });
                stateOut = BlockCommentState;
                break;
            }
            // Closed on this line: already handled by the block-comment regex.
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
