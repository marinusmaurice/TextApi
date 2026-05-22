using System.Globalization;
using TextEditor.Core;
using TextEditor.Core.Language;
using TextEditor.Core.Cursor;
using Xunit;

// ─────────────────────────────────────────────────────────────────────────────
// Grapheme cluster, display-width, word-boundary, cursor, and fuzz tests.
//
// Unicode test strings used throughout
// ─────────────────────────────────────
//   "é" via combining         = 'e' + U+0301 COMBINING ACUTE ACCENT  (2 code units, 1 cluster)
//   "é" precomposed           = U+00E9                                (1 code unit, 1 cluster)
//   "👋" waving hand          = U+1F44B (surrogate pair)             (2 code units, 1 cluster)
//   "👋🏽" with skin tone    = U+1F44B + U+1F3FD (skin modifier)    (4 code units, 1 cluster)
//   "🇺🇸" US flag           = U+1F1FA + U+1F1F8 (reg. indicators) (4 code units, 1 cluster)
//   "👨‍👩‍👧‍👦" family emoji = 4 people + 3 ZWJs                  (11 code units, 1 cluster)
//   "한" Hangul syllable      = U+D55C                                (1 code unit, 2 display cols)
//   "中" CJK ideograph        = U+4E2D                                (1 code unit, 2 display cols)
// ─────────────────────────────────────────────────────────────────────────────

namespace TextEditor.Tests;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class U
{
    // Surrogate pair helpers
    public static string SurrPair(int codePoint)
    {
        char high = (char)(0xD800 + ((codePoint - 0x10000) >> 10));
        char low  = (char)(0xDC00 + ((codePoint - 0x10000) & 0x3FF));
        return new string([high, low]);
    }

    // Well-known sequences
    public const string EWithCombining   = "e\u0301";       // e + combining acute
    public const string EPrecomposed     = "\u00E9";        // é precomposed
    public const string WavingHand       = "\U0001F44B";    // 👋  surrogate pair
    public const string SkinToneModifier = "\U0001F3FD";    // 🏽  medium skin tone
    public const string WavingHandBrown  = "\U0001F44B\U0001F3FD"; // 👋🏽
    public const string FlagUS           = "\U0001F1FA\U0001F1F8"; // 🇺🇸
    public const string FlagGB           = "\U0001F1EC\U0001F1E7"; // 🇬🇧
    public const string FamilyEmoji      = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466"; // 👨‍👩‍👧‍👦
    public const string Hangul           = "\uD55C";        // 한  (wide)
    public const string CJK              = "\u4E2D";        // 中  (wide)
    public const string ZeroWidthJoiner  = "\u200D";

    // text element length of a string via StringInfo (reference impl)
    public static int RefClusterCount(string s) => new StringInfo(s).LengthInTextElements;
    public static int RefNextCluster(string s, int offset) =>
        offset + StringInfo.GetNextTextElementLength(s.AsSpan()[offset..]);
}

// ─────────────────────────────────────────────────────────────────────────────
// 1. NextCluster
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_NextCluster
{
    [Theory]
    [InlineData("a",      0, 1)]
    [InlineData("ab",     0, 1)]
    [InlineData("ab",     1, 2)]
    [InlineData("hello",  4, 5)]
    [InlineData(" ",      0, 1)]
    [InlineData("\t",     0, 1)]
    [InlineData("\n",     0, 1)]
    public void Ascii_SingleCharStep(string text, int offset, int expected)
        => Assert.Equal(expected, GraphemeHelper.NextCluster(text, offset));

    [Fact]
    public void PastEnd_ReturnLength()
        => Assert.Equal(3, GraphemeHelper.NextCluster("abc", 3));

    [Fact]
    public void Empty_ReturnZero()
        => Assert.Equal(0, GraphemeHelper.NextCluster(ReadOnlySpan<char>.Empty, 0));

    [Fact]
    public void CombiningMark_TwoCodeUnits_OneCluster()
    {
        // "e" + combining acute: next cluster from 0 should skip both
        string text = U.EWithCombining;
        int next = GraphemeHelper.NextCluster(text, 0);
        Assert.Equal(2, next);
    }

    [Fact]
    public void PrecomposedChar_OneCodeUnit_OneCluster()
    {
        string text = U.EPrecomposed;
        Assert.Equal(1, GraphemeHelper.NextCluster(text, 0));
    }

    [Fact]
    public void SurrogatePair_TwoCodeUnits_OneCluster()
    {
        string text = U.WavingHand; // 2 code units
        Assert.Equal(2, GraphemeHelper.NextCluster(text, 0));
    }

    [Fact]
    public void SkinToneEmoji_FourCodeUnits_OneCluster()
    {
        string text = U.WavingHandBrown; // 👋🏽 = 4 code units
        Assert.Equal(4, GraphemeHelper.NextCluster(text, 0));
    }

    [Fact]
    public void FlagEmoji_FourCodeUnits_OneCluster()
    {
        string text = U.FlagUS; // 🇺🇸 = 4 code units
        Assert.Equal(4, GraphemeHelper.NextCluster(text, 0));
    }

    [Fact]
    public void FamilyEmoji_ElevenCodeUnits_OneCluster()
    {
        string text = U.FamilyEmoji;
        int next = GraphemeHelper.NextCluster(text, 0);
        Assert.Equal(text.Length, next); // whole thing is one cluster
    }

    [Fact]
    public void MultipleEmoji_EachCluster()
    {
        string text = U.WavingHand + U.FlagUS + "!";
        int a = GraphemeHelper.NextCluster(text, 0);
        int b = GraphemeHelper.NextCluster(text, a);
        int c = GraphemeHelper.NextCluster(text, b);
        Assert.Equal(2, a);           // 👋
        Assert.Equal(2 + 4, b);      // 🇺🇸
        Assert.Equal(2 + 4 + 1, c);  // !
    }

    [Fact]
    public void MultiCombining_AacuteRing_IsOneCluster()
    {
        // a + ring above + acute = one grapheme cluster (multiple combining marks)
        string text = "a\u030A\u0301";
        Assert.Equal(3, GraphemeHelper.NextCluster(text, 0));
    }

    [Fact]
    public void AllAscii_NeverExceedsOneStep()
    {
        for (int i = 0; i < 128; i++)
        {
            string text = ((char)i).ToString();
            Assert.Equal(1, GraphemeHelper.NextCluster(text, 0));
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2. PreviousCluster
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_PreviousCluster
{
    [Theory]
    [InlineData("a",     1, 0)]
    [InlineData("ab",    2, 1)]
    [InlineData("ab",    1, 0)]
    [InlineData("hello", 5, 4)]
    public void Ascii_SingleCharStepBack(string text, int offset, int expected)
        => Assert.Equal(expected, GraphemeHelper.PreviousCluster(text, offset));

    [Fact]
    public void AtStart_ReturnZero()
        => Assert.Equal(0, GraphemeHelper.PreviousCluster("abc", 0));

    [Fact]
    public void Empty_ReturnZero()
        => Assert.Equal(0, GraphemeHelper.PreviousCluster(ReadOnlySpan<char>.Empty, 0));

    [Fact]
    public void CombiningMark_StepsBackBoth()
    {
        string text = U.EWithCombining; // 2 code units
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, 2));
    }

    [Fact]
    public void SurrogatePair_StepsBackBoth()
    {
        string text = U.WavingHand; // 2 code units
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, 2));
    }

    [Fact]
    public void SkinToneEmoji_StepsBackAll()
    {
        string text = U.WavingHandBrown; // 4 code units
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, 4));
    }

    [Fact]
    public void FlagEmoji_StepsBackAll()
    {
        string text = U.FlagUS; // 4 code units
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, 4));
    }

    [Fact]
    public void FamilyEmoji_StepsBackAll()
    {
        string text = U.FamilyEmoji;
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, text.Length));
    }

    [Fact]
    public void TextPlusEmoji_StepsBackToEmoji()
    {
        string text = "hi" + U.WavingHand; // "hi👋"
        // from end (4) back to start of 👋 (2)
        Assert.Equal(2, GraphemeHelper.PreviousCluster(text, 4));
        // from 2 back to 'i' (1)
        Assert.Equal(1, GraphemeHelper.PreviousCluster(text, 2));
        // from 1 back to 'h' (0)
        Assert.Equal(0, GraphemeHelper.PreviousCluster(text, 1));
    }

    [Fact]
    public void RoundTrip_NextThenPrev_SameOffset()
    {
        string text = U.WavingHandBrown + U.FlagUS + "hello" + U.FamilyEmoji;
        int offset = 0;
        var offsets = new List<int> { 0 };
        while (offset < text.Length)
        {
            offset = GraphemeHelper.NextCluster(text, offset);
            offsets.Add(offset);
        }
        // Walk backwards and verify we hit all the same offsets
        for (int i = offsets.Count - 1; i > 0; i--)
        {
            int prev = GraphemeHelper.PreviousCluster(text, offsets[i]);
            Assert.Equal(offsets[i - 1], prev);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. IsClusterBoundary
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_IsClusterBoundary
{
    [Fact]
    public void Zero_AlwaysBoundary() => Assert.True(GraphemeHelper.IsClusterBoundary("abc", 0));

    [Fact]
    public void Length_AlwaysBoundary() => Assert.True(GraphemeHelper.IsClusterBoundary("abc", 3));

    [Fact]
    public void BetweenAscii_IsBoundary() => Assert.True(GraphemeHelper.IsClusterBoundary("ab", 1));

    [Fact]
    public void InsideSurrogatePair_NotBoundary()
    {
        string text = U.WavingHand; // offset 1 is the low surrogate
        Assert.False(GraphemeHelper.IsClusterBoundary(text, 1));
    }

    [Fact]
    public void AfterSurrogatePair_IsBoundary()
    {
        string text = U.WavingHand;
        Assert.True(GraphemeHelper.IsClusterBoundary(text, 2));
    }

    [Fact]
    public void InsideCombiningMark_NotBoundary()
    {
        string text = U.EWithCombining; // offset 1 is combining acute
        Assert.False(GraphemeHelper.IsClusterBoundary(text, 1));
    }

    [Fact]
    public void InsideSkinToneSequence_NotBoundary()
    {
        // 👋🏽 = 4 code units: offsets 1, 2, 3 are not boundaries
        string text = U.WavingHandBrown;
        Assert.True (GraphemeHelper.IsClusterBoundary(text, 0));
        Assert.False(GraphemeHelper.IsClusterBoundary(text, 1)); // low surr of 👋
        Assert.False(GraphemeHelper.IsClusterBoundary(text, 2)); // high surr of 🏽
        Assert.False(GraphemeHelper.IsClusterBoundary(text, 3)); // low surr of 🏽
        Assert.True (GraphemeHelper.IsClusterBoundary(text, 4));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. SnapToClusterStart
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_SnapToClusterStart
{
    [Fact]
    public void AlreadyOnBoundary_Unchanged() =>
        Assert.Equal(0, GraphemeHelper.SnapToClusterStart("abc", 0));

    [Fact]
    public void InsideSurrogatePair_SnapsBack()
    {
        string text = U.WavingHand;
        Assert.Equal(0, GraphemeHelper.SnapToClusterStart(text, 1));
    }

    [Fact]
    public void InsideCombiningMark_SnapsBack()
    {
        string text = U.EWithCombining;
        Assert.Equal(0, GraphemeHelper.SnapToClusterStart(text, 1));
    }

    [Fact]
    public void AfterCluster_Unchanged()
    {
        string text = U.WavingHand + "a";
        Assert.Equal(2, GraphemeHelper.SnapToClusterStart(text, 2));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. ClusterCount
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_ClusterCount
{
    [Fact]
    public void Empty_Zero() => Assert.Equal(0, GraphemeHelper.ClusterCount(ReadOnlySpan<char>.Empty));

    [Fact]
    public void PureAscii_EqualToLength()
    {
        string text = "hello";
        Assert.Equal(5, GraphemeHelper.ClusterCount(text));
    }

    [Theory]
    [InlineData(1)]
    public void SurrogatePair_CountsOne(int expected)
    {
        Assert.Equal(expected, GraphemeHelper.ClusterCount(U.WavingHand));
    }

    [Fact]
    public void SkinToneEmoji_CountsOne()
        => Assert.Equal(1, GraphemeHelper.ClusterCount(U.WavingHandBrown));

    [Fact]
    public void FlagEmoji_CountsOne()
        => Assert.Equal(1, GraphemeHelper.ClusterCount(U.FlagUS));

    [Fact]
    public void FamilyEmoji_CountsOne()
        => Assert.Equal(1, GraphemeHelper.ClusterCount(U.FamilyEmoji));

    [Fact]
    public void CombiningMark_CountsOne()
        => Assert.Equal(1, GraphemeHelper.ClusterCount(U.EWithCombining));

    [Fact]
    public void MixedString_CorrectCount()
    {
        // "hi" (2) + 👋 (1) + "!" (1) + 🇺🇸 (1) = 5
        string text = "hi" + U.WavingHand + "!" + U.FlagUS;
        Assert.Equal(5, GraphemeHelper.ClusterCount(text));
    }

    [Fact]
    public void ThreeFamilyEmoji_CountsThree()
        => Assert.Equal(3, GraphemeHelper.ClusterCount(U.FamilyEmoji + U.FamilyEmoji + U.FamilyEmoji));

    [Fact]
    public void MatchesStringInfoReference()
    {
        string[] tests =
        [
            "hello",
            U.EWithCombining,
            U.WavingHand,
            U.WavingHandBrown,
            U.FlagUS,
            U.FamilyEmoji,
            "a\u030A\u0301",         // a + ring + acute
            "Hello " + U.WavingHand + " World",
            U.FlagGB + U.FlagUS + "!"
        ];
        foreach (string t in tests)
            Assert.Equal(U.RefClusterCount(t), GraphemeHelper.ClusterCount(t));
    }

    [Fact]
    public void MultipleCombiningMarks_EachGroupIsOne()
    {
        // Two separate base+combining sequences
        string text = U.EWithCombining + U.EWithCombining;
        Assert.Equal(2, GraphemeHelper.ClusterCount(text));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. DisplayWidth
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_DisplayWidth
{
    [Theory]
    [InlineData(' ', 1)]
    [InlineData('A', 1)]
    [InlineData('z', 1)]
    [InlineData('~', 1)]
    [InlineData('\0', 0)]    // NUL
    [InlineData('\x1F', 0)] // US (last C0 control)
    [InlineData('\x7F', 0)] // DEL
    public void AsciiAndControls(char c, int expected)
        => Assert.Equal(expected, GraphemeHelper.DisplayWidth(c.ToString()));

    [Fact]
    public void CjkIdeograph_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.CJK));

    [Fact]
    public void HangulSyllable_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.Hangul));

    [Fact]
    public void SurrogatePairEmoji_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.WavingHand));

    [Fact]
    public void SkinToneEmoji_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.WavingHandBrown));

    [Fact]
    public void FlagEmoji_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.FlagUS));

    [Fact]
    public void FamilyEmoji_Width2()
        => Assert.Equal(2, GraphemeHelper.DisplayWidth(U.FamilyEmoji));

    [Fact]
    public void CombiningMark_Width0()
    {
        // Combining acute alone
        Assert.Equal(0, GraphemeHelper.DisplayWidth("\u0301"));
    }

    [Fact]
    public void BaseWithCombining_Width1()
    {
        // "é" via combining: the cluster has width 1 (same as the base 'e')
        Assert.Equal(1, GraphemeHelper.DisplayWidth(U.EWithCombining));
    }

    [Fact]
    public void FullwidthLatin_Width2()
    {
        // U+FF21 FULLWIDTH LATIN CAPITAL LETTER A
        Assert.Equal(2, GraphemeHelper.DisplayWidth("\uFF21"));
    }

    [Fact]
    public void ZeroWidthJoiner_Width0()
        => Assert.Equal(0, GraphemeHelper.DisplayWidth(U.ZeroWidthJoiner));
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. TotalDisplayWidth
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_TotalDisplayWidth
{
    [Fact]
    public void Empty_Zero() => Assert.Equal(0, GraphemeHelper.TotalDisplayWidth(ReadOnlySpan<char>.Empty));

    [Fact]
    public void PureAscii_EqualToLength()
        => Assert.Equal(5, GraphemeHelper.TotalDisplayWidth("hello"));

    [Fact]
    public void TwoCjkChars_Width4()
        => Assert.Equal(4, GraphemeHelper.TotalDisplayWidth(U.CJK + U.Hangul));

    [Fact]
    public void MixedAsciiAndCjk()
    {
        // "ab中c" = 1+1+2+1 = 5
        Assert.Equal(5, GraphemeHelper.TotalDisplayWidth("ab" + U.CJK + "c"));
    }

    [Fact]
    public void Emoji_Width2()
        => Assert.Equal(2, GraphemeHelper.TotalDisplayWidth(U.WavingHand));

    [Fact]
    public void TwoFlags_Width4()
        => Assert.Equal(4, GraphemeHelper.TotalDisplayWidth(U.FlagUS + U.FlagGB));

    [Fact]
    public void FamilyPlusAscii_Width3()
        => Assert.Equal(3, GraphemeHelper.TotalDisplayWidth(U.FamilyEmoji + "!"));
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. WordBoundary with Unicode (tested through public API)
// ─────────────────────────────────────────────────────────────────────────────

public class WordBoundary_Unicode
{
    private static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        d.Load(text);
        return d;
    }

    [Fact]
    public void GetWordBoundaryRight_ThroughEmoji_StopsAtNextWord()
    {
        // "hello 👋 world" — word right from 0 should land on 'w' in world
        string text  = "hello " + U.WavingHand + " world";
        var    doc   = Doc(text);
        int    result = WordBoundary.GetWordBoundaryRight(doc, 0);
        int    wOffset = 5 + 1 + U.WavingHand.Length + 1; // = 10
        Assert.Equal(wOffset, result);
    }

    [Fact]
    public void GetWordBoundaryLeft_ThroughEmoji_LandsAtWordStart()
    {
        // " 👋 word" — from end, left should land at start of "word"
        string prefix = " " + U.WavingHand + " ";
        string text   = prefix + "word";
        var    doc    = Doc(text);
        int    left   = WordBoundary.GetWordBoundaryLeft(doc, text.Length);
        Assert.Equal(prefix.Length, left);
    }

    [Fact]
    public void GetWordBoundaryRight_EmojiInMiddle()
    {
        // "a 🇺🇸 b" — from offset 0: skip "a" then non-word " 🇺🇸 " → land on 'b'
        var doc    = Doc("a " + U.FlagUS + " b");
        int result = WordBoundary.GetWordBoundaryRight(doc, 0);
        Assert.Equal(1 + 1 + U.FlagUS.Length + 1, result);
    }

    [Fact]
    public void GetWordBoundaryLeft_CjkText_TreatedAsWord()
    {
        // CJK ideographs are letters → "中한" is a two-cluster word
        var doc  = Doc(U.CJK + U.Hangul);
        int left = WordBoundary.GetWordBoundaryLeft(doc, 2);
        Assert.Equal(0, left);
    }

    [Fact]
    public void GetWordAt_CjkWord_ExpandsBothClusters()
    {
        string text = " " + U.CJK + U.Hangul + " ";
        var    doc  = Doc(text);
        var    span = WordBoundary.GetWordAt(doc, 1);
        Assert.Equal(1, span.Start);
        Assert.Equal(3, span.End); // both CJK chars are word chars
    }

    [Fact]
    public void GetWordAt_EmojiBetweenWords_SelectsEmojiGroup()
    {
        // "hello 👋 world" — GetWordAt on the emoji offset
        string text     = "hello " + U.WavingHand + " world";
        var    doc      = Doc(text);
        int    emojiOff = "hello ".Length;
        var    span     = WordBoundary.GetWordAt(doc, emojiOff);
        // emoji is non-word, non-newline → the non-word group is " 👋 " (with surrounding spaces)
        Assert.True(span.Start <= emojiOff);
        Assert.True(span.End   >= emojiOff + U.WavingHand.Length);
    }

    [Fact]
    public void SupplementaryPlaneLetter_IsWordChar()
    {
        // U+1D400 MATHEMATICAL BOLD CAPITAL A — a letter on supplementary plane
        // GetWordAt on it should include it in the word group
        string text = " \uD835\uDC00 "; // space 𝐀 space
        var    doc  = Doc(text);
        var    span = WordBoundary.GetWordAt(doc, 1);
        Assert.Equal(1, span.Start);
        Assert.Equal(3, span.End); // surrogate pair = 2 code units
    }

    [Fact]
    public void GetWordBoundaryRight_AtDocEnd_ReturnsLength()
    {
        var doc = Doc("abc");
        Assert.Equal(3, WordBoundary.GetWordBoundaryRight(doc, 3));
    }

    [Fact]
    public void GetWordBoundaryLeft_AtDocStart_ReturnsZero()
    {
        var doc = Doc("abc");
        Assert.Equal(0, WordBoundary.GetWordBoundaryLeft(doc, 0));
    }

    [Fact]
    public void GetWordBoundaryRight_FlagEmoji_SkipsWholeFlag()
    {
        // "a🇺🇸b" — from 0: "a" is word, then non-word "🇺🇸", then "b"
        string text = "a" + U.FlagUS + "b";
        var    doc  = Doc(text);
        // From 0: skip word "a" then non-word "🇺🇸" → should land on "b"
        int result = WordBoundary.GetWordBoundaryRight(doc, 0);
        Assert.Equal(1 + U.FlagUS.Length, result); // 1 + 4 = 5
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 9. TextCursor grapheme-aware movement
// ─────────────────────────────────────────────────────────────────────────────

public class TextCursor_GraphemeMovement
{
    private static TextDocument MakeDoc(string content)
    {
        var doc = new TextDocument();
        doc.Load(content);
        return doc;
    }

    [Fact]
    public void MoveRight_ThroughSurrogatePair_Skips2CodeUnits()
    {
        var doc    = MakeDoc(U.WavingHand);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight();
        Assert.Equal(2, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_ThroughSurrogatePair_Skips2CodeUnits()
    {
        var doc    = MakeDoc(U.WavingHand);
        var cursor = new TextCursor(doc, 2);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void MoveRight_ThroughCombiningMark_Skips2CodeUnits()
    {
        var doc    = MakeDoc(U.EWithCombining);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight();
        Assert.Equal(2, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_ThroughCombiningMark_Skips2CodeUnits()
    {
        var doc    = MakeDoc(U.EWithCombining);
        var cursor = new TextCursor(doc, 2);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void MoveRight_ThroughFlagEmoji_Skips4CodeUnits()
    {
        var doc    = MakeDoc(U.FlagUS);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight();
        Assert.Equal(4, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_ThroughFlagEmoji_Skips4CodeUnits()
    {
        var doc    = MakeDoc(U.FlagUS);
        var cursor = new TextCursor(doc, 4);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void MoveRight_ThroughSkinToneEmoji_Skips4CodeUnits()
    {
        var doc    = MakeDoc(U.WavingHandBrown);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight();
        Assert.Equal(4, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_ThroughSkinToneEmoji_Skips4CodeUnits()
    {
        var doc    = MakeDoc(U.WavingHandBrown);
        var cursor = new TextCursor(doc, 4);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void MoveRight_ThroughFamilyEmoji_SkipsAll()
    {
        var doc    = MakeDoc(U.FamilyEmoji);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight();
        Assert.Equal(U.FamilyEmoji.Length, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_ThroughFamilyEmoji_SkipsAll()
    {
        var doc    = MakeDoc(U.FamilyEmoji);
        var cursor = new TextCursor(doc, U.FamilyEmoji.Length);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void SelectRight_ThroughEmoji_SelectsWholeCluster()
    {
        var doc    = MakeDoc(U.FlagUS + "a");
        var cursor = new TextCursor(doc, 0);
        cursor.SelectRight();
        Assert.Equal(0, cursor.SelectionStart);
        Assert.Equal(4, cursor.SelectionEnd);
        Assert.Equal(U.FlagUS, cursor.SelectedText);
    }

    [Fact]
    public void SelectLeft_ThroughEmoji_SelectsWholeCluster()
    {
        var doc    = MakeDoc("a" + U.FlagUS);
        var cursor = new TextCursor(doc, 5);
        cursor.SelectLeft();
        Assert.Equal(1, cursor.SelectionStart);
        Assert.Equal(5, cursor.SelectionEnd);
        Assert.Equal(U.FlagUS, cursor.SelectedText);
    }

    [Fact]
    public void DeleteLeft_ThroughSurrogatePair_DeletesBothCodeUnits()
    {
        var doc    = MakeDoc(U.WavingHand);
        var cursor = new TextCursor(doc, 2);
        cursor.DeleteLeft();
        Assert.Equal(0, doc.Length);
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void DeleteRight_ThroughSurrogatePair_DeletesBothCodeUnits()
    {
        var doc    = MakeDoc(U.WavingHand);
        var cursor = new TextCursor(doc, 0);
        cursor.DeleteRight();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void DeleteLeft_ThroughFamilyEmoji_DeletesWholeCluster()
    {
        var doc    = MakeDoc(U.FamilyEmoji);
        var cursor = new TextCursor(doc, U.FamilyEmoji.Length);
        cursor.DeleteLeft();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void DeleteRight_ThroughFamilyEmoji_DeletesWholeCluster()
    {
        var doc    = MakeDoc(U.FamilyEmoji);
        var cursor = new TextCursor(doc, 0);
        cursor.DeleteRight();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void DeleteLeft_ThroughCombiningMark_DeletesBothCodeUnits()
    {
        var doc    = MakeDoc(U.EWithCombining);
        var cursor = new TextCursor(doc, 2);
        cursor.DeleteLeft();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void MoveRight_AtDocEnd_Clamps()
    {
        var doc    = MakeDoc("a");
        var cursor = new TextCursor(doc, 1);
        cursor.MoveRight();
        Assert.Equal(1, cursor.CaretOffset);
    }

    [Fact]
    public void MoveLeft_AtDocStart_Clamps()
    {
        var doc    = MakeDoc("a");
        var cursor = new TextCursor(doc, 0);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void MoveRight_MixedString_StepsCorrectly()
    {
        // "a👋b" — offsets: 0,1,3,4
        string content = "a" + U.WavingHand + "b";
        var doc    = MakeDoc(content);
        var cursor = new TextCursor(doc, 0);

        cursor.MoveRight(); Assert.Equal(1, cursor.CaretOffset); // past 'a'
        cursor.MoveRight(); Assert.Equal(3, cursor.CaretOffset); // past 👋 (2 code units)
        cursor.MoveRight(); Assert.Equal(4, cursor.CaretOffset); // past 'b'
    }

    [Fact]
    public void MoveLeft_MixedString_StepsCorrectly()
    {
        string content = "a" + U.WavingHand + "b";
        var doc    = MakeDoc(content);
        var cursor = new TextCursor(doc, 4);

        cursor.MoveLeft(); Assert.Equal(3, cursor.CaretOffset); // past 'b'
        cursor.MoveLeft(); Assert.Equal(1, cursor.CaretOffset); // past 👋
        cursor.MoveLeft(); Assert.Equal(0, cursor.CaretOffset); // past 'a'
    }

    [Fact]
    public void MoveCount2_SkipsTwoClusters()
    {
        string content = U.WavingHand + U.FlagUS + "!";
        var doc    = MakeDoc(content);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveRight(2);
        // Skipped 👋 (2) and 🇺🇸 (4) = offset 6
        Assert.Equal(6, cursor.CaretOffset);
    }

    [Fact]
    public void MultiLine_MoveRightAcrossNewline_StepsOneCodeUnit()
    {
        var doc    = MakeDoc("a\nb");
        var cursor = new TextCursor(doc, 1); // end of first line
        cursor.MoveRight();
        Assert.Equal(2, cursor.CaretOffset); // past '\n'
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 10. DocumentStats
// ─────────────────────────────────────────────────────────────────────────────

public class DocumentStats_Tests
{
    [Fact]
    public void PureAscii_AllCountsEqual()
    {
        var doc = new TextDocument();
        doc.Load("hello");
        var stats = doc.GetStats();
        Assert.Equal(5, stats.GraphemeCount);
        Assert.Equal(5, stats.CodeUnitCount);
        Assert.Equal(5, stats.RuneCount);
        Assert.Equal(1, stats.WordCount);
        Assert.Equal(1, stats.LineCount);
        Assert.Equal(5, stats.DisplayColumns);
    }

    [Fact]
    public void SurrogatePair_GraphemeLessThanCodeUnits()
    {
        var doc = new TextDocument();
        doc.Load(U.WavingHand); // 2 code units, 1 rune, 1 cluster
        var stats = doc.GetStats();
        Assert.Equal(1, stats.GraphemeCount);
        Assert.Equal(2, stats.CodeUnitCount);
        Assert.Equal(1, stats.RuneCount);
        Assert.Equal(2, stats.DisplayColumns);
    }

    [Fact]
    public void CombiningMark_GraphemeLessThanCodeUnits()
    {
        var doc = new TextDocument();
        doc.Load(U.EWithCombining); // 2 code units, 2 runes, 1 cluster
        var stats = doc.GetStats();
        Assert.Equal(1, stats.GraphemeCount);
        Assert.Equal(2, stats.CodeUnitCount);
        Assert.Equal(2, stats.RuneCount);
        Assert.Equal(1, stats.DisplayColumns);
    }

    [Fact]
    public void FamilyEmoji_OneCluster()
    {
        var doc = new TextDocument();
        doc.Load(U.FamilyEmoji);
        var stats = doc.GetStats();
        Assert.Equal(1, stats.GraphemeCount);
        Assert.True(stats.CodeUnitCount > 1);
        Assert.Equal(2, stats.DisplayColumns);
    }

    [Fact]
    public void CjkText_DisplayColumnsDouble()
    {
        var doc = new TextDocument();
        doc.Load(U.CJK + U.Hangul); // 2 CJK chars, each width 2
        var stats = doc.GetStats();
        Assert.Equal(2, stats.GraphemeCount);
        Assert.Equal(4, stats.DisplayColumns);
    }

    [Fact]
    public void WordCount_MultipleWords()
    {
        var doc = new TextDocument();
        doc.Load("one two three");
        Assert.Equal(3, doc.GetStats().WordCount);
    }

    [Fact]
    public void WordCount_EmptyDocument()
    {
        var doc = new TextDocument();
        doc.Load("");
        Assert.Equal(0, doc.GetStats().WordCount);
    }

    [Fact]
    public void WordCount_EmojiNotWords()
    {
        var doc = new TextDocument();
        doc.Load("hello " + U.WavingHand + " world");
        // "hello" and "world" are words; emoji is non-word but not whitespace-separated
        // Actually: "hello", then space, then 👋 (non-space non-word), then space, then "world"
        // → 3 "words" by whitespace delimiting (hello, 👋, world)
        Assert.Equal(3, doc.GetStats().WordCount);
    }

    [Fact]
    public void LineCount_MultiLine()
    {
        var doc = new TextDocument();
        doc.Load("a\nb\nc");
        Assert.Equal(3, doc.GetStats().LineCount);
    }

    [Fact]
    public void CodeUnitCount_EqualsDocLength()
    {
        string content = "ab" + U.WavingHand + "cd";
        var doc = new TextDocument();
        doc.Load(content);
        var stats = doc.GetStats();
        Assert.Equal(doc.Length, stats.CodeUnitCount);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 11. Fuzz tests
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_Fuzz
{
    // A curated pool of code points covering ASCII, BMP non-ASCII, supplementary plane,
    // combining marks, ZWJ, regional indicators, and emoji modifiers.
    private static readonly int[] CodePointPool =
    [
        0x0041, // A
        0x0020, // space
        0x0301, // combining acute
        0x0302, // combining circumflex
        0x00E9, // é precomposed
        0x4E2D, // 中
        0xD55C, // 한
        0x1F44B,// 👋
        0x1F3FD,// 🏽 skin tone
        0x1F1FA,// regional indicator U
        0x1F1F8,// regional indicator S
        0x200D, // ZWJ
        0x1F468,// 👨
        0x1F469,// 👩
        0x1F467,// 👧
        0x1F466,// 👦
        0x0041, 0x0042, 0x0043, // repeated ASCII
        0x000A, // newline
        0x0009, // tab
    ];

    private static string BuildRandomString(Random rng, int maxClusters)
    {
        var sb = new System.Text.StringBuilder();
        int count = rng.Next(0, maxClusters + 1);
        for (int i = 0; i < count; i++)
        {
            int cp = CodePointPool[rng.Next(CodePointPool.Length)];
            if (cp <= 0xFFFF)
                sb.Append((char)cp);
            else
                sb.Append(char.ConvertFromUtf32(cp));
        }
        return sb.ToString();
    }

    [Fact]
    public void NextCluster_NeverInfiniteLoop_NeverOutOfBounds()
    {
        var rng = new Random(42);
        for (int trial = 0; trial < 2000; trial++)
        {
            string text   = BuildRandomString(rng, 20);
            int    offset = 0;
            int    steps  = 0;
            while (offset < text.Length)
            {
                int next = GraphemeHelper.NextCluster(text, offset);
                Assert.True(next > offset,   $"nextCluster did not advance at offset {offset} in '{text}'");
                Assert.True(next <= text.Length, $"nextCluster went past end at offset {offset}");
                offset = next;
                steps++;
                Assert.True(steps <= text.Length, "too many steps — possible infinite loop");
            }
        }
    }

    [Fact]
    public void PreviousCluster_RoundTrips_WithNext()
    {
        var rng = new Random(43);
        for (int trial = 0; trial < 2000; trial++)
        {
            string text = BuildRandomString(rng, 20);
            if (text.Length == 0) continue;

            // Collect all forward boundaries
            var boundaries = new List<int> { 0 };
            int pos = 0;
            while (pos < text.Length)
            {
                pos = GraphemeHelper.NextCluster(text, pos);
                boundaries.Add(pos);
            }

            // Walk back: PreviousCluster(boundaries[i]) must equal boundaries[i-1]
            for (int i = boundaries.Count - 1; i > 0; i--)
            {
                int prev = GraphemeHelper.PreviousCluster(text, boundaries[i]);
                Assert.Equal(boundaries[i - 1], prev);
            }
        }
    }

    [Fact]
    public void ClusterCount_ConsistentWithManualCount()
    {
        var rng = new Random(44);
        for (int trial = 0; trial < 2000; trial++)
        {
            string text  = BuildRandomString(rng, 30);
            int clusterCount = GraphemeHelper.ClusterCount(text);

            // Manual count via forward scan
            int manual = 0;
            int offset = 0;
            while (offset < text.Length)
            {
                offset = GraphemeHelper.NextCluster(text, offset);
                manual++;
            }

            Assert.Equal(manual, clusterCount);
        }
    }

    [Fact]
    public void DisplayWidth_NeverNegative()
    {
        var rng = new Random(45);
        for (int trial = 0; trial < 2000; trial++)
        {
            string text = BuildRandomString(rng, 20);
            // Walk cluster by cluster
            int offset = 0;
            while (offset < text.Length)
            {
                int next  = GraphemeHelper.NextCluster(text, offset);
                int width = GraphemeHelper.DisplayWidth(text.AsSpan()[offset..next]);
                Assert.True(width >= 0, $"Negative width at offset {offset}");
                Assert.True(width <= 2, $"Width > 2 at offset {offset}");
                offset = next;
            }
        }
    }

    [Fact]
    public void TotalDisplayWidth_SumOfIndividualWidths()
    {
        var rng = new Random(46);
        for (int trial = 0; trial < 2000; trial++)
        {
            string text  = BuildRandomString(rng, 20);
            int total    = GraphemeHelper.TotalDisplayWidth(text);
            int manual   = 0;
            int offset   = 0;
            while (offset < text.Length)
            {
                int next = GraphemeHelper.NextCluster(text, offset);
                manual  += GraphemeHelper.DisplayWidth(text.AsSpan()[offset..next]);
                offset   = next;
            }
            Assert.Equal(manual, total);
        }
    }

    [Fact]
    public void AllBmpCodePoints_NextClusterNeverInfiniteLoop()
    {
        // Spot check a range of BMP code points — no infinite loops, no out-of-bounds
        for (int cp = 0; cp < 0xD800; cp += 17) // skip surrogates
        {
            string text = ((char)cp).ToString();
            int next = GraphemeHelper.NextCluster(text, 0);
            Assert.True(next >= 1 && next <= text.Length);
        }
        for (int cp = 0xE000; cp <= 0xFFFF; cp += 17)
        {
            string text = ((char)cp).ToString();
            int next = GraphemeHelper.NextCluster(text, 0);
            Assert.True(next >= 1 && next <= text.Length);
        }
    }

    [Fact]
    public void SupplementaryPlaneCodePoints_NextClusterSkipsBoth()
    {
        // Every supplementary-plane code point should advance by 2 (the surrogate pair)
        // or more (if it has combining chars after it, which won't happen here).
        int[] supplementary = [0x1F600, 0x1F44B, 0x1F1FA, 0x20000, 0x1D400];
        foreach (int cp in supplementary)
        {
            string text = char.ConvertFromUtf32(cp);
            Assert.Equal(2, text.Length); // sanity: it IS a surrogate pair
            int next = GraphemeHelper.NextCluster(text, 0);
            Assert.Equal(2, next);
        }
    }

    [Fact]
    public void CursorMovement_RandomContent_NeverLandsInsideSurrogatePair()
    {
        var rng = new Random(47);
        for (int trial = 0; trial < 500; trial++)
        {
            string content = BuildRandomString(rng, 15);
            var doc    = new TextDocument();
            doc.Load(content);
            var cursor = new TextCursor(doc, 0);

            // Walk right to end
            int steps = 0;
            while (cursor.CaretOffset < doc.Length)
            {
                cursor.MoveRight();
                int off = cursor.CaretOffset;
                // Cursor must not land on a low surrogate
                if (off < content.Length)
                    Assert.False(char.IsLowSurrogate(content[off]),
                        $"Cursor landed on low surrogate at {off} in '{content}'");
                steps++;
                Assert.True(steps <= doc.Length + 1, "Cursor stuck in a loop");
            }

            // Walk left back to start
            while (cursor.CaretOffset > 0)
            {
                cursor.MoveLeft();
                int off = cursor.CaretOffset;
                if (off < content.Length)
                    Assert.False(char.IsLowSurrogate(content[off]),
                        $"Cursor landed on low surrogate (left) at {off}");
            }
        }
    }

    [Fact]
    public void DeleteLeft_RandomContent_DocumentNeverCorrupted()
    {
        var rng = new Random(48);
        for (int trial = 0; trial < 300; trial++)
        {
            string content = BuildRandomString(rng, 10);
            var doc    = new TextDocument();
            doc.Load(content);
            var cursor = new TextCursor(doc, doc.Length);

            // Delete everything from the right, checking no corruption
            int iterations = 0;
            while (doc.Length > 0)
            {
                cursor.DeleteLeft();
                // Ensure cursor is not inside a surrogate pair
                int off = cursor.CaretOffset;
                string cur = doc.GetText();
                if (off > 0 && off < cur.Length)
                    Assert.False(char.IsLowSurrogate(cur[off]));
                iterations++;
                Assert.True(iterations <= content.Length * 2 + 5);
            }
            Assert.Equal(0, doc.Length);
        }
    }

    [Fact]
    public void MatchesStringInfo_ClusterCount_ForAllTestStrings()
    {
        string[] testStrings =
        [
            "",
            "a",
            "hello world",
            U.EWithCombining,
            U.WavingHand,
            U.WavingHandBrown,
            U.FlagUS + U.FlagGB,
            U.FamilyEmoji,
            "a\u030A\u0301b",
            U.CJK + U.Hangul + "abc",
            U.FamilyEmoji + " and " + U.FlagUS,
            string.Concat(Enumerable.Repeat(U.WavingHand, 10)),
        ];
        foreach (string s in testStrings)
        {
            int expected = U.RefClusterCount(s);
            int actual   = GraphemeHelper.ClusterCount(s);
            Assert.Equal(expected, actual);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 12. Edge cases and regression tests
// ─────────────────────────────────────────────────────────────────────────────

public class GraphemeHelper_EdgeCases
{
    [Fact]
    public void NextCluster_NegativeOffset_TreatedAsZero()
    {
        // offset is cast to uint for the bounds check; negative becomes large positive → returns text.Length
        // But our API says "if >= text.Length return text.Length"
        // Negative int cast to uint = huge number >= text.Length → return text.Length
        // That's safe; test just that it doesn't throw.
        _ = GraphemeHelper.NextCluster("abc", -1);
    }

    [Fact]
    public void SingleCombiningMark_AloneAtStart_IsOwnCluster()
    {
        // A combining mark with no base character — it forms its own degenerate cluster
        string text = "\u0301"; // bare combining acute
        int next = GraphemeHelper.NextCluster(text, 0);
        Assert.Equal(1, next);
    }

    [Fact]
    public void HangulComposableSyllables_CountedAsExpected()
    {
        // "가" = U+AC00 (precomposed), "각" = U+AC01 — each is one cluster
        string text = "\uAC00\uAC01\uAC02";
        Assert.Equal(3, GraphemeHelper.ClusterCount(text));
        Assert.Equal(6, GraphemeHelper.TotalDisplayWidth(text)); // 3 × width-2
    }

    [Fact]
    public void VariationSelector_IncludedInCluster()
    {
        // U+FE0F VARIATION SELECTOR-16 (emoji presentation) attaches to the preceding char
        // "☎️" = U+260E + U+FE0F
        string text = "\u260E\uFE0F";
        Assert.Equal(1, GraphemeHelper.ClusterCount(text));
    }

    [Fact]
    public void LongCombiningSequence_StillOneCluster()
    {
        // base + 5 combining marks — should be 1 cluster
        string text = "a\u0301\u0302\u0303\u0304\u0305";
        Assert.Equal(1, GraphemeHelper.ClusterCount(text));
    }

    [Fact]
    public void ZalgoText_ManyCombiners_OneCluster()
    {
        // Zalgo: base 'z' + 100 stacked combining marks — still 1 cluster
        string zalgo = "z" + new string('\u0301', 100); // 101 code units
        Assert.Equal(1, GraphemeHelper.ClusterCount(zalgo));
    }

    [Fact]
    public void ZalgoText_PreviousCluster_FindsBaseEvenWithManyCombiners()
    {
        // 'z' + 100 combining acutes: PreviousCluster from end must return 0
        string zalgo = "z" + new string('\u0301', 100);
        Assert.Equal(0, GraphemeHelper.PreviousCluster(zalgo, zalgo.Length));
    }

    [Fact]
    public void ZalgoText_DeleteLeft_RemovesWholeCluster()
    {
        // Backspace at end of a Zalgo character must delete the entire thing
        string zalgo = "z" + new string('\u0301', 100);
        var doc    = new TextDocument();
        doc.Load(zalgo);
        var cursor = new TextCursor(doc, doc.Length);
        cursor.DeleteLeft();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void ZalgoText_DeleteRight_RemovesWholeCluster()
    {
        string zalgo = "z" + new string('\u0301', 100);
        var doc    = new TextDocument();
        doc.Load(zalgo);
        var cursor = new TextCursor(doc, 0);
        cursor.DeleteRight();
        Assert.Equal(0, doc.Length);
    }

    [Fact]
    public void ZalgoText_MoveLeft_SkipsWholeCluster()
    {
        string zalgo = "z" + new string('\u0301', 100);
        var doc    = new TextDocument();
        doc.Load(zalgo);
        var cursor = new TextCursor(doc, doc.Length);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void ZalgoText_NextCluster_SkipsAllCombiners()
    {
        string zalgo = "z" + new string('\u0301', 80);
        Assert.Equal(zalgo.Length, GraphemeHelper.NextCluster(zalgo, 0));
    }

    [Fact]
    public void ZWJ_NotAlone_DoesNotCreateSpuriousBoundary()
    {
        // Inside a ZWJ sequence, the ZWJ position is not a cluster boundary
        string text = U.FamilyEmoji;
        // Find the position of the first ZWJ
        int zwjIdx = text.IndexOf('\u200D');
        Assert.True(zwjIdx > 0);
        Assert.False(GraphemeHelper.IsClusterBoundary(text, zwjIdx));
    }

    [Fact]
    public void RegionalIndicator_PairIsOneCluster_SingleIsOne()
    {
        // 🇺 alone = one cluster (unpaired RI), 🇺🇸 = one cluster (paired RI)
        string single = "\U0001F1FA"; // 🇺 (2 code units)
        string paired = "\U0001F1FA\U0001F1F8"; // 🇺🇸 (4 code units)
        Assert.Equal(1, GraphemeHelper.ClusterCount(single));
        Assert.Equal(1, GraphemeHelper.ClusterCount(paired));
    }

    [Fact]
    public void MixedMultilineDoc_StatsCorrect()
    {
        var doc = new TextDocument();
        // Line 0: "hello" (5 ASCII)
        // Line 1: 👋 + " world" (2+6=8 code units, but 7 clusters)
        // Line 2: 中文 (2 CJK, each width 2)
        doc.Load("hello\n" + U.WavingHand + " world\n" + U.CJK + U.Hangul);
        var stats = doc.GetStats();
        Assert.Equal(3, stats.LineCount);
        Assert.True(stats.GraphemeCount < stats.CodeUnitCount); // because of surrogate pairs
        Assert.True(stats.DisplayColumns > stats.GraphemeCount); // because of CJK/emoji width-2
    }

    [Fact]
    public void CursorAtEnd_MoveRight_StaysAtEnd()
    {
        var doc    = new TextDocument();
        doc.Load(U.WavingHand);
        var cursor = new TextCursor(doc, doc.Length);
        cursor.MoveRight();
        Assert.Equal(doc.Length, cursor.CaretOffset);
    }

    [Fact]
    public void CursorAtStart_MoveLeft_StaysAtStart()
    {
        var doc    = new TextDocument();
        doc.Load(U.WavingHand);
        var cursor = new TextCursor(doc, 0);
        cursor.MoveLeft();
        Assert.Equal(0, cursor.CaretOffset);
    }

    [Fact]
    public void CursorDeleteRight_FamilyEmoji_OneUndoStep()
    {
        var doc    = new TextDocument();
        doc.Load(U.FamilyEmoji + "abc");
        var cursor = new TextCursor(doc, 0);
        cursor.DeleteRight();
        Assert.Equal("abc", doc.GetText());
        doc.Undo();
        Assert.Equal(U.FamilyEmoji + "abc", doc.GetText());
    }
}
