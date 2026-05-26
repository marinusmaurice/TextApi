using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Language;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class SH
{
    public static TextDocument Make(string text = "", bool useCSharp = true)
    {
        var doc = new TextDocument(useCSharp ? new CSharpTokeniser() : null);
        if (!string.IsNullOrEmpty(text)) doc.Load(text);
        return doc;
    }

    public static bool HasType(IReadOnlyList<SyntaxToken> tokens, string type)
        => tokens.Any(t => t.Type == type);

    public static IEnumerable<SyntaxToken> OfType(IReadOnlyList<SyntaxToken> tokens, string type)
        => tokens.Where(t => t.Type == type);
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. CSharpTokeniser — stateless (existing) behaviour
// ═══════════════════════════════════════════════════════════════════════════

public class CSharpTokeniserStatelessTests
{
    private readonly CSharpTokeniser _tok = new();

    [Fact] public void Keyword_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("var x = 1;");
        t.Should().Contain(tok => tok.Type == "keyword" && tok.Start == 0 && tok.Length == 3);
    }

    [Fact] public void Number_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("var x = 42;");
        t.Should().Contain(tok => tok.Type == "number");
    }

    [Fact] public void LineComment_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("x = 1; // comment");
        t.Should().Contain(tok => tok.Type == "comment");
    }

    [Fact] public void InlineBlockComment_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("x /* foo */ y");
        t.Should().Contain(tok => tok.Type == "comment" && tok.Length == 9); // /* foo */
    }

    [Fact] public void StringLiteral_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("var s = \"hello\";");
        t.Should().Contain(tok => tok.Type == "string");
    }

    [Fact] public void TypeName_IsTaggedCorrectly()
    {
        var t = _tok.TokeniseLine("MyClass obj");
        t.Should().Contain(tok => tok.Type == "type" && tok.Length == 7);
    }

    [Fact] public void Tokens_AreSortedByStartOffset()
    {
        var t = _tok.TokeniseLine("var x = 1;");
        t.Select(tok => tok.Start).Should().BeInAscendingOrder();
    }

    [Fact] public void EmptyLine_ReturnsNoTokens()
    {
        _tok.TokeniseLine("").Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. CSharpTokeniser — stateful (IStatefulSyntaxTokeniser)
// ═══════════════════════════════════════════════════════════════════════════

public class CSharpTokeniserStatefulTests
{
    private readonly CSharpTokeniser _tok = new();

    [Fact] public void NormalLine_StateOut_IsZero()
    {
        _tok.TokeniseLine("var x = 1;", 0, 0, out int stateOut);
        stateOut.Should().Be(0);
    }

    [Fact] public void LineWithOpenBlockComment_StateOut_IsOne()
    {
        _tok.TokeniseLine("var x = /* start", 0, 0, out int stateOut);
        stateOut.Should().Be(1);
    }

    [Fact] public void LineOpenBlockComment_RestOfLine_IsComment()
    {
        var tokens = _tok.TokeniseLine("x = /* start", 0, 0, out _);
        // Everything from "/*" to end of line should be comment
        var commentTokens = tokens.Where(t => t.Type == "comment").ToList();
        commentTokens.Should().NotBeEmpty();
        commentTokens.Last().End.Should().Be("x = /* start".Length);
    }

    [Fact] public void InlineClosedBlockComment_StateOut_IsZero()
    {
        _tok.TokeniseLine("x = /* comment */ y;", 0, 0, out int stateOut);
        stateOut.Should().Be(0);
    }

    [Fact] public void ContinuationLine_NoClose_StateOut_IsOne()
    {
        var tokens = _tok.TokeniseLine("  still inside comment", 0, 1, out int stateOut);
        stateOut.Should().Be(1);
        tokens.Should().ContainSingle(t => t.Type == "comment");
        tokens[0].Length.Should().Be("  still inside comment".Length);
    }

    [Fact] public void ContinuationLine_WithClose_StateOut_IsZero()
    {
        var tokens = _tok.TokeniseLine("  end */ var y = 2;", 0, 1, out int stateOut);
        stateOut.Should().Be(0);
        // Leading comment covers up to and including */
        tokens.Should().Contain(t => t.Type == "comment" && t.Start == 0);
        // var should be a keyword after the close
        tokens.Should().Contain(t => t.Type == "keyword");
    }

    [Fact] public void LineCommentContainingBlockCommentOpen_StateOut_IsZero()
    {
        // "/* inside // comment" — the // makes the /* irrelevant
        _tok.TokeniseLine("x = 1; // /* no state change", 0, 0, out int stateOut);
        stateOut.Should().Be(0);
    }

    [Fact] public void StringContainingBlockCommentOpen_StateOut_IsZero()
    {
        _tok.TokeniseLine("var s = \"/* not a comment */\";", 0, 0, out int stateOut);
        stateOut.Should().Be(0);
    }

    [Fact] public void EmptyLine_PreservesState()
    {
        _tok.TokeniseLine("", 0, 1, out int stateOut);
        stateOut.Should().Be(1);
    }

    [Fact] public void InitialState_IsZero()
    {
        _tok.InitialState.Should().Be(0);
    }

    [Fact] public void StatelessOverload_DelegatesToStateful()
    {
        // Opening block comment line should still have a comment token at the end
        var tokens = _tok.TokeniseLine("x /* open");
        tokens.Should().Contain(t => t.Type == "comment");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. LineHighlightCache — via TextDocument (white-box via GetSyntaxTokens)
// ═══════════════════════════════════════════════════════════════════════════

public class IncrementalHighlightCacheTests
{
    [Fact] public void GetSyntaxTokens_ReturnsCorrectTokens()
    {
        var doc = SH.Make("var x = 1;");
        var tokens = doc.GetSyntaxTokens(0);
        SH.HasType(tokens, "keyword").Should().BeTrue();
        SH.HasType(tokens, "number").Should().BeTrue();
    }

    [Fact] public void GetSyntaxTokens_CachesResult()
    {
        var doc = SH.Make("var x = 1;");
        var first  = doc.GetSyntaxTokens(0);
        var second = doc.GetSyntaxTokens(0);
        // Same list reference means the cache was hit
        second.Should().BeSameAs(first);
    }

    [Fact] public void Insert_SameLine_InvalidatesLine()
    {
        var doc = SH.Make("int x;");
        _ = doc.GetSyntaxTokens(0);   // warm cache
        doc.Insert(3, "eger");        // "integer x;"
        var tokens = doc.GetSyntaxTokens(0);
        // Should now tokenise with the new content (no crash, updated)
        tokens.Should().NotBeEmpty();
    }

    [Fact] public void Insert_NewLine_ShiftsCache()
    {
        // Line 0: "var a;"  Line 1: "var b;"
        var doc = SH.Make("var a;\nvar b;");
        _ = doc.GetSyntaxTokens(0);
        _ = doc.GetSyntaxTokens(1);

        // Insert a new line before line 1
        doc.Insert(6, "\nvar c;");
        // Original line 1 content is now at line 2
        var line2Tokens = doc.GetSyntaxTokens(2);
        SH.HasType(line2Tokens, "keyword").Should().BeTrue();   // "var" is still there
    }

    [Fact] public void Delete_NewLine_CompactsCache()
    {
        var doc = SH.Make("var a;\nvar b;\nvar c;");
        // Remove line 1 entirely (including its \n)
        doc.Delete(6, 7);   // delete "\nvar b;"
        doc.LineCount.Should().Be(2);
        var line1Tokens = doc.GetSyntaxTokens(1);
        SH.HasType(line1Tokens, "keyword").Should().BeTrue();
    }

    [Fact] public void Undo_FullyInvalidatesCache()
    {
        var doc = SH.Make("var x;");
        _ = doc.GetSyntaxTokens(0);
        doc.Insert(0, "int y;\n");
        doc.Undo();
        // After undo, GetSyntaxTokens should re-tokenise from scratch
        var tokens = doc.GetSyntaxTokens(0);
        tokens.Should().NotBeEmpty();
    }

    [Fact] public void SetTokeniser_InvalidatesCache()
    {
        var doc = SH.Make("var x;");
        _ = doc.GetSyntaxTokens(0);
        doc.SetTokeniser(new NullTokeniser());
        var tokens = doc.GetSyntaxTokens(0);
        tokens.Should().BeEmpty();   // NullTokeniser returns nothing
    }

    [Fact] public void InvalidateHighlightCache_FromZero_InvalidatesAll()
    {
        var doc = SH.Make("var a;\nvar b;");
        _ = doc.GetSyntaxTokens(0);
        _ = doc.GetSyntaxTokens(1);
        doc.InvalidateHighlightCache();
        // Should re-tokenise (no assertion on tokens, just no crash + correct result)
        doc.GetSyntaxTokens(0).Should().NotBeEmpty();
        doc.GetSyntaxTokens(1).Should().NotBeEmpty();
    }

    [Fact] public void InvalidateHighlightCache_FromLine_PreservesLinesBefore()
    {
        var doc = SH.Make("var a;\nvar b;\nvar c;");
        var line0First = doc.GetSyntaxTokens(0);
        doc.InvalidateHighlightCache(1);
        var line0After = doc.GetSyntaxTokens(0);
        // Line 0 was not invalidated, so it returns the same cached list
        line0After.Should().BeSameAs(line0First);
    }

    [Fact] public void NullTokeniser_ReturnsEmptyTokens()
    {
        var doc = SH.Make("var x;", useCSharp: false);
        doc.GetSyntaxTokens(0).Should().BeEmpty();
    }

    [Fact] public void OutOfRange_ReturnsEmpty()
    {
        var doc = SH.Make("var x;");
        doc.GetSyntaxTokens(99).Should().BeEmpty();
        doc.GetSyntaxTokens(-1).Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Multi-line block comment state propagation
// ═══════════════════════════════════════════════════════════════════════════

public class MultiLineCommentPropagationTests
{
    [Fact] public void ThreeLineBlockComment_AllLinesAreComment()
    {
        var doc = SH.Make("x = /* start\n   middle\n   end */ y;");

        var line0 = doc.GetSyntaxTokens(0);
        var line1 = doc.GetSyntaxTokens(1);
        var line2 = doc.GetSyntaxTokens(2);

        SH.HasType(line0, "comment").Should().BeTrue();      // "/* start" part
        SH.HasType(line1, "comment").Should().BeTrue();      // entirely comment
        SH.HasType(line2, "comment").Should().BeTrue();      // "   end */" part
        SH.HasType(line2, "identifier").Should().BeTrue();   // "y" after the close
    }

    [Fact] public void EditInsideComment_OnlyDirtyLinesReTokenised()
    {
        // "a\n/* open\nstill\n*/\nb"
        var doc = SH.Make("a\n/* open\nstill\n*/\nb");

        // Warm up all lines
        for (int i = 0; i < doc.LineCount; i++) _ = doc.GetSyntaxTokens(i);

        // Capture the cached list for line 4 ("b") BEFORE edit
        var line4Before = doc.GetSyntaxTokens(4);

        // Edit inside the comment (line 2 "still" → "still here")
        doc.Insert(doc.PositionToOffset(2, 5), " here");

        // Line 4 tokens after edit — should be re-tokenised (cache was invalidated from line 2)
        // but the result should still be the same content since state is unchanged
        var line4After = doc.GetSyntaxTokens(4);

        // Content of line 4 ("b") is unchanged, and its incoming state (0) is
        // unchanged → WarmUp should detect stabilisation and return equal tokens.
        line4After.Select(t => t.Type).Should().BeEquivalentTo(
            line4Before.Select(t => t.Type));
    }

    [Fact] public void OpeningBlockComment_AffectsSubsequentLines()
    {
        // Insert "/*" into a normal line — subsequent lines should become comment
        var doc = SH.Make("int x;\nint y;\nint z;");

        // Warm up
        for (int i = 0; i < doc.LineCount; i++) _ = doc.GetSyntaxTokens(i);

        // Insert /* at end of line 0
        doc.Insert(6, " /*");   // "int x; /*"

        var line1 = doc.GetSyntaxTokens(1);
        // Line 1 ("int y;") is now inside a block comment
        line1.Should().NotBeEmpty();
        line1.All(t => t.Type == "comment").Should().BeTrue();
    }

    [Fact] public void ClosingBlockComment_RestoresNormalStateBelow()
    {
        // Start with a block comment open
        var doc = SH.Make("/* open\nstill\nstill\nend */ int z;");

        var line3 = doc.GetSyntaxTokens(3);
        SH.HasType(line3, "keyword").Should().BeTrue();   // "int" after the close
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. TokeniseLines — decoration integration
// ═══════════════════════════════════════════════════════════════════════════

public class TokeniseLinesDecorationTests
{
    [Fact] public void TokeniseLines_PopulatesDecorations()
    {
        var doc = SH.Make("var x = 1;\nstring s;");
        doc.TokeniseLines(0, 1);

        int offset0 = doc.PositionToOffset(0, 0);
        int offset1 = doc.PositionToOffset(1, doc.GetLine(1).Length);
        var decorations = doc.GetDecorationsInRange(offset0, offset1 + 1).ToList();
        decorations.Should().NotBeEmpty();
        decorations.Should().Contain(d => d.Tag == "keyword");
    }

    [Fact] public void TokeniseLines_ClearsPreviousSyntaxDecorations()
    {
        var doc = SH.Make("var x;\nvar y;");
        doc.TokeniseLines(0, 1);
        int firstCount = doc.GetDecorationsInRange(0, doc.Length + 1).Count();

        doc.TokeniseLines(0, 1);
        int secondCount = doc.GetDecorationsInRange(0, doc.Length + 1).Count();

        // Should not double-add decorations
        secondCount.Should().Be(firstCount);
    }

    [Fact] public void TokeniseLine_UsesCache()
    {
        var doc = SH.Make("var x;");
        var first  = doc.TokeniseLine(0);
        var second = doc.TokeniseLine(0);
        second.Should().BeSameAs(first);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. WarmUp stabilisation
// ═══════════════════════════════════════════════════════════════════════════

public class WarmUpStabilisationTests
{
    [Fact] public void WarmUp_PopulatesRequestedRange()
    {
        // Use TextDocument.TokeniseLines which internally calls WarmUp
        var doc = SH.Make("var a;\nvar b;\nvar c;\nvar d;");
        doc.TokeniseLines(0, 1);   // warm up lines 0-1

        // Lines 0 and 1 should be cached (same reference on second call)
        var l0a = doc.GetSyntaxTokens(0);
        var l0b = doc.GetSyntaxTokens(0);
        l0b.Should().BeSameAs(l0a);
    }

    [Fact] public void AfterEdit_UnaffectedLines_ReturnConsistentTokens()
    {
        var doc = SH.Make("int a;\nint b;\nint c;");
        // Warm everything
        for (int i = 0; i < doc.LineCount; i++) _ = doc.GetSyntaxTokens(i);

        // Edit line 1 without changing state (no block comment involved)
        doc.Replace(doc.PositionToOffset(1, 4), 1, "x");   // "int b;" → "int x;"

        var line2 = doc.GetSyntaxTokens(2);
        SH.HasType(line2, "keyword").Should().BeTrue();   // "int" still a keyword
    }
}
