using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Cursor;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class WB
{
    public static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        if (!string.IsNullOrEmpty(text)) d.Insert(0, text);
        return d;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. IsWordChar
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_IsWordCharTests
{
    [Theory]
    [InlineData('a')] [InlineData('z')] [InlineData('A')] [InlineData('Z')]
    [InlineData('0')] [InlineData('9')] [InlineData('_')]
    public void WordChar_ReturnsTrue(char c) =>
        WordBoundary.IsWordChar(c).Should().BeTrue();

    [Theory]
    [InlineData(' ')] [InlineData('\t')] [InlineData('\n')] [InlineData('\r')]
    [InlineData('.')] [InlineData(',')] [InlineData('!')] [InlineData('(')] [InlineData(')')]
    [InlineData('-')] [InlineData('+')] [InlineData('=')] [InlineData('"')] [InlineData('\'')]
    public void NonWordChar_ReturnsFalse(char c) =>
        WordBoundary.IsWordChar(c).Should().BeFalse();

    [Fact] public void UnicodeLetters_AreWordChars()
    {
        WordBoundary.IsWordChar('é').Should().BeTrue();
        WordBoundary.IsWordChar('ñ').Should().BeTrue();
        WordBoundary.IsWordChar('中').Should().BeTrue();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. GetWordBoundaryLeft
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_GetWordBoundaryLeftTests
{
    [Fact] public void EmptyDoc_ReturnsZero()
    {
        var doc = WB.Doc(string.Empty);
        WordBoundary.GetWordBoundaryLeft(doc, 0).Should().Be(0);
    }

    [Fact] public void AtStart_ReturnsZero()
    {
        var doc = WB.Doc("hello");
        WordBoundary.GetWordBoundaryLeft(doc, 0).Should().Be(0);
    }

    [Fact] public void AtEndOfWord_ReturnsWordStart()
    {
        var doc = WB.Doc("hello world");
        WordBoundary.GetWordBoundaryLeft(doc, 11).Should().Be(6, "from end of 'world' → start of 'world'");
    }

    [Fact] public void FromInsideWord_ReturnsWordStart()
    {
        var doc = WB.Doc("hello world");
        WordBoundary.GetWordBoundaryLeft(doc, 9).Should().Be(6, "from 'r' inside 'world' → start of 'world'");
    }

    [Fact] public void FromStartOfWord_JumpsToPreviousWordStart()
    {
        var doc = WB.Doc("hello world");
        WordBoundary.GetWordBoundaryLeft(doc, 6).Should().Be(0, "from start of 'world' → start of 'hello'");
    }

    [Fact] public void SkipsTrailingSpacesThenWord()
    {
        var doc = WB.Doc("hello   ");
        WordBoundary.GetWordBoundaryLeft(doc, 8).Should().Be(0, "skip spaces then 'hello'");
    }

    [Fact] public void AcrossNewlines_TreatsNewlineAsNonWord()
    {
        var doc = WB.Doc("alpha\nbeta");
        // from end of 'beta' (10), should go to start of 'beta' (6)
        WordBoundary.GetWordBoundaryLeft(doc, 10).Should().Be(6);
    }

    [Fact] public void PastEnd_ClampedThenScans()
    {
        var doc = WB.Doc("hello");
        WordBoundary.GetWordBoundaryLeft(doc, 999).Should().Be(0);
    }

    [Fact] public void MatchesCursorWordLeft()
    {
        var doc = WB.Doc("one two three");
        var cur = new TextCursor(doc, 13);
        WordBoundary.GetWordBoundaryLeft(doc, 13).Should().Be(cur.WordLeft(13));
    }

    [Fact] public void MultipleCallsConsistent()
    {
        var doc  = WB.Doc("foo bar baz");
        int step = WordBoundary.GetWordBoundaryLeft(doc, 11);   // baz start = 8
        step.Should().Be(8);
        step = WordBoundary.GetWordBoundaryLeft(doc, step);     // bar start = 4
        step.Should().Be(4);
        step = WordBoundary.GetWordBoundaryLeft(doc, step);     // foo start = 0
        step.Should().Be(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. GetWordBoundaryRight
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_GetWordBoundaryRightTests
{
    [Fact] public void EmptyDoc_ReturnsZero()
    {
        var doc = WB.Doc(string.Empty);
        WordBoundary.GetWordBoundaryRight(doc, 0).Should().Be(0);
    }

    [Fact] public void AtEnd_ReturnsLength()
    {
        var doc = WB.Doc("hello");
        WordBoundary.GetWordBoundaryRight(doc, 5).Should().Be(5);
    }

    [Fact] public void FromStartOfWord_SkipsWordThenSpaces()
    {
        var doc = WB.Doc("hello world");
        WordBoundary.GetWordBoundaryRight(doc, 0).Should().Be(6, "skip 'hello' + ' ' → start of 'world'");
    }

    [Fact] public void FromInsideWord_SkipsRestOfWordThenSpaces()
    {
        var doc = WB.Doc("hello world");
        WordBoundary.GetWordBoundaryRight(doc, 2).Should().Be(6, "skip 'llo' + ' ' → start of 'world'");
    }

    [Fact] public void OnNonWord_SkipsToNextWordStart()
    {
        var doc = WB.Doc("   hello");
        WordBoundary.GetWordBoundaryRight(doc, 0).Should().Be(3, "skip spaces → start of 'hello'");
    }

    [Fact] public void AtEndOfLastWord_ReturnsLength()
    {
        var doc = WB.Doc("hello");
        WordBoundary.GetWordBoundaryRight(doc, 0).Should().Be(5, "skip 'hello', no trailing non-word, land at Length");
    }

    [Fact] public void MatchesCursorWordRight()
    {
        var doc = WB.Doc("one two three");
        var cur = new TextCursor(doc);
        WordBoundary.GetWordBoundaryRight(doc, 0).Should().Be(cur.WordRight(0));
    }

    [Fact] public void MultipleCallsConsistent()
    {
        var doc  = WB.Doc("foo bar baz");
        int step = WordBoundary.GetWordBoundaryRight(doc, 0);   // after 'foo '→ 4
        step.Should().Be(4);
        step = WordBoundary.GetWordBoundaryRight(doc, step);    // after 'bar ' → 8
        step.Should().Be(8);
        step = WordBoundary.GetWordBoundaryRight(doc, step);    // after 'baz' → 11
        step.Should().Be(11);
    }

    [Fact] public void RepeatedCallsReachDocEnd()
    {
        var doc = WB.Doc("one two three");
        int pos = 0;
        while (pos < doc.Length) pos = WordBoundary.GetWordBoundaryRight(doc, pos);
        pos.Should().Be(doc.Length);
    }

    [Fact] public void PastEnd_ClampedReturnsLength()
    {
        var doc = WB.Doc("hello");
        WordBoundary.GetWordBoundaryRight(doc, 999).Should().Be(5);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. GetWordAt
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_GetWordAtTests
{
    [Fact] public void EmptyDoc_ReturnsEmpty()
    {
        var span = WordBoundary.GetWordAt(WB.Doc(string.Empty), 0);
        span.Should().Be(WordSpan.Empty);
    }

    [Fact] public void OnWordChar_ReturnsFullWord()
    {
        var doc  = WB.Doc("hello world");
        var span = WordBoundary.GetWordAt(doc, 2);   // 'l' in 'hello'
        span.Start.Should().Be(0);
        span.End.Should().Be(5);
        span.Text.Should().Be("hello");
        span.Length.Should().Be(5);
        span.IsEmpty.Should().BeFalse();
    }

    [Fact] public void OnSpaceChar_ReturnsSpaceGroup()
    {
        var doc  = WB.Doc("hello   world");
        var span = WordBoundary.GetWordAt(doc, 6);   // middle space
        span.Start.Should().Be(5);
        span.End.Should().Be(8);
        span.Text.Should().Be("   ");
    }

    [Fact] public void AtWordStart_ReturnsFullWord()
    {
        var doc  = WB.Doc("hello world");
        var span = WordBoundary.GetWordAt(doc, 6);   // 'w' in 'world'
        span.Text.Should().Be("world");
        span.Start.Should().Be(6);
        span.End.Should().Be(11);
    }

    [Fact] public void AtWordEnd_StillReturnsWord()
    {
        var doc  = WB.Doc("hello world");
        var span = WordBoundary.GetWordAt(doc, 4);   // 'o' at end of 'hello'
        span.Text.Should().Be("hello");
    }

    [Fact] public void AtDocLength_TreatsAsLastChar()
    {
        var doc  = WB.Doc("hello");
        var span = WordBoundary.GetWordAt(doc, 5);   // past 'o'
        // clamped to doc.Length - 1 = 4 ('o'), which expands to full 'hello'
        span.Text.Should().Be("hello");
    }

    [Fact] public void UnderscoreIsWordChar()
    {
        var doc  = WB.Doc("my_var = 1");
        var span = WordBoundary.GetWordAt(doc, 3);   // '_' in 'my_var'
        span.Text.Should().Be("my_var");
    }

    [Fact] public void DoesNotCrossNewline_LeftExpansion()
    {
        var doc  = WB.Doc("alpha\nbeta");
        var span = WordBoundary.GetWordAt(doc, 8);   // 't' in 'beta'
        span.Text.Should().Be("beta");
        span.Start.Should().Be(6);
    }

    [Fact] public void DoesNotCrossNewline_SpaceGroup()
    {
        // spaces on both sides of newline — should stop at newline
        var doc  = WB.Doc("foo   \n   bar");
        var span = WordBoundary.GetWordAt(doc, 4);   // space before '\n'
        span.End.Should().BeLessOrEqualTo(6, "space group must not cross newline");
    }

    [Fact] public void SingleCharDoc_WordChar()
    {
        var doc  = WB.Doc("x");
        var span = WordBoundary.GetWordAt(doc, 0);
        span.Text.Should().Be("x");
        span.Start.Should().Be(0);
        span.End.Should().Be(1);
    }

    [Fact] public void SingleCharDoc_NonWordChar()
    {
        var doc  = WB.Doc(".");
        var span = WordBoundary.GetWordAt(doc, 0);
        span.Text.Should().Be(".");
        span.Start.Should().Be(0);
        span.End.Should().Be(1);
    }

    [Fact] public void TextMatchesDocGetText()
    {
        var doc  = WB.Doc("the quick brown fox");
        var span = WordBoundary.GetWordAt(doc, 10);   // inside 'brown'
        span.Text.Should().Be(doc.GetText(span.Start, span.Length));
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. WordSpan value semantics
// ═══════════════════════════════════════════════════════════════════════════

public class WordSpanTests
{
    [Fact] public void Empty_Properties()
    {
        var e = WordSpan.Empty;
        e.Start.Should().Be(0);
        e.End.Should().Be(0);
        e.Text.Should().Be(string.Empty);
        e.Length.Should().Be(0);
        e.IsEmpty.Should().BeTrue();
    }

    [Fact] public void NonEmpty_Properties()
    {
        var s = new WordSpan(3, 8, "hello");
        s.Length.Should().Be(5);
        s.IsEmpty.Should().BeFalse();
    }

    [Fact] public void RecordEquality()
    {
        var a = new WordSpan(0, 5, "hello");
        var b = new WordSpan(0, 5, "hello");
        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact] public void RecordInequality_DifferentStart()
    {
        var a = new WordSpan(0, 5, "hello");
        var b = new WordSpan(1, 5, "hello");
        a.Should().NotBe(b);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. WordBoundary ↔ TextCursor parity
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_CursorParityTests
{
    [Theory]
    [InlineData("hello world", 0)]
    [InlineData("hello world", 5)]
    [InlineData("hello world", 6)]
    [InlineData("hello world", 11)]
    [InlineData("one two three", 4)]
    [InlineData("  spaces  ", 5)]
    [InlineData("a", 0)]
    [InlineData("a", 1)]
    public void WordBoundaryLeft_MatchesCursorWordLeft(string text, int offset)
    {
        var doc = WB.Doc(text);
        var cur = new TextCursor(doc, offset);
        WordBoundary.GetWordBoundaryLeft(doc, offset).Should().Be(
            cur.WordLeft(offset),
            $"left from {offset} in \"{text}\"");
    }

    [Theory]
    [InlineData("hello world", 0)]
    [InlineData("hello world", 5)]
    [InlineData("hello world", 6)]
    [InlineData("hello world", 11)]
    [InlineData("one two three", 0)]
    [InlineData("one two three", 4)]
    [InlineData("  spaces  ", 0)]
    [InlineData("a", 0)]
    [InlineData("a", 1)]
    public void WordBoundaryRight_MatchesCursorWordRight(string text, int offset)
    {
        var doc = WB.Doc(text);
        var cur = new TextCursor(doc, offset);
        WordBoundary.GetWordBoundaryRight(doc, offset).Should().Be(
            cur.WordRight(offset),
            $"right from {offset} in \"{text}\"");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. Fuzz — Left/Right match cursor over random documents
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_FuzzTests
{
    private static string RandomDoc(Random rng, int wordCount = 20)
    {
        var parts = new List<string>();
        string[] separators = [" ", "  ", "\n", "   ", ".", ", ", " - "];
        for (int i = 0; i < wordCount; i++)
        {
            parts.Add(new string((char)('a' + rng.Next(26)), rng.Next(1, 10)));
            if (i < wordCount - 1)
                parts.Add(separators[rng.Next(separators.Length)]);
        }
        return string.Concat(parts);
    }

    [Theory]
    [InlineData(9001, 300)]
    [InlineData(9002, 300)]
    [InlineData(9003, 300)]
    public void BoundaryLeft_MatchesCursor(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = RandomDoc(rng);
        var doc  = WB.Doc(text);
        var cur  = new TextCursor(doc);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, doc.Length + 1);
            WordBoundary.GetWordBoundaryLeft(doc, offset)
                .Should().Be(cur.WordLeft(offset),
                    $"WordLeft({offset}) seed={seed} op={i}");
        }
    }

    [Theory]
    [InlineData(9101, 300)]
    [InlineData(9102, 300)]
    [InlineData(9103, 300)]
    public void BoundaryRight_MatchesCursor(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = RandomDoc(rng);
        var doc  = WB.Doc(text);
        var cur  = new TextCursor(doc);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, doc.Length + 1);
            WordBoundary.GetWordBoundaryRight(doc, offset)
                .Should().Be(cur.WordRight(offset),
                    $"WordRight({offset}) seed={seed} op={i}");
        }
    }

    [Theory]
    [InlineData(9201, 100)]
    [InlineData(9202, 100)]
    public void GetWordAt_TextMatchesDocSlice(int seed, int ops)
    {
        var rng  = new Random(seed);
        var text = RandomDoc(rng);
        var doc  = WB.Doc(text);

        for (int i = 0; i < ops; i++)
        {
            int offset = rng.Next(0, doc.Length);
            var span   = WordBoundary.GetWordAt(doc, offset);
            span.Text.Should().Be(doc.GetText(span.Start, span.Length),
                $"GetWordAt({offset}) text mismatch, seed={seed} op={i}");
            span.Start.Should().BeGreaterOrEqualTo(0);
            span.End.Should().BeLessOrEqualTo(doc.Length);
            span.Start.Should().BeLessOrEqualTo(offset + 1,
                $"start {span.Start} should be ≤ offset+1 {offset + 1}");
            span.End.Should().BeGreaterOrEqualTo(offset,
                $"end {span.End} should be ≥ offset {offset}");
        }
    }

    [Theory]
    [InlineData(9301)]
    [InlineData(9302)]
    public void RepeatedRight_ReachesDocEnd(int seed)
    {
        var rng  = new Random(seed);
        var text = RandomDoc(rng, 30);
        var doc  = WB.Doc(text);

        int pos = 0, limit = text.Length + 10, steps = 0;
        while (pos < doc.Length && steps++ < limit)
            pos = WordBoundary.GetWordBoundaryRight(doc, pos);
        pos.Should().Be(doc.Length, "repeated GetWordBoundaryRight must reach doc end");
    }

    [Theory]
    [InlineData(9401)]
    [InlineData(9402)]
    public void RepeatedLeft_ReachesDocStart(int seed)
    {
        var rng  = new Random(seed);
        var text = RandomDoc(rng, 30);
        var doc  = WB.Doc(text);

        int pos = doc.Length, limit = text.Length + 10, steps = 0;
        while (pos > 0 && steps++ < limit)
            pos = WordBoundary.GetWordBoundaryLeft(doc, pos);
        pos.Should().Be(0, "repeated GetWordBoundaryLeft must reach doc start");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. WholeWord search uses WordBoundary.IsWordChar (no duplicate predicate)
// ═══════════════════════════════════════════════════════════════════════════

public class WordBoundary_SearchIntegrationTests
{
    [Fact] public void WholeWord_OnlyMatchesFullWord()
    {
        var doc    = new TextDocument();
        doc.Insert(0, "catfish cat concatenate");
        var opts   = new Core.Search.SearchOptions { WholeWord = true };
        var result = doc.FindAll("cat", opts).ToList();
        result.Should().HaveCount(1);
        result[0].Offset.Should().Be(8, "only standalone 'cat' matches");
    }

    [Fact] public void WholeWord_MatchAtDocStart()
    {
        var doc  = new TextDocument();
        doc.Insert(0, "cat and dog");
        var opts = new Core.Search.SearchOptions { WholeWord = true };
        doc.FindAll("cat", opts).Should().HaveCount(1);
    }

    [Fact] public void WholeWord_MatchAtDocEnd()
    {
        var doc  = new TextDocument();
        doc.Insert(0, "I see a cat");
        var opts = new Core.Search.SearchOptions { WholeWord = true };
        doc.FindAll("cat", opts).Should().HaveCount(1);
    }

    [Fact] public void WholeWord_UnderscoreIsWordChar()
    {
        var doc  = new TextDocument();
        doc.Insert(0, "my_cat is not a cat");
        var opts = new Core.Search.SearchOptions { WholeWord = true };
        var hits = doc.FindAll("cat", opts).ToList();
        // "my_cat" contains "cat" but is bounded by '_' (word char) on the left → no match
        hits.Should().HaveCount(1);
        hits[0].Offset.Should().Be(16);
    }

    [Fact] public void WholeWord_DigitIsWordChar()
    {
        var doc  = new TextDocument();
        doc.Insert(0, "cat1 cat 1cat");
        var opts = new Core.Search.SearchOptions { WholeWord = true };
        var hits = doc.FindAll("cat", opts).ToList();
        // "cat1" and "1cat" are bounded by digit → no whole-word match
        hits.Should().HaveCount(1);
        hits[0].Offset.Should().Be(5);
    }
}
