using TextAPI.Core;
using TextAPI.Core.Cursor;
using TextAPI.Repl;
using Xunit;

namespace TextAPI.Tests;

public class PluginRegistryTests
{
    private static string TempPlugin(string name, string description, string tags, string body = "")
    {
        string path = Path.ChangeExtension(Path.GetTempFileName(), ".csx");
        File.WriteAllText(path,
            $"// @plugin\n// Name: {name}\n// Description: {description}\n// Tags: {tags}\n// @end\n{body}");
        return path;
    }

    [Fact]
    public void Register_ParsesName()
    {
        string p = TempPlugin("My Plugin", "Does things", "util");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Single(reg.GetAll());
            Assert.Equal("My Plugin", reg.GetAll()[0].Name);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Register_ParsesDescription()
    {
        string p = TempPlugin("P", "Some description", "x");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Equal("Some description", reg.GetAll()[0].Description);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Register_ParsesTags_CommaSeparated()
    {
        string p = TempPlugin("T", "desc", "util, format, cleanup");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            var tags = reg.GetAll()[0].Tags;
            Assert.Contains("util", tags);
            Assert.Contains("format", tags);
            Assert.Contains("cleanup", tags);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Register_FileWithoutFrontmatter_NotAdded()
    {
        string p = Path.ChangeExtension(Path.GetTempFileName(), ".csx");
        File.WriteAllText(p, "Console.WriteLine(\"no metadata\");");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Empty(reg.GetAll());
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Search_ByName_FindsPlugin()
    {
        string p = TempPlugin("Hello World", "says hello", "greet");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            var results = reg.Search("hello");
            Assert.Single(results);
            Assert.Equal("Hello World", results[0].Name);
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Search_ByTag_FindsPlugin()
    {
        string p = TempPlugin("Formatter", "formats code", "format, code");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Single(reg.Search("format"));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Search_ByDescription_FindsPlugin()
    {
        string p = TempPlugin("P", "inserts headers", "x");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Single(reg.Search("header"));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        string p = TempPlugin("Alpha", "alpha stuff", "alpha");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);
            Assert.Empty(reg.Search("zzz_no_match"));
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void Search_EmptyQuery_ReturnsAll()
    {
        string p1 = TempPlugin("A", "a", "x");
        string p2 = TempPlugin("B", "b", "y");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p1);
            reg.Register(p2);
            Assert.Equal(2, reg.Search("").Count);
        }
        finally { File.Delete(p1); File.Delete(p2); }
    }

    [Fact]
    public async Task Execute_RunsScript_WithDocAndMc()
    {
        string p = TempPlugin("Inserter", "inserts text", "edit",
            "doc.Insert(0, \"PLUGIN\");");
        try
        {
            var reg = new PluginRegistry();
            reg.Register(p);

            var doc = new TextDocument();
            doc.Load("hello");
            var mc = new MultiCursor(doc);

            var result = await reg.ExecuteAsync(reg.GetAll()[0], doc, mc);
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal("PLUGINhello", doc.GetText());
        }
        finally { File.Delete(p); }
    }

    [Fact]
    public void ScanDirectory_LoadsAllCsxFiles()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.csx"),
                "// @plugin\n// Name: A\n// Description: a\n// Tags: x\n// @end\n");
            File.WriteAllText(Path.Combine(dir, "b.csx"),
                "// @plugin\n// Name: B\n// Description: b\n// Tags: y\n// @end\n");
            File.WriteAllText(Path.Combine(dir, "notplugin.csx"),
                "// just a script\n");

            var reg = new PluginRegistry();
            reg.ScanDirectory(dir);
            Assert.Equal(2, reg.GetAll().Count);
        }
        finally { Directory.Delete(dir, true); }
    }
}
