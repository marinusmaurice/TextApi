using TextAPI.Core;
using TextAPI.Core.Search;
using Xunit;

namespace TextAPI.Tests;

public class RegexCaptureTests
{
    private static TextDocument Make(string content)
    {
        var doc = new TextDocument();
        doc.Load(content);
        return doc;
    }

    [Fact]
    public void ReplaceAll_Regex_SingleCaptureGroup()
    {
        var doc = Make("foo bar baz");
        int n = doc.ReplaceAll(@"(\w+)", "[$1]",
            new SearchOptions { UseRegex = true });
        Assert.Equal(3, n);
        Assert.Equal("[foo] [bar] [baz]", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_MultipleGroups_SwapOrder()
    {
        var doc = Make("John Smith");
        int n = doc.ReplaceAll(@"(\w+) (\w+)", "$2, $1",
            new SearchOptions { UseRegex = true });
        Assert.Equal(1, n);
        Assert.Equal("Smith, John", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_NoCaptureGroups_StillWorks()
    {
        var doc = Make("aaa bbb");
        int n = doc.ReplaceAll(@"\w+", "X",
            new SearchOptions { UseRegex = true });
        Assert.Equal(2, n);
        Assert.Equal("X X", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_CaptureGroup_IsUndoable()
    {
        var doc = Make("hello world");
        doc.ReplaceAll(@"(\w+)", "[$1]", new SearchOptions { UseRegex = true });
        doc.Undo();
        Assert.Equal("hello world", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_EmptyPattern_ReturnsZero()
    {
        var doc = Make("hello");
        int n = doc.ReplaceAll("", "x", new SearchOptions { UseRegex = true });
        Assert.Equal(0, n);
        Assert.Equal("hello", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_NoMatches_ReturnsZero()
    {
        var doc = Make("hello");
        int n = doc.ReplaceAll("xyz", "Q", new SearchOptions { UseRegex = true });
        Assert.Equal(0, n);
        Assert.Equal("hello", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_CaseInsensitive_WithGroup()
    {
        var doc = Make("Hello HELLO hello");
        int n = doc.ReplaceAll(@"(hello)", "<$1>",
            new SearchOptions { UseRegex = true, CaseSensitive = false });
        Assert.Equal(3, n);
        Assert.Equal("<Hello> <HELLO> <hello>", doc.GetText());
    }

    [Fact]
    public void ReplaceAll_Regex_MultiLine_AllReplaced()
    {
        var doc = Make("John Smith\nJane Doe");
        doc.ReplaceAll(@"(\w+) (\w+)", "$2, $1",
            new SearchOptions { UseRegex = true });
        Assert.Equal("Smith, John\nDoe, Jane", doc.GetText());
    }
}
