using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Diff;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class D
{
    public static DiffResult Of(string[] old, string[] @new, DiffOptions? opts = null)
        => TextDiff.Diff(old, @new, opts);

    public static DiffResult Text(string old, string @new, DiffOptions? opts = null)
        => TextDiff.Diff(old, @new, opts);

    // Extract just the (Kind, OldCount, NewCount) tuples for quick assertion
    public static IEnumerable<(DiffKind K, int OC, int NC)> Shape(DiffResult r)
        => r.Hunks.Select(h => (h.Kind, h.OldCount, h.NewCount));
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class DiffEdgeCaseTests
{
    [Fact] public void BothEmpty_NoChanges()
    {
        var r = D.Of([], []);
        r.HasChanges.Should().BeFalse();
        r.Hunks.Should().BeEmpty();
    }

    [Fact] public void OldEmpty_PureInsert()
    {
        var r = D.Of([], ["a", "b"]);
        r.HasChanges.Should().BeTrue();
        r.AddedLines.Should().Be(2);
        r.DeletedLines.Should().Be(0);
        r.Hunks.Should().ContainSingle(h => h.Kind == DiffKind.Insert && h.NewCount == 2);
    }

    [Fact] public void NewEmpty_PureDelete()
    {
        var r = D.Of(["a", "b"], []);
        r.AddedLines.Should().Be(0);
        r.DeletedLines.Should().Be(2);
        r.Hunks.Should().ContainSingle(h => h.Kind == DiffKind.Delete && h.OldCount == 2);
    }

    [Fact] public void Identical_NoChanges()
    {
        var r = D.Of(["a", "b", "c"], ["a", "b", "c"]);
        r.HasChanges.Should().BeFalse();
        r.Hunks.Should().ContainSingle(h => h.Kind == DiffKind.Equal && h.OldCount == 3);
    }

    [Fact] public void SingleLine_Changed()
    {
        var r = D.Of(["hello"], ["world"]);
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(1);
    }

    [Fact] public void SingleLine_Same()
    {
        var r = D.Of(["hello"], ["hello"]);
        r.HasChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. Core diff shapes
// ═══════════════════════════════════════════════════════════════════════════

public class DiffShapeTests
{
    [Fact] public void MiddleLineChanged()
    {
        // a b c → a X c
        var r = D.Of(["a", "b", "c"], ["a", "X", "c"]);
        D.Shape(r).Should().BeEquivalentTo(new[]
        {
            (DiffKind.Equal,  1, 1),
            (DiffKind.Delete, 1, 0),
            (DiffKind.Insert, 0, 1),
            (DiffKind.Equal,  1, 1),
        }, o => o.WithStrictOrdering());
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(1);
    }

    [Fact] public void LineAppended()
    {
        var r = D.Of(["a", "b"], ["a", "b", "c"]);
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(0);
        r.Hunks.Last().Kind.Should().Be(DiffKind.Insert);
        r.Hunks.Last().Lines.Should().ContainSingle("c");
    }

    [Fact] public void LinePrepended()
    {
        var r = D.Of(["a", "b"], ["z", "a", "b"]);
        r.AddedLines.Should().Be(1);
        r.Hunks.First().Kind.Should().Be(DiffKind.Insert);
        r.Hunks.First().Lines.Should().ContainSingle("z");
    }

    [Fact] public void MultipleInserts()
    {
        var r = D.Of(["a", "b"], ["a", "X", "Y", "b"]);
        r.AddedLines.Should().Be(2);
        r.DeletedLines.Should().Be(0);
    }

    [Fact] public void MultipleDeletes()
    {
        var r = D.Of(["a", "X", "Y", "b"], ["a", "b"]);
        r.AddedLines.Should().Be(0);
        r.DeletedLines.Should().Be(2);
    }

    [Fact] public void CompleteReplacement()
    {
        var r = D.Of(["a", "b"], ["c", "d"]);
        r.AddedLines.Should().Be(2);
        r.DeletedLines.Should().Be(2);
    }

    [Fact] public void MultipleNonAdjacentChanges()
    {
        // change line 1 and line 5
        var old = new[] { "a", "b", "c", "d", "e", "f" };
        var @new = new[] { "a", "X", "c", "d", "Y", "f" };
        var r = D.Of(old, @new);
        r.AddedLines.Should().Be(2);
        r.DeletedLines.Should().Be(2);
        r.Hunks.Count(h => h.Kind == DiffKind.Delete).Should().Be(2);
        r.Hunks.Count(h => h.Kind == DiffKind.Insert).Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. Line content correctness
// ═══════════════════════════════════════════════════════════════════════════

public class DiffContentTests
{
    [Fact] public void DeleteHunk_ContainsOldLines()
    {
        var r = D.Of(["a", "b", "c"], ["a", "c"]);
        var del = r.Hunks.Single(h => h.Kind == DiffKind.Delete);
        del.Lines.Should().ContainSingle("b");
    }

    [Fact] public void InsertHunk_ContainsNewLines()
    {
        var r = D.Of(["a", "c"], ["a", "b", "c"]);
        var ins = r.Hunks.Single(h => h.Kind == DiffKind.Insert);
        ins.Lines.Should().ContainSingle("b");
    }

    [Fact] public void EqualHunk_ContainsSharedLines()
    {
        var r = D.Of(["a", "b"], ["a", "X"]);
        var eq = r.Hunks.First(h => h.Kind == DiffKind.Equal);
        eq.Lines.Should().ContainSingle("a");
    }

    [Fact] public void HunkOffsets_AreCorrect()
    {
        // old: a(0) b(1) c(2)
        // new: a(0) X(1) c(2)
        var r = D.Of(["a", "b", "c"], ["a", "X", "c"]);
        var del = r.Hunks.Single(h => h.Kind == DiffKind.Delete);
        var ins = r.Hunks.Single(h => h.Kind == DiffKind.Insert);
        del.OldStart.Should().Be(1);
        del.OldCount.Should().Be(1);
        ins.NewStart.Should().Be(1);
        ins.NewCount.Should().Be(1);
    }

    [Fact] public void Equal_OldAndNewOffsetsMatch()
    {
        var r = D.Of(["a", "b", "c"], ["a", "b", "c"]);
        var eq = r.Hunks.Single();
        eq.OldStart.Should().Be(0);
        eq.NewStart.Should().Be(0);
        eq.OldCount.Should().Be(3);
        eq.NewCount.Should().Be(3);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. Options — IgnoreCase and IgnoreWhitespace
// ═══════════════════════════════════════════════════════════════════════════

public class DiffOptionsTests
{
    [Fact] public void IgnoreCase_SameContent_NoChanges()
    {
        var r = D.Of(["Hello", "World"], ["hello", "WORLD"],
                     new DiffOptions { IgnoreCase = true });
        r.HasChanges.Should().BeFalse();
    }

    [Fact] public void IgnoreCase_Default_DifferentCase_IsChange()
    {
        var r = D.Of(["Hello"], ["hello"]);
        r.HasChanges.Should().BeTrue();
    }

    [Fact] public void IgnoreWhitespace_LeadingTrailing_NoChanges()
    {
        var r = D.Of(["  hello  "], ["hello"],
                     new DiffOptions { IgnoreWhitespace = true });
        r.HasChanges.Should().BeFalse();
    }

    [Fact] public void IgnoreWhitespace_InternalRuns_NoChanges()
    {
        var r = D.Of(["a   b   c"], ["a b c"],
                     new DiffOptions { IgnoreWhitespace = true });
        r.HasChanges.Should().BeFalse();
    }

    [Fact] public void IgnoreWhitespace_Default_IsChange()
    {
        var r = D.Of(["  hello  "], ["hello"]);
        r.HasChanges.Should().BeTrue();
    }

    [Fact] public void BothOptions_Combined()
    {
        var r = D.Of(["  HELLO  "], ["hello"],
                     new DiffOptions { IgnoreCase = true, IgnoreWhitespace = true });
        r.HasChanges.Should().BeFalse();
    }

    [Fact] public void MaxEditDistance_Exceeded_ReturnsCoaprseResult()
    {
        var r = D.Of(["a", "b", "c"], ["X", "Y", "Z"],
                     new DiffOptions { MaxEditDistance = 2 });
        // All 3 old lines differ from all 3 new lines — D=6, exceeds limit=2.
        // Result should be a coarse Delete+Insert, not a detailed diff.
        r.Hunks.Should().HaveCount(2);
        r.Hunks[0].Kind.Should().Be(DiffKind.Delete);
        r.Hunks[1].Kind.Should().Be(DiffKind.Insert);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. String overload and TextDocument overload
// ═══════════════════════════════════════════════════════════════════════════

public class DiffInputOverloadTests
{
    [Fact] public void StringOverload_SplitsOnLF()
    {
        var r = D.Text("a\nb\nc", "a\nX\nc");
        r.HasChanges.Should().BeTrue();
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(1);
    }

    [Fact] public void StringOverload_NormalisesCRLF()
    {
        var r = D.Text("a\r\nb", "a\nb");
        r.HasChanges.Should().BeFalse();
    }

    [Fact] public void TextDocument_Overload_Works()
    {
        var old = new TextDocument();
        old.Load("int x;\nint y;");
        var @new = new TextDocument();
        @new.Load("int x;\nint z;");

        var r = TextDiff.Diff(old, @new);
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(1);
    }

    [Fact] public void TextDocument_Identical_NoChanges()
    {
        var old = new TextDocument();
        old.Load("hello\nworld");
        var @new = new TextDocument();
        @new.Load("hello\nworld");
        TextDiff.Diff(old, @new).HasChanges.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. DiffChars — character-level diff
// ═══════════════════════════════════════════════════════════════════════════

public class DiffCharsTests
{
    [Fact] public void Identical_SingleEqualSpan()
    {
        var r = TextDiff.DiffChars("hello", "hello");
        r.Should().ContainSingle(s => s.Kind == DiffKind.Equal && s.Text == "hello");
    }

    [Fact] public void AllReplaced_DeleteThenInsert()
    {
        var r = TextDiff.DiffChars("abc", "xyz");
        r.Any(s => s.Kind == DiffKind.Delete).Should().BeTrue();
        r.Any(s => s.Kind == DiffKind.Insert).Should().BeTrue();
        string rebuilt = Reconstruct(r);
        rebuilt.Should().Be("xyz");
    }

    [Fact] public void MiddleChanged_CorrectSpans()
    {
        // "hello world" → "hello there"
        var r = TextDiff.DiffChars("hello world", "hello there");
        // "hello " should be equal
        r.Should().Contain(s => s.Kind == DiffKind.Equal && s.Text == "hello ");
        string rebuilt = Reconstruct(r);
        rebuilt.Should().Be("hello there");
    }

    [Fact] public void OldEmpty_PureInsert()
    {
        var r = TextDiff.DiffChars("", "abc");
        r.Should().ContainSingle(s => s.Kind == DiffKind.Insert && s.Text == "abc");
    }

    [Fact] public void NewEmpty_PureDelete()
    {
        var r = TextDiff.DiffChars("abc", "");
        r.Should().ContainSingle(s => s.Kind == DiffKind.Delete && s.Text == "abc");
    }

    [Fact] public void Reconstruct_AlwaysProducesNewText()
    {
        (string, string)[] pairs =
        [
            ("foo bar baz", "foo BAZ baz"),
            ("abcdef", "ace"),
            ("", "xyz"),
            ("xyz", ""),
            ("same", "same"),
        ];
        foreach (var (a, b) in pairs)
        {
            var spans = TextDiff.DiffChars(a, b);
            Reconstruct(spans).Should().Be(b, because: $"'{a}' → '{b}'");
        }
    }

    // Rebuild the new text from the spans (equal + insert, skip delete).
    private static string Reconstruct(IReadOnlyList<DiffSpan> spans)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in spans)
            if (s.Kind != DiffKind.Delete) sb.Append(s.Text);
        return sb.ToString();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. ToUnifiedDiff formatting
// ═══════════════════════════════════════════════════════════════════════════

public class UnifiedDiffTests
{
    [Fact] public void Identical_EmptyString()
    {
        D.Text("a\nb", "a\nb").ToUnifiedDiff().Should().BeEmpty();
    }

    [Fact] public void Headers_ArePresent()
    {
        string u = D.Text("a", "b").ToUnifiedDiff("old.cs", "new.cs");
        u.Should().StartWith("--- old.cs\n+++ new.cs\n");
    }

    [Fact] public void HunkHeader_Format()
    {
        // Single-line change in a 3-line file
        string u = D.Of(["a", "b", "c"], ["a", "X", "c"]).ToUnifiedDiff();
        u.Should().Contain("@@ -");
        u.Should().Contain(" @@");
    }

    [Fact] public void DeletedLine_HasMinusPrefix()
    {
        string u = D.Of(["a", "b", "c"], ["a", "c"]).ToUnifiedDiff();
        u.Should().Contain("-b");
    }

    [Fact] public void InsertedLine_HasPlusPrefix()
    {
        string u = D.Of(["a", "c"], ["a", "b", "c"]).ToUnifiedDiff();
        u.Should().Contain("+b");
    }

    [Fact] public void ContextLine_HasSpacePrefix()
    {
        string u = D.Of(["a", "b", "c"], ["a", "X", "c"]).ToUnifiedDiff(contextLines: 1);
        u.Should().Contain(" a");
        u.Should().Contain(" c");
    }

    [Fact] public void ZeroContext_OnlyChangedLines()
    {
        string u = D.Of(["a", "b", "c"], ["a", "X", "c"]).ToUnifiedDiff(contextLines: 0);
        // Content lines (not headers) must all start with '-' or '+'
        var contentLines = u.Split('\n')
            .Where(l => l.Length > 0 && !l.StartsWith("---") && !l.StartsWith("+++") && !l.StartsWith("@@"))
            .ToList();
        contentLines.Should().AllSatisfy(l => (l[0] == '-' || l[0] == '+').Should().BeTrue());
        u.Should().Contain("-b");
        u.Should().Contain("+X");
    }

    [Fact] public void TwoDistantChanges_TwoHunkHeaders()
    {
        // Two changes far apart — should produce two @@ sections
        var old = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
        var @new = new[] { "1", "X", "3", "4", "5", "6", "7", "Y", "9", "10" };
        string u = D.Of(old, @new).ToUnifiedDiff(contextLines: 1);
        int count = 0;
        int pos = 0;
        while ((pos = u.IndexOf("@@ -", pos)) >= 0) { count++; pos++; }
        count.Should().Be(2);
    }

    [Fact] public void TwoCloseChanges_OneHunkHeader()
    {
        // Two changes only 2 lines apart with contextLines=3 — should merge
        var old = new[] { "1", "X", "3", "4", "Y", "6" };
        var @new = new[] { "1", "a", "3", "4", "b", "6" };
        string u = D.Of(old, @new).ToUnifiedDiff(contextLines: 3);
        int count = 0;
        int pos = 0;
        while ((pos = u.IndexOf("@@ -", pos)) >= 0) { count++; pos++; }
        count.Should().Be(1);
    }

    [Fact] public void NewFile_OldRangeIsZero()
    {
        // New file: old is empty, new has content
        string u = D.Of([], ["line1", "line2"]).ToUnifiedDiff();
        u.Should().Contain("-0,0");
    }

    [Fact] public void Reconstruct_FromUnifiedDiff_ProducesNewContent()
    {
        // Apply the +/- lines from the diff to verify the result equals newLines
        string[] old = ["int x;", "int y;", "return x + y;"];
        string[] @new = ["int x;", "int z;", "return x + z;"];
        string u = D.Of(old, @new).ToUnifiedDiff(contextLines: 0);
        var addedLines = u.Split('\n')
            .Where(l => l.StartsWith('+') && !l.StartsWith("+++"))
            .Select(l => l[1..])
            .ToList();
        addedLines.Should().ContainInOrder("int z;", "return x + z;");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. Myers correctness — round-trip and invariants
// ═══════════════════════════════════════════════════════════════════════════

public class DiffCorrectnessTests
{
    [Theory]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "x", "c" })]
    [InlineData(new[] { "a" }, new[] { "b" })]
    [InlineData(new[] { "x", "y" }, new[] { "x", "y", "z" })]
    [InlineData(new[] { "x", "y", "z" }, new[] { "x", "y" })]
    [InlineData(new string[0], new[] { "a" })]
    [InlineData(new[] { "a" }, new string[0])]
    public void Apply_ProducesNewSequence(string[] old, string[] @new)
    {
        var r = D.Of(old, @new);
        Apply(r, old).Should().BeEquivalentTo(@new, o => o.WithStrictOrdering());
    }

    [Fact] public void LargeFile_NoStackOverflow()
    {
        // 5 000-line files with a small diff
        string[] old = Enumerable.Range(0, 5000).Select(i => $"line{i}").ToArray();
        string[] @new = [..old[..2500], "INSERTED", ..old[2500..]];
        var r = D.Of(old, @new);
        r.AddedLines.Should().Be(1);
        r.DeletedLines.Should().Be(0);
    }

    [Fact] public void HunkOffsets_CoverEntireOldDocument()
    {
        string[] old = ["a", "b", "c", "d", "e"];
        string[] @new = ["a", "X", "c", "Y", "e"];
        var r = D.Of(old, @new);

        // Every old line index must appear in exactly one hunk
        var covered = new HashSet<int>();
        foreach (var h in r.Hunks)
            if (h.Kind != DiffKind.Insert)
                for (int i = h.OldStart; i < h.OldStart + h.OldCount; i++)
                    covered.Add(i);

        covered.Should().BeEquivalentTo(Enumerable.Range(0, old.Length));
    }

    [Fact] public void HunkOffsets_CoverEntireNewDocument()
    {
        string[] old = ["a", "b", "c", "d", "e"];
        string[] @new = ["a", "X", "c", "Y", "e"];
        var r = D.Of(old, @new);

        var covered = new HashSet<int>();
        foreach (var h in r.Hunks)
            if (h.Kind != DiffKind.Delete)
                for (int i = h.NewStart; i < h.NewStart + h.NewCount; i++)
                    covered.Add(i);

        covered.Should().BeEquivalentTo(Enumerable.Range(0, @new.Length));
    }

    [Fact] public void AddedPlusDeleted_IsMinimal()
    {
        // Myers guarantees the shortest edit script.
        // "a b c" → "a x c"  :  SES = delete b, insert x  → D=2
        var r = D.Of(["a", "b", "c"], ["a", "x", "c"]);
        (r.AddedLines + r.DeletedLines).Should().Be(2);
    }

    // Apply a DiffResult to oldLines to produce newLines.
    private static string[] Apply(DiffResult r, string[] old)
    {
        var result = new List<string>();
        foreach (var h in r.Hunks)
        {
            if (h.Kind == DiffKind.Equal || h.Kind == DiffKind.Insert)
                result.AddRange(h.Lines);
        }
        return [..result];
    }
}
