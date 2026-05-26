using TextAPI.Core;
using TextAPI.Core.Language;
using FluentAssertions;
using Xunit;

namespace TextAPI.Tests;

// ── Helpers ──────────────────────────────────────────────────────────────────

/// <summary>Shared factory helpers used by all bracket test classes.</summary>
file static class BH
{
    /// <summary>Create a plain-text document (NullTokeniser) loaded with <paramref name="text"/>.</summary>
    public static TextDocument Plain(string text)
    {
        var doc = new TextDocument();
        doc.Load(text);
        return doc;
    }

    /// <summary>Create a C#-tokenised document loaded with <paramref name="text"/>.</summary>
    public static TextDocument CSharp(string text)
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load(text);
        return doc;
    }

    /// <summary>Offset of the first occurrence of <paramref name="ch"/> in the document.</summary>
    public static int FirstOf(TextDocument doc, char ch)
        => doc.GetText().IndexOf(ch);

    /// <summary>Offset of the last occurrence of <paramref name="ch"/> in the document.</summary>
    public static int LastOf(TextDocument doc, char ch)
        => doc.GetText().LastIndexOf(ch);

    /// <summary>Offset of the <paramref name="n"/>th (1-based) occurrence of <paramref name="ch"/>.</summary>
    public static int NthOf(TextDocument doc, char ch, int n)
    {
        string text = doc.GetText();
        int idx = -1;
        for (int i = 0; i < n; i++) idx = text.IndexOf(ch, idx + 1);
        return idx;
    }
}

// ── BracketMatcherForwardTests ────────────────────────────────────────────────

public class BracketMatcherForwardTests
{
    [Fact]
    public void OpenParen_MatchesClose()
    {
        var doc  = BH.Plain("(hello)");
        int open = BH.FirstOf(doc, '(');
        BracketMatcher.FindMatch(doc, open).Should().Be(BH.FirstOf(doc, ')'));
    }

    [Fact]
    public void OpenBracket_MatchesClose()
    {
        var doc  = BH.Plain("[abc]");
        BracketMatcher.FindMatch(doc, 0).Should().Be(4);
    }

    [Fact]
    public void OpenBrace_MatchesClose()
    {
        var doc  = BH.Plain("{xyz}");
        BracketMatcher.FindMatch(doc, 0).Should().Be(4);
    }

    [Fact]
    public void Nested_InnerOpen_MatchesInnerClose()
    {
        var doc = BH.Plain("((a))");
        // offset 1 is the inner '('
        BracketMatcher.FindMatch(doc, 1).Should().Be(3);
    }

    [Fact]
    public void Nested_OuterOpen_MatchesOuterClose()
    {
        var doc = BH.Plain("((a))");
        BracketMatcher.FindMatch(doc, 0).Should().Be(4);
    }

    [Fact]
    public void DeeplyNested_Braces()
    {
        var doc  = BH.Plain("{ { { } } }");
        // offset 0 should match the last '}'
        int last = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, 0).Should().Be(last);
    }

    [Fact]
    public void MixedTypes_IndependentPairs()
    {
        var doc = BH.Plain("([{}])");
        BracketMatcher.FindMatch(doc, 0).Should().Be(5);   // ( → )
        BracketMatcher.FindMatch(doc, 1).Should().Be(4);   // [ → ]
        BracketMatcher.FindMatch(doc, 2).Should().Be(3);   // { → }
    }

    [Fact]
    public void UnmatchedOpen_ReturnsNegOne()
    {
        var doc = BH.Plain("(abc");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void MultiLine_OpenBrace_MatchesCloseOnLaterLine()
    {
        var doc = BH.Plain("{\n    int x;\n}");
        int open  = BH.FirstOf(doc, '{');
        int close = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void MultiLine_NestedBraces_SpanMultipleLines()
    {
        string src = "{\n    {\n        x;\n    }\n}";
        var doc    = BH.Plain(src);
        int outer  = BH.FirstOf(doc, '{');
        int outerClose = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, outer).Should().Be(outerClose);
    }

    [Fact]
    public void NotABracket_ReturnsNegOne()
    {
        var doc = BH.Plain("hello");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void OffsetBeyondLength_ReturnsNegOne()
    {
        var doc = BH.Plain("(x)");
        BracketMatcher.FindMatch(doc, 99).Should().Be(-1);
    }

    [Fact]
    public void NegativeOffset_ReturnsNegOne()
    {
        var doc = BH.Plain("(x)");
        BracketMatcher.FindMatch(doc, -1).Should().Be(-1);
    }
}

// ── BracketMatcherBackwardTests ───────────────────────────────────────────────

public class BracketMatcherBackwardTests
{
    [Fact]
    public void CloseParen_MatchesOpen()
    {
        var doc   = BH.Plain("(hello)");
        int close = BH.LastOf(doc, ')');
        BracketMatcher.FindMatch(doc, close).Should().Be(BH.FirstOf(doc, '('));
    }

    [Fact]
    public void CloseBracket_MatchesOpen()
    {
        var doc = BH.Plain("[abc]");
        BracketMatcher.FindMatch(doc, 4).Should().Be(0);
    }

    [Fact]
    public void CloseBrace_MatchesOpen()
    {
        var doc = BH.Plain("{xyz}");
        BracketMatcher.FindMatch(doc, 4).Should().Be(0);
    }

    [Fact]
    public void Nested_InnerClose_MatchesInnerOpen()
    {
        var doc = BH.Plain("((a))");
        BracketMatcher.FindMatch(doc, 3).Should().Be(1);
    }

    [Fact]
    public void Nested_OuterClose_MatchesOuterOpen()
    {
        var doc = BH.Plain("((a))");
        BracketMatcher.FindMatch(doc, 4).Should().Be(0);
    }

    [Fact]
    public void UnmatchedClose_ReturnsNegOne()
    {
        var doc = BH.Plain("abc)");
        BracketMatcher.FindMatch(doc, 3).Should().Be(-1);
    }

    [Fact]
    public void MultiLine_CloseOnLaterLine_MatchesOpenOnEarlierLine()
    {
        var doc   = BH.Plain("{\n    int x;\n}");
        int open  = BH.FirstOf(doc, '{');
        int close = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, close).Should().Be(open);
    }

    [Fact]
    public void Backward_DeeplyNested()
    {
        var doc  = BH.Plain("{ { { } } }");
        int last = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, last).Should().Be(BH.FirstOf(doc, '{'));
    }
}

// ── BracketMatcherSkipTests ───────────────────────────────────────────────────

public class BracketMatcherSkipTests
{
    [Fact]
    public void BracketInsideStringLiteral_Ignored()
    {
        // The only real ( and ) are at positions 0 and the last char.
        // The ones inside the string should be skipped.
        var doc  = BH.CSharp("(\"()\")");
        int open = BH.FirstOf(doc, '(');
        int close = BH.LastOf(doc, ')');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void BracketInsideLineComment_Ignored()
    {
        // "( // ( \n )" — the '(' after '//' should be skipped
        var doc  = BH.CSharp("( // (\n)");
        int open  = BH.FirstOf(doc, '(');
        int close = BH.LastOf(doc, ')');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void BracketInsideBlockComment_Ignored()
    {
        var doc  = BH.CSharp("( /* ( */ )");
        int open  = BH.FirstOf(doc, '(');
        int close = BH.LastOf(doc, ')');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void CloseBracketInsideString_NotTreatedAsMatch()
    {
        // The matching '}' for the first '{' is the last one; the '}' inside
        // the string must be ignored.
        var doc  = BH.CSharp("{ string s = \"}\"; }");
        int open  = BH.FirstOf(doc, '{');
        int close = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void MultiLineBlockComment_BracketsSkipped()
    {
        string src = "{\n/* (\n( */\n}";
        var doc    = BH.CSharp(src);
        int open   = BH.FirstOf(doc, '{');
        int close  = BH.LastOf(doc, '}');
        BracketMatcher.FindMatch(doc, open).Should().Be(close);
    }

    [Fact]
    public void PlainText_NoBracketInsideString_AllBracketsLive()
    {
        // Without a tokeniser ALL characters are live, including the ( inside quotes.
        // "(\n(\n)" has two opens and one close, so the outer ( has no match.
        // The inner ( at position 2 DOES match the ) at position 4.
        var doc = BH.Plain("(\"(\")");
        // Positions: 0='(' 1='"' 2='(' 3='"' 4=')'
        // Both ( are live → depth never returns to 0 for offset 0
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
        // The inner ( at position 2 matches the ) at position 4
        BracketMatcher.FindMatch(doc, 2).Should().Be(4);
    }
}

// ── AutoIndentBasicTests ──────────────────────────────────────────────────────

public class AutoIndentBasicTests
{
    [Fact]
    public void NoIndent_ReturnsEmpty()
    {
        var doc = BH.CSharp("int x = 5;");
        doc.GetAutoIndent(0).Should().Be("");
    }

    [Fact]
    public void LeadingSpaces_Copied()
    {
        var doc = BH.CSharp("    int x = 5;");
        // caret at end of line
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end).Should().Be("    ");
    }

    [Fact]
    public void LeadingTabs_Copied()
    {
        var doc = BH.Plain("\t\tcode;");
        doc.GetAutoIndent(doc.GetText().Length).Should().Be("\t\t");
    }

    [Fact]
    public void LineEndingWithBrace_AddsOneLevel()
    {
        var doc  = BH.CSharp("void Foo() {");
        int end  = doc.GetText().Length;
        doc.GetAutoIndent(end).Should().Be("    ");
    }

    [Fact]
    public void LineEndingWithBrace_PreservesExistingIndent()
    {
        var doc = BH.CSharp("    void Foo() {");
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end).Should().Be("        "); // 4 existing + 4 new
    }

    [Fact]
    public void LineEndingWithBrace_CustomTabText()
    {
        var doc = BH.CSharp("void Foo() {");
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end, "\t").Should().Be("\t");
    }

    [Fact]
    public void LineEndingWithBraceAndTrailingComment_AddsOneLevel()
    {
        // The '{' is the last meaningful char; the comment follows.
        var doc = BH.CSharp("void Foo() { // setup");
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end).Should().Be("    ");
    }

    [Fact]
    public void LineThatDoesNotEndWithBrace_CopiesIndent()
    {
        var doc = BH.CSharp("    int x = 5;");
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end).Should().Be("    ");
    }

    [Fact]
    public void CaretMidLine_UsesCurrentLineIndent()
    {
        var doc = BH.CSharp("    int x = 5;");
        // caret after "    int " (offset 8)
        doc.GetAutoIndent(8).Should().Be("    ");
    }

    [Fact]
    public void MultiLine_CaretOnSecondLine_UsesSecondLineIndent()
    {
        var doc = BH.CSharp("{\n        int x;\n}");
        // caret at end of second line ("        int x;")
        int secondLineEnd = doc.PositionToOffset(1, doc.GetLine(1).Length);
        doc.GetAutoIndent(secondLineEnd).Should().Be("        ");
    }

    [Fact]
    public void EmptyLine_ReturnsEmpty()
    {
        var doc = BH.CSharp("");
        doc.GetAutoIndent(0).Should().Be("");
    }

    [Fact]
    public void LineEndingWithBrace_SingleSpace_TabText()
    {
        var doc = BH.CSharp("  {");
        int end = doc.GetText().Length;
        doc.GetAutoIndent(end, " ").Should().Be("   "); // 2 existing + 1 new
    }
}

// ── AutoIndentClosingBraceTests ───────────────────────────────────────────────

public class AutoIndentClosingBraceTests
{
    [Fact]
    public void ClosingBrace_ReturnsIndentOfOpenBraceLine()
    {
        // "{\n    }" — the '}' is at the start of line 1 (indented by 4 spaces in the source)
        // but its matching '{' is on line 0 with no indent → returns ""
        var doc   = BH.CSharp("{\n    }");
        int close = BH.LastOf(doc, '}');
        doc.GetClosingBraceIndent(close).Should().Be("");
    }

    [Fact]
    public void ClosingBrace_IndentedOpenBrace_ReturnsOpenIndent()
    {
        var doc   = BH.CSharp("    {\n        }");
        int close = BH.LastOf(doc, '}');
        doc.GetClosingBraceIndent(close).Should().Be("    ");
    }

    [Fact]
    public void ClosingBrace_Nested_ReturnsImmediateOpenIndent()
    {
        string src =
            "{\n" +
            "    {\n" +
            "        }\n" +  // matches the inner '{'
            "}";
        var doc    = BH.CSharp(src);
        int inner  = BH.NthOf(doc, '}', 1);  // first '}'
        doc.GetClosingBraceIndent(inner).Should().Be("    ");
    }

    [Fact]
    public void NotAClosingBrace_ReturnsNull()
    {
        var doc = BH.CSharp("(abc)");
        doc.GetClosingBraceIndent(BH.FirstOf(doc, '(')).Should().BeNull();
    }

    [Fact]
    public void UnmatchedClosingBrace_ReturnsNull()
    {
        var doc   = BH.CSharp("abc }");
        int close = BH.LastOf(doc, '}');
        doc.GetClosingBraceIndent(close).Should().BeNull();
    }

    [Fact]
    public void OutOfRange_ReturnsNull()
    {
        var doc = BH.CSharp("{}");
        doc.GetClosingBraceIndent(-1).Should().BeNull();
        doc.GetClosingBraceIndent(99).Should().BeNull();
    }

    [Fact]
    public void MultiLine_OuterClosingBrace_ReturnsOuterOpenIndent()
    {
        string src =
            "class Foo\n" +
            "{\n" +
            "    void Bar()\n" +
            "    {\n" +
            "    }\n" +
            "}";
        var doc    = BH.CSharp(src);
        int outer  = BH.LastOf(doc, '}');    // closing brace of class
        // Opening brace of class is on line 1 with no indent → ""
        doc.GetClosingBraceIndent(outer).Should().Be("");
    }

    [Fact]
    public void ClosingBrace_InsideString_MatchedCorrectly()
    {
        // The '}' inside the string literal should not confuse the backward scan
        var doc   = BH.CSharp("{ string s = \"}\"; }");
        int outer = BH.LastOf(doc, '}');
        doc.GetClosingBraceIndent(outer).Should().Be("");
    }
}

// ── TextDocumentBracketApiTests ───────────────────────────────────────────────

public class TextDocumentBracketApiTests
{
    [Fact]
    public void FindMatchingBracket_DelegatesToBracketMatcher()
    {
        var doc = BH.Plain("(x)");
        doc.FindMatchingBracket(0).Should().Be(2);
        doc.FindMatchingBracket(2).Should().Be(0);
    }

    [Fact]
    public void GetAutoIndent_DelegatesToAutoIndent()
    {
        var doc = BH.CSharp("void Foo() {");
        doc.GetAutoIndent(doc.GetText().Length).Should().Be("    ");
    }

    [Fact]
    public void GetClosingBraceIndent_DelegatesToAutoIndent()
    {
        var doc   = BH.CSharp("{\n}");
        int close = BH.LastOf(doc, '}');
        doc.GetClosingBraceIndent(close).Should().Be("");
    }

    [Fact]
    public void FindMatchingBracket_AllPairs_RoundTrip()
    {
        // For each bracket kind, FindMatch(open) == close AND FindMatch(close) == open.
        string[] samples = ["(x)", "[x]", "{x}"];
        foreach (string s in samples)
        {
            var doc = BH.Plain(s);
            doc.FindMatchingBracket(0).Should().Be(2, because: s);
            doc.FindMatchingBracket(2).Should().Be(0, because: s);
        }
    }

    [Fact]
    public void GetAutoIndent_DefaultTabText_IsFourSpaces()
    {
        var doc = BH.CSharp("{");
        doc.GetAutoIndent(1).Should().Be("    ");
    }

    [Fact]
    public void GetAutoIndent_CustomTabText_Respected()
    {
        var doc = BH.CSharp("{");
        doc.GetAutoIndent(1, "\t").Should().Be("\t");
    }

    [Fact]
    public void AfterInsertAndDelete_BracketMatchingStillCorrect()
    {
        var doc = BH.CSharp("{}");
        doc.Insert(1, "  ");   // now "{ }"... wait, it becomes "{  }"
        doc.FindMatchingBracket(0).Should().Be(3);
        doc.Delete(1, 2);      // back to "{}"
        doc.FindMatchingBracket(0).Should().Be(1);
    }
}

// ── BracketMatcherEdgeCaseTests ───────────────────────────────────────────────

public class BracketMatcherEdgeCaseTests
{
    [Fact]
    public void EmptyDocument_ReturnsNegOne()
    {
        var doc = BH.Plain("");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void SingleOpenBracket_ReturnsNegOne()
    {
        var doc = BH.Plain("(");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void SingleCloseBracket_ReturnsNegOne()
    {
        var doc = BH.Plain(")");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void AdjacentPairs_EachMatchesCorrectly()
    {
        var doc = BH.Plain("()[]{}");
        BracketMatcher.FindMatch(doc, 0).Should().Be(1);
        BracketMatcher.FindMatch(doc, 2).Should().Be(3);
        BracketMatcher.FindMatch(doc, 4).Should().Be(5);
    }

    [Fact]
    public void ForwardAndBackward_AreInverses()
    {
        string src = "(a(b(c)d)e)";
        var doc    = BH.Plain(src);
        // For every '(' find its close, then match back.
        for (int i = 0; i < src.Length; i++)
        {
            if (src[i] != '(') continue;
            int close = BracketMatcher.FindMatch(doc, i);
            close.Should().BeGreaterThan(0, because: $"offset {i}");
            BracketMatcher.FindMatch(doc, close).Should().Be(i,
                because: $"round-trip at offset {i}");
        }
    }

    [Fact]
    public void BracketAtLastChar_MatchedCorrectly()
    {
        var doc = BH.Plain("(x)");
        BracketMatcher.FindMatch(doc, 2).Should().Be(0);
    }

    [Fact]
    public void MismatchedTypes_NotCrossMatched()
    {
        // '(' should not match ']'
        var doc = BH.Plain("(abc]");
        BracketMatcher.FindMatch(doc, 0).Should().Be(-1);
    }

    [Fact]
    public void LargeNesting_MatchesCorrectly()
    {
        // 50 levels deep
        int depth    = 50;
        string open  = new string('(', depth);
        string close = new string(')', depth);
        var doc      = BH.Plain(open + "x" + close);
        // outermost open is at 0, outermost close is at 2*depth
        BracketMatcher.FindMatch(doc, 0).Should().Be(2 * depth);
        BracketMatcher.FindMatch(doc, 2 * depth).Should().Be(0);
    }
}
