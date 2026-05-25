namespace TextEditor.Core.Language;

using TextEditor.Core.Language.TmLanguage;
using System.Text.RegularExpressions;

/// <summary>
/// A syntax tokeniser driven by a TextMate grammar (.tmLanguage.json).
///
/// Loads any VS Code–compatible grammar file and maps its scope names to
/// <see cref="SyntaxToken"/> type strings compatible with the engine's
/// <see cref="LineHighlightCache"/> and decoration tree.
///
/// Implements <see cref="IStatefulSyntaxTokeniser"/> using an integer state
/// that indexes into an internal <see cref="TmStateTable"/> — a grow-only
/// registry of scope stacks (one per unique inter-line scope stack seen).
/// State 0 is always the root (top-level) scope stack.
///
/// Limitations (acceptable for this implementation):
///   • No captures beyond \1 back-reference in end patterns
///   • No while patterns
///   • No applyEndPatternLast
///   • Regex compiled on first use per rule (cached)
/// </summary>
public sealed class TmLanguageTokeniser : IStatefulSyntaxTokeniser
{
    private readonly TmGrammar                  _grammar;
    private readonly TmStateTable               _stateTable = new();
    private readonly Dictionary<string, Regex>  _regexCache = new();

    public string LanguageId    => _grammar.LanguageId;
    public int    InitialState  => 0;  // state 0 = TmScopeStack.Root

    // ── Constructors ──────────────────────────────────────────────────────

    /// <summary>Load from a JSON string.</summary>
    public TmLanguageTokeniser(string grammarJson)
    {
        _grammar = TmGrammarParser.Parse(grammarJson);
    }

    // ── ISyntaxTokeniser (stateless fallback) ─────────────────────────────

    /// <summary>
    /// Tokenise without inter-line state — delegates to the stateful overload
    /// with state 0 (root scope).
    /// </summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(string lineText, int lineOffset = 0)
        => TokeniseLine(lineText, lineOffset, InitialState, out _);

    // ── IStatefulSyntaxTokeniser ──────────────────────────────────────────

    /// <summary>
    /// Tokenise <paramref name="lineText"/> using the scope stack identified by
    /// <paramref name="stateIn"/>.  Returns tokens with absolute offsets
    /// (i.e. offset from document start) and emits <paramref name="stateOut"/>
    /// as the state to pass to the next line.
    /// </summary>
    public IReadOnlyList<SyntaxToken> TokeniseLine(
        string lineText, int lineOffset, int stateIn, out int stateOut)
    {
        var stack  = _stateTable.Get(stateIn);
        var frames = new List<TmScopeFrame>(stack.Frames);  // mutable copy
        var tokens = new List<SyntaxToken>();
        int pos    = 0;

        while (pos < lineText.Length)
        {
            // ── Try to close the innermost scope first (at current pos) ──
            if (frames.Count > 0)
            {
                var top   = frames[^1];
                var endRx = GetOrCompileRegex(top.ResolvedEnd);
                var endM  = endRx.Match(lineText, pos);

                if (endM.Success && endM.Index == pos)
                {
                    if (endM.Length > 0)
                        EmitToken(tokens, top.Rule.Name ?? top.ContentName,
                                  lineOffset + pos, endM.Length);
                    pos += Math.Max(1, endM.Length);
                    frames.RemoveAt(frames.Count - 1);
                    continue;
                }
            }

            // ── Determine active pattern list ─────────────────────────────
            List<TmRule> activePatterns = frames.Count > 0
                ? frames[^1].Rule.Patterns
                : _grammar.Patterns;

            // If innermost scope has no sub-patterns, use top-level
            if (activePatterns.Count == 0 && frames.Count > 0)
                activePatterns = _grammar.Patterns;

            var (bestRule, bestMatch) = FindBestMatch(lineText, pos, activePatterns);

            if (bestMatch == null)
            {
                // No sub-pattern matched — handle remainder of line inside current scope
                if (frames.Count > 0)
                {
                    var top   = frames[^1];
                    var endRx = GetOrCompileRegex(top.ResolvedEnd);
                    var endM  = endRx.Match(lineText, pos);

                    if (endM.Success)
                    {
                        // Emit content up to the end match
                        if (endM.Index > pos)
                            EmitToken(tokens, top.ContentName ?? top.Rule.Name,
                                      lineOffset + pos, endM.Index - pos);
                        // Emit end token
                        if (endM.Length > 0)
                            EmitToken(tokens, top.Rule.Name,
                                      lineOffset + endM.Index, endM.Length);
                        pos = endM.Index + Math.Max(1, endM.Length);
                        frames.RemoveAt(frames.Count - 1);
                    }
                    else
                    {
                        // Rest of line is inside this scope
                        EmitToken(tokens, top.ContentName ?? top.Rule.Name,
                                  lineOffset + pos, lineText.Length - pos);
                        pos = lineText.Length;
                    }
                }
                else
                {
                    pos++;  // nothing matched, advance past one char
                }
                continue;
            }

            // ── Gap between pos and match start ───────────────────────────
            if (bestMatch.Index > pos)
            {
                if (frames.Count > 0)
                {
                    var top   = frames[^1];
                    var endRx = GetOrCompileRegex(top.ResolvedEnd);
                    var endM  = endRx.Match(lineText, pos);

                    if (endM.Success && endM.Index < bestMatch.Index)
                    {
                        // End fires before the best sub-pattern match
                        if (endM.Index > pos)
                            EmitToken(tokens, top.ContentName ?? top.Rule.Name,
                                      lineOffset + pos, endM.Index - pos);
                        if (endM.Length > 0)
                            EmitToken(tokens, top.Rule.Name,
                                      lineOffset + endM.Index, endM.Length);
                        pos = endM.Index + Math.Max(1, endM.Length);
                        frames.RemoveAt(frames.Count - 1);
                        continue;
                    }

                    // Emit gap as content of current scope
                    EmitToken(tokens, top.ContentName ?? top.Rule.Name,
                              lineOffset + pos, bestMatch.Index - pos);
                }
                pos = bestMatch.Index;
            }

            // ── Apply the matched rule ────────────────────────────────────
            if (bestRule!.Begin != null)
            {
                // Begin/end rule: push new frame
                string resolvedEnd = ResolveEndPattern(bestRule.End ?? "", bestMatch);
                frames.Add(new TmScopeFrame(bestRule, resolvedEnd, bestRule.ContentName));
                if (bestMatch.Length > 0)
                    EmitToken(tokens, bestRule.Name, lineOffset + pos, bestMatch.Length);
                pos += Math.Max(1, bestMatch.Length);
            }
            else
            {
                // Match rule
                if (bestMatch.Length > 0)
                    EmitToken(tokens, bestRule.Name, lineOffset + pos, bestMatch.Length);
                pos += Math.Max(1, bestMatch.Length);
            }
        }

        // Encode outgoing stack as an integer state
        var outStack = new TmScopeStack([.. frames]);
        stateOut = _stateTable.GetOrAdd(outStack);

        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return tokens;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private (TmRule? Rule, System.Text.RegularExpressions.Match? Match) FindBestMatch(
        string line, int pos, List<TmRule> patterns)
    {
        TmRule? bestRule  = null;
        System.Text.RegularExpressions.Match? bestMatch = null;

        foreach (var rule in patterns)
        {
            if (rule.Include != null)
            {
                var included = ResolveInclude(rule.Include);
                var (r, m)   = FindBestMatch(line, pos, included);
                if (m != null && (bestMatch == null || m.Index < bestMatch.Index))
                {
                    bestRule  = r;
                    bestMatch = m;
                }
                continue;
            }

            string? pattern = rule.Begin ?? rule.Match;
            if (pattern == null) continue;

            try
            {
                var rx = GetOrCompileRegex(pattern);
                var m  = rx.Match(line, pos);
                if (m.Success && (bestMatch == null || m.Index < bestMatch.Index))
                {
                    bestRule  = rule;
                    bestMatch = m;
                }
            }
            catch { /* invalid regex — skip */ }
        }

        return (bestRule, bestMatch);
    }

    private List<TmRule> ResolveInclude(string include)
    {
        if (include == "$self")
            return _grammar.Patterns;

        if (include.StartsWith('#') &&
            _grammar.Repository.TryGetValue(include[1..], out var rule))
        {
            return rule.Patterns.Count > 0 ? rule.Patterns : [rule];
        }

        return [];
    }

    private static void EmitToken(List<SyntaxToken> tokens, string? scopeName,
                                  int absoluteStart, int length)
    {
        if (length <= 0 || scopeName == null) return;
        string type = ScopeToTokenType(scopeName);
        if (type == "text") return;  // suppress plain-text tokens
        tokens.Add(new SyntaxToken { Start = absoluteStart, Length = length, Type = type });
    }

    private static string ScopeToTokenType(string scope)
    {
        if (scope.StartsWith("comment"))          return "comment";
        if (scope.StartsWith("string"))           return "string";
        if (scope.StartsWith("keyword"))          return "keyword";
        if (scope.StartsWith("storage"))          return "keyword";
        if (scope.StartsWith("constant.numeric")) return "number";
        if (scope.StartsWith("constant"))         return "constant";
        if (scope.StartsWith("entity.name"))      return "identifier";
        if (scope.StartsWith("variable"))         return "variable";
        if (scope.StartsWith("support"))          return "identifier";
        if (scope.StartsWith("punctuation"))      return "punctuation";
        return "text";
    }

    private static string ResolveEndPattern(string endPattern,
                                            System.Text.RegularExpressions.Match beginMatch)
    {
        return Regex.Replace(endPattern, @"\\(\d+)", m =>
        {
            int idx = int.Parse(m.Groups[1].Value);
            return idx < beginMatch.Groups.Count
                ? Regex.Escape(beginMatch.Groups[idx].Value)
                : "";
        });
    }

    private Regex GetOrCompileRegex(string pattern)
    {
        if (_regexCache.TryGetValue(pattern, out var rx)) return rx;
        try
        {
            rx = new Regex(pattern,
                           RegexOptions.Compiled,
                           TimeSpan.FromMilliseconds(100));
        }
        catch
        {
            rx = new Regex("(?!)", RegexOptions.Compiled);  // never matches
        }
        return _regexCache[pattern] = rx;
    }
}
