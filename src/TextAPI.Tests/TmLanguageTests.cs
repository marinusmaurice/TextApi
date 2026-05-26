using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Language;
using Xunit;

namespace TextAPI.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Shared helpers
// ─────────────────────────────────────────────────────────────────────────────

file static class TH
{
    // Minimal C# TextMate grammar, embedded as a constant to avoid file-path issues.
    public const string MinimalCSharpGrammar = """
        {
          "scopeName": "source.cs",
          "patterns": [
            { "include": "#comments" },
            { "include": "#strings" },
            { "include": "#keywords" },
            { "include": "#numbers" }
          ],
          "repository": {
            "comments": {
              "patterns": [
                {
                  "name": "comment.line.double-slash.cs",
                  "match": "//.*$"
                },
                {
                  "name": "comment.block.cs",
                  "begin": "/\\*",
                  "end": "\\*/"
                }
              ]
            },
            "strings": {
              "patterns": [
                {
                  "name": "string.quoted.double.cs",
                  "begin": "\"",
                  "end": "\"",
                  "patterns": [
                    { "name": "constant.character.escape.cs", "match": "\\\\." }
                  ]
                }
              ]
            },
            "keywords": {
              "patterns": [
                {
                  "name": "keyword.control.cs",
                  "match": "\\b(if|else|for|foreach|while|do|switch|case|break|continue|return|throw|try|catch|finally|using|namespace|class|struct|interface|enum|void|new|this|base|static|public|private|protected|internal|sealed|abstract|virtual|override|readonly|const|var|true|false|null)\\b"
                }
              ]
            },
            "numbers": {
              "patterns": [
                {
                  "name": "constant.numeric.cs",
                  "match": "\\b[0-9]+(\\.[0-9]+)?\\b"
                }
              ]
            }
          }
        }
        """;

    public static TmLanguageTokeniser Tokeniser() => new(MinimalCSharpGrammar);
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. Parser tests
// ─────────────────────────────────────────────────────────────────────────────

public class TmGrammarParserTests
{
    [Fact]
    public void Parse_ScopeName()
    {
        // LanguageId is derived from scopeName "source.cs" → "csharp"
        var tok = TH.Tokeniser();
        tok.LanguageId.Should().Be("csharp");
    }

    [Fact]
    public void Parse_PatternsCount()
    {
        // Top-level patterns list contains 4 includes
        var tok = new TmLanguageTokeniser(TH.MinimalCSharpGrammar);
        // We can't easily inspect grammar internals, but tokenising a keyword
        // confirms patterns are wired correctly.
        var tokens = tok.TokeniseLine("return");
        tokens.Should().ContainSingle(t => t.Type == "keyword");
    }

    [Fact]
    public void Parse_RepositoryKeys()
    {
        // Grammar has 4 repository entries. Verify by using each one.
        var tok = TH.Tokeniser();
        tok.TokeniseLine("// comment").Should().Contain(t => t.Type == "comment");
        tok.TokeniseLine("\"hello\"").Should().Contain(t => t.Type == "string");
        tok.TokeniseLine("return").Should().Contain(t => t.Type == "keyword");
        tok.TokeniseLine("42").Should().Contain(t => t.Type == "number");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. Tokeniser — basic
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokeniserBasicTests
{
    private readonly TmLanguageTokeniser _tok = TH.Tokeniser();

    [Fact]
    public void Tokenise_EmptyLine_NoTokens()
    {
        _tok.TokeniseLine("").Should().BeEmpty();
    }

    [Fact]
    public void Tokenise_PlainText_NoTokens()
    {
        // "hello" doesn't match any rule — text tokens are suppressed
        var tokens = _tok.TokeniseLine("hello");
        tokens.Should().BeEmpty();
    }

    [Fact]
    public void Tokenise_Keyword_ReturnsKeyword()
    {
        var tokens = _tok.TokeniseLine("return");
        tokens.Should().ContainSingle(t => t.Type == "keyword");
    }

    [Fact]
    public void Tokenise_MultipleKeywords()
    {
        var tokens = _tok.TokeniseLine("if else");
        tokens.Where(t => t.Type == "keyword").Should().HaveCount(2);
    }

    [Fact]
    public void Tokenise_Number_ReturnsNumber()
    {
        var tokens = _tok.TokeniseLine("42");
        tokens.Should().ContainSingle(t => t.Type == "number");
    }

    [Fact]
    public void Tokenise_Float_ReturnsNumber()
    {
        var tokens = _tok.TokeniseLine("3.14");
        tokens.Should().ContainSingle(t => t.Type == "number");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. Comments
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokeniserCommentTests
{
    private readonly TmLanguageTokeniser _tok = TH.Tokeniser();

    [Fact]
    public void Tokenise_LineComment_ReturnsComment()
    {
        var tokens = _tok.TokeniseLine("// hello");
        tokens.Should().ContainSingle(t => t.Type == "comment");
        tokens[0].Start.Should().Be(0);
        tokens[0].Length.Should().Be("// hello".Length);
    }

    [Fact]
    public void Tokenise_LineCommentMidLine()
    {
        // "x = 1; // note" → number + comment
        var tokens = _tok.TokeniseLine("x = 1; // note");
        tokens.Should().Contain(t => t.Type == "number");
        tokens.Should().Contain(t => t.Type == "comment");
    }

    [Fact]
    public void Tokenise_BlockComment_SingleLine()
    {
        var tokens = _tok.TokeniseLine("/* hello */");
        // begin/end rule may emit open, content, and close as separate comment tokens
        tokens.Should().Contain(t => t.Type == "comment");
        tokens.Should().AllSatisfy(t => t.Type.Should().Be("comment"));
    }

    [Fact]
    public void Tokenise_BlockComment_MultiLine()
    {
        // Line 1: "/* start"  → non-root state (inside block comment)
        // Line 2: "end */"    → closes the comment
        _tok.TokeniseLine("/* start", 0, _tok.InitialState, out int state1);
        state1.Should().NotBe(_tok.InitialState);  // we're now inside the comment

        var line2Tokens = _tok.TokeniseLine("end */", 0, state1, out int state2);
        line2Tokens.Should().Contain(t => t.Type == "comment");
        state2.Should().Be(_tok.InitialState);  // back to root after close
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. Strings
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokeniserStringTests
{
    private readonly TmLanguageTokeniser _tok = TH.Tokeniser();

    [Fact]
    public void Tokenise_String_ReturnsString()
    {
        var tokens = _tok.TokeniseLine("\"hello\"");
        tokens.Should().Contain(t => t.Type == "string");
    }

    [Fact]
    public void Tokenise_String_MultiLine()
    {
        // Unclosed double-quote carries state forward
        _tok.TokeniseLine("\"unclosed", 0, _tok.InitialState, out int state1);
        state1.Should().NotBe(_tok.InitialState);
    }

    [Fact]
    public void Tokenise_String_WithEscape()
    {
        // Escape sequence inside string — still produces a string token
        var tokens = _tok.TokeniseLine("\"a\\nb\"");
        tokens.Should().Contain(t => t.Type == "string");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. State continuity
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokeniserStateTests
{
    private readonly TmLanguageTokeniser _tok = TH.Tokeniser();

    [Fact]
    public void State_EmptyLine_RootState()
    {
        _tok.TokeniseLine("", 0, _tok.InitialState, out int stateOut);
        stateOut.Should().Be(_tok.InitialState);
    }

    [Fact]
    public void State_AfterLineComment_RootState()
    {
        // Line comments don't persist state
        _tok.TokeniseLine("// entire line is comment", 0, _tok.InitialState, out int stateOut);
        stateOut.Should().Be(_tok.InitialState);
    }

    [Fact]
    public void State_AfterBlockCommentOpen_NonRoot()
    {
        _tok.TokeniseLine("/* start of comment", 0, _tok.InitialState, out int stateOut);
        stateOut.Should().NotBe(_tok.InitialState);
    }

    [Fact]
    public void State_AfterBlockCommentClose_Root()
    {
        _tok.TokeniseLine("/* open", 0, _tok.InitialState, out int mid);
        _tok.TokeniseLine("close */", 0, mid, out int stateOut);
        stateOut.Should().Be(_tok.InitialState);
    }

    [Fact]
    public void State_EqualStates_AreEqual()
    {
        // Two independent tokenisations of the same input should yield the same state
        _tok.TokeniseLine("/* open", 0, _tok.InitialState, out int state1);
        _tok.TokeniseLine("/* open", 0, _tok.InitialState, out int state2);
        state1.Should().Be(state2);
    }

    [Fact]
    public void State_DifferentStates_AreNotEqual()
    {
        int rootState = _tok.InitialState;
        _tok.TokeniseLine("/* open", 0, rootState, out int commentState);
        rootState.Should().NotBe(commentState);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. Token range correctness
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokenRangeTests
{
    private readonly TmLanguageTokeniser _tok = TH.Tokeniser();

    [Fact]
    public void Token_StartLessThanEnd()
    {
        var tokens = _tok.TokeniseLine("return 42");
        tokens.Should().AllSatisfy(t => t.End.Should().BeGreaterThan(t.Start));
    }

    [Fact]
    public void Token_TypeIsCorrect()
    {
        var tokens = _tok.TokeniseLine("return");
        tokens.Should().ContainSingle();
        tokens[0].Type.Should().Be("keyword");
    }

    [Fact]
    public void Token_OffsetWithinLine()
    {
        // "  return" — keyword at column 2
        var tokens = _tok.TokeniseLine("  return");
        var kw = tokens.Single(t => t.Type == "keyword");
        kw.Start.Should().Be(2);
        kw.End.Should().Be(8);   // "return" is 6 chars, 2+6=8
    }

    [Fact]
    public void Token_LineOffsetApplied()
    {
        // When lineOffset=100 is given, token Start should be >= 100
        var tokens = _tok.TokeniseLine("return", lineOffset: 100);
        tokens.Should().ContainSingle(t => t.Start >= 100);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Integration with TextDocument
// ─────────────────────────────────────────────────────────────────────────────

public class TmTextDocumentIntegrationTests
{
    [Fact]
    public void TextDocument_SetTmTokeniser_Works()
    {
        var tok = TH.Tokeniser();
        var doc = new TextDocument(tok);
        doc.LanguageId.Should().Be("csharp");
    }

    [Fact]
    public void TextDocument_GetSyntaxTokens_ReturnsTmTokens()
    {
        var tok = TH.Tokeniser();
        var doc = new TextDocument(tok);
        doc.Load("return 42;");
        var tokens = doc.GetSyntaxTokens(0);
        tokens.Should().Contain(t => t.Type == "keyword");
        tokens.Should().Contain(t => t.Type == "number");
    }

    [Fact]
    public void TextDocument_TokeniseLines_UsesTmGrammar()
    {
        var tok = TH.Tokeniser();
        var doc = new TextDocument(tok);
        doc.Load("var x = 1;\nreturn x;");
        doc.TokeniseLines(0, 1);

        var line0 = doc.GetSyntaxTokens(0);
        var line1 = doc.GetSyntaxTokens(1);
        line0.Should().Contain(t => t.Type == "keyword");  // "var"
        line1.Should().Contain(t => t.Type == "keyword");  // "return"
    }

    [Fact]
    public void TextDocument_MultiLineBlockComment_StatePropagatesToNextLine()
    {
        var tok = TH.Tokeniser();
        var doc = new TextDocument(tok);
        doc.Load("/* open\ncontinued\nclose */");

        var line1 = doc.GetSyntaxTokens(1);
        line1.Should().Contain(t => t.Type == "comment");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. Error handling / robustness
// ─────────────────────────────────────────────────────────────────────────────

public class TmTokeniserErrorHandlingTests
{
    [Fact]
    public void InvalidRegex_DoesNotThrow()
    {
        const string badGrammar = """
            {
              "scopeName": "source.test",
              "patterns": [
                { "name": "keyword.bad", "match": "[invalid(regex" }
              ],
              "repository": {}
            }
            """;

        var tok = new TmLanguageTokeniser(badGrammar);
        var act = () => tok.TokeniseLine("hello world");
        act.Should().NotThrow();
    }

    [Fact]
    public void UnknownInclude_DoesNotThrow()
    {
        const string grammar = """
            {
              "scopeName": "source.test",
              "patterns": [
                { "include": "#nonexistent" }
              ],
              "repository": {}
            }
            """;

        var tok = new TmLanguageTokeniser(grammar);
        var act = () => tok.TokeniseLine("return 42");
        act.Should().NotThrow();
    }

    [Fact]
    public void NullScopeName_EmitsNoToken()
    {
        // A rule with no name should not produce tokens
        const string grammar = """
            {
              "scopeName": "source.test",
              "patterns": [
                { "match": "\\btest\\b" }
              ],
              "repository": {}
            }
            """;

        var tok    = new TmLanguageTokeniser(grammar);
        var tokens = tok.TokeniseLine("test");
        tokens.Should().BeEmpty();
    }
}
