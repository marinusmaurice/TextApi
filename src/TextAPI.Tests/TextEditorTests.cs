using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.Commands;
using TextAPI.Core.Decorations;
using TextAPI.Core.EOL;
using TextAPI.Core.Language;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// EOL Registry tests
// ═══════════════════════════════════════════════════════════════════════════

public class EolRegistryTests
{
    [Fact] public void Detect_Lf_ReturnsLf()
        => EolRegistry.Detect("hello\nworld").Should().Be(EolStyle.Lf);

    [Fact] public void Detect_CrLf_ReturnsCrLf()
        => EolRegistry.Detect("hello\r\nworld").Should().Be(EolStyle.CrLf);

    [Fact] public void Detect_Cr_ReturnsCr()
        => EolRegistry.Detect("hello\rworld").Should().Be(EolStyle.Cr);

    [Fact] public void Detect_Mixed_ReturnsMixed()
        => EolRegistry.Detect("hello\nworld\r\n").Should().Be(EolStyle.Mixed);

    [Fact] public void Detect_NoLineEndings_ReturnsLf()
        => EolRegistry.Detect("hello").Should().Be(EolStyle.Lf);

    [Fact] public void NormaliseOnLoad_CrLf_BecomesPureLf()
    {
        var reg    = new EolRegistry();
        var result = reg.NormaliseOnLoad("a\r\nb\r\nc");
        result.Should().Be("a\nb\nc");
        reg.OriginalStyle.Should().Be(EolStyle.CrLf);
    }

    [Fact] public void NormaliseOnLoad_Cr_BecomesPureLf()
    {
        var reg    = new EolRegistry();
        var result = reg.NormaliseOnLoad("a\rb\rc");
        result.Should().Be("a\nb\nc");
        reg.OriginalStyle.Should().Be(EolStyle.Cr);
    }

    [Fact] public void RestoreEol_CrLf_ExpandsCorrectly()
    {
        var reg = new EolRegistry();
        reg.NormaliseOnLoad("a\r\nb");
        reg.RestoreEol("a\nb").Should().Be("a\r\nb");
    }

    [Fact] public void RestoreEol_Cr_ExpandsCorrectly()
    {
        var reg = new EolRegistry();
        reg.NormaliseOnLoad("a\rb");
        reg.RestoreEol("a\nb").Should().Be("a\rb");
    }

    [Fact] public void CountLf_CountsCorrectly()
        => EolRegistry.CountLf("a\nb\nc\n".AsSpan()).Should().Be(3);
    // AsSpan() available via MemoryExtensions in net8 — just needs using System;

    [Fact] public void NormaliseInsert_CrLf_NormalisedToLf()
        => EolRegistry.NormaliseInsert("hello\r\nworld").Should().Be("hello\nworld");
}

// ═══════════════════════════════════════════════════════════════════════════
// Piece Table tests
// ═══════════════════════════════════════════════════════════════════════════

public class PieceTableTests
{
    private static PieceTable Make(string content = "")
    {
        var pt = new PieceTable();
        if (content.Length > 0) pt.Load(content);
        return pt;
    }

    // ── Basic read ────────────────────────────────────────────────────────

    [Fact] public void EmptyDocument_LengthZero()
        => new PieceTable().Length.Should().Be(0);

    [Fact] public void Load_SetsLengthAndLineCount()
    {
        var pt = Make("Hello\nWorld\n");
        pt.Length.Should().Be(12);
        pt.LineCount.Should().Be(3);  // "Hello", "World", ""
    }

    [Fact] public void GetText_ReturnsFull()
        => Make("abc").GetText().Should().Be("abc");

    [Fact] public void GetLine_ReturnsCorrectLine()
    {
        var pt = Make("Line0\nLine1\nLine2");
        pt.GetLine(0).Should().Be("Line0");
        pt.GetLine(1).Should().Be("Line1");
        pt.GetLine(2).Should().Be("Line2");
    }

    [Fact] public void GetText_Slice_ReturnsCorrectSlice()
        => Make("Hello World").GetText(6, 5).Should().Be("World");

    // ── Insert ────────────────────────────────────────────────────────────

    [Fact] public void Insert_AtStart_PrependsText()
    {
        var pt = Make("World");
        pt.Insert(0, "Hello ");
        pt.GetText().Should().Be("Hello World");
    }

    [Fact] public void Insert_AtEnd_AppendsText()
    {
        var pt = Make("Hello");
        pt.Insert(5, " World");
        pt.GetText().Should().Be("Hello World");
    }

    [Fact] public void Insert_InMiddle_SplitsPieceCorrectly()
    {
        var pt = Make("Hello World");
        pt.Insert(5, " Beautiful");
        pt.GetText().Should().Be("Hello Beautiful World");
    }

    [Fact] public void Insert_NewLine_IncreasesLineCount()
    {
        var pt = Make("HelloWorld");
        pt.Insert(5, "\n");
        pt.LineCount.Should().Be(2);
        pt.GetLine(0).Should().Be("Hello");
        pt.GetLine(1).Should().Be("World");
    }

    [Fact] public void Insert_CrLf_NormalisedToLf()
    {
        var pt = Make("Hello");
        pt.Insert(5, "\r\nWorld");
        pt.GetText().Should().NotContain("\r");
        pt.LineCount.Should().Be(2);
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [Fact] public void Delete_FromStart_RemovesChars()
    {
        var pt = Make("Hello World");
        pt.Delete(0, 6);
        pt.GetText().Should().Be("World");
    }

    [Fact] public void Delete_FromEnd_RemovesChars()
    {
        var pt = Make("Hello World");
        pt.Delete(5, 6);
        pt.GetText().Should().Be("Hello");
    }

    [Fact] public void Delete_FromMiddle_RemovesChars()
    {
        var pt = Make("Hello Beautiful World");
        pt.Delete(5, 10);
        pt.GetText().Should().Be("Hello World");
    }

    [Fact] public void Delete_NewLine_DecreasesLineCount()
    {
        var pt = Make("Hello\nWorld");
        pt.Delete(5, 1);
        pt.LineCount.Should().Be(1);
        pt.GetText().Should().Be("HelloWorld");
    }

    [Fact] public void Delete_EntireContent_LengthZero()
    {
        var pt = Make("Hello");
        pt.Delete(0, 5);
        pt.Length.Should().Be(0);
    }

    // ── Position ──────────────────────────────────────────────────────────

    [Fact] public void OffsetToPosition_CorrectLineAndColumn()
    {
        var pt = Make("Hello\nWorld");
        pt.OffsetToPosition(0).Should().Be((0, 0));
        pt.OffsetToPosition(5).Should().Be((0, 5));
        pt.OffsetToPosition(6).Should().Be((1, 0));
        pt.OffsetToPosition(9).Should().Be((1, 3));
    }

    [Fact] public void PositionToOffset_CorrectOffset()
    {
        var pt = Make("Hello\nWorld");
        pt.PositionToOffset(0, 0).Should().Be(0);
        pt.PositionToOffset(1, 0).Should().Be(6);
        pt.PositionToOffset(1, 3).Should().Be(9);
    }

    // ── Compaction ────────────────────────────────────────────────────────

    [Fact] public void Compact_PreservesContent()
    {
        var pt = Make("Hello");
        pt.Insert(5, " World");
        pt.Delete(0, 6);
        var before = pt.GetText();
        pt.Compact();
        pt.GetText().Should().Be(before);
        pt.PieceCount.Should().Be(1);
    }

    // ── EOL on load ───────────────────────────────────────────────────────

    [Fact] public void Load_CrLf_NormalisedInternally()
    {
        var pt = Make("Hello\r\nWorld");
        pt.OriginalEolStyle.Should().Be(EolStyle.CrLf);
        pt.GetText().Should().NotContain("\r");
        pt.LineCount.Should().Be(2);
    }

    [Fact] public void GetTextWithEol_RestoresCrLf()
    {
        var pt = Make("Hello\r\nWorld");
        pt.GetTextWithEol().Should().Be("Hello\r\nWorld");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Command + Undo/Redo tests
// ═══════════════════════════════════════════════════════════════════════════

public class CommandTests
{
    private static (PieceTable Buf, CommandHistory History) Setup(string content = "Hello World")
    {
        var buf = new PieceTable();
        buf.Load(content);
        return (buf, new CommandHistory());
    }

    [Fact] public void InsertCommand_Execute_InsertsText()
    {
        var (buf, hist) = Setup("Hello");
        hist.Execute(new InsertCommand(buf, 5, " World"));
        buf.GetText().Should().Be("Hello World");
    }

    [Fact] public void InsertCommand_Undo_RestoresOriginal()
    {
        var (buf, hist) = Setup("Hello");
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        buf.GetText().Should().Be("Hello");
    }

    [Fact] public void DeleteCommand_Undo_RestoresText()
    {
        var (buf, hist) = Setup("Hello World");
        hist.Execute(new DeleteCommand(buf, 5, 6));
        hist.Undo();
        buf.GetText().Should().Be("Hello World");
    }

    [Fact] public void ReplaceCommand_Undo_RestoresText()
    {
        var (buf, hist) = Setup("Hello World");
        hist.Execute(new ReplaceCommand(buf, 6, 5, "Claude"));
        buf.GetText().Should().Be("Hello Claude");
        hist.Undo();
        buf.GetText().Should().Be("Hello World");
    }

    [Fact] public void Redo_AfterUndo_ReappliesCommand()
    {
        var (buf, hist) = Setup("Hello");
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.Redo();
        buf.GetText().Should().Be("Hello World");
    }

    [Fact] public void NewEdit_AfterUndo_ClearsRedoStack()
    {
        var (buf, hist) = Setup("Hello");
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.Execute(new InsertCommand(buf, 5, "!"));
        hist.CanRedo.Should().BeFalse();
    }

    [Fact] public void CompositeCommand_UndoesAll()
    {
        var (buf, hist) = Setup("Hello World");
        var cmds = new IEditorCommand[]
        {
            new InsertCommand(buf, 0, ">> "),
            new InsertCommand(buf, 14, " <<")
        };
        hist.Execute(new CompositeCommand("wrap", cmds));
        buf.GetText().Should().Be(">> Hello World <<");
        hist.Undo();
        buf.GetText().Should().Be("Hello World");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Decoration tree tests
// ═══════════════════════════════════════════════════════════════════════════

public class DecorationTreeTests
{
    [Fact] public void AddAndQuery_ReturnsDecoration()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 0, Type = DecorationType.SyntaxHighlight, Tag = "keyword" }.SetEnd(5));
        tree.GetDecorationsInRange(0, 10).Should().HaveCount(1);
    }

    [Fact] public void QueryRange_ExcludesNonOverlapping()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 20, Type = DecorationType.SyntaxHighlight }.SetEnd(30));
        tree.GetDecorationsInRange(0, 10).Should().BeEmpty();
    }

    [Fact] public void OnInsert_ShiftsDecorations()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 10, Type = DecorationType.ErrorSquiggle }.SetEnd(15);
        tree.AddDecoration(d);
        tree.OnInsert(5, 3);
        d.Start.Should().Be(13);
        d.End.Should().Be(18);
    }

    [Fact] public void OnDelete_RemovesFullyDeletedDecoration()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 5, Type = DecorationType.SearchMatch }.SetEnd(10));
        tree.OnDelete(4, 7);
        tree.Count.Should().Be(0);
    }

    [Fact] public void OnDelete_ShiftsDecorationsAfterDeletePoint()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 20, Type = DecorationType.Bookmark }.SetEnd(25);
        tree.AddDecoration(d);
        tree.OnDelete(5, 5);
        d.Start.Should().Be(15);
        d.End.Should().Be(20);
    }

    [Fact] public void RemoveDecoration_ById_RemovesIt()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 0, Type = DecorationType.Selection }.SetEnd(5);
        tree.AddDecoration(d);
        tree.RemoveDecoration(d.Id).Should().BeTrue();
        tree.Count.Should().Be(0);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// TextDocument integration tests
// ═══════════════════════════════════════════════════════════════════════════

public class TextDocumentTests
{
    [Fact] public void FullRoundTrip_InsertDeleteUndo()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("namespace Foo\n{\n}");
        doc.Insert(14, "    class Bar\n    {\n    }\n");
        doc.GetLine(1).Should().Be("    class Bar");
        doc.Undo();
        doc.GetText().Should().Be("namespace Foo\n{\n}");
    }

    [Fact] public void CrLf_File_RoundTrip()
    {
        var doc = new TextDocument();
        doc.Load("Line1\r\nLine2\r\nLine3");
        doc.OriginalEolStyle.Should().Be(EolStyle.CrLf);
        doc.LineCount.Should().Be(3);
        // Editing in LF internally
        doc.Insert(doc.PositionToOffset(1, 0), "Inserted\n");
        // Save should restore CRLF
        doc.GetTextWithEol().Should().Contain("\r\n");
        doc.GetTextWithEol().Should().NotContain("\r\n\r\n\r\n"); // no doubled endings
    }

    [Fact] public void TokeniseLine_CSharp_ReturnsKeywords()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        var tokens = doc.TokeniseLine(0);
        tokens.Should().Contain(t => t.Type == "keyword" && doc.GetText(t.Start, t.Length) == "public");
        tokens.Should().Contain(t => t.Type == "keyword" && doc.GetText(t.Start, t.Length) == "class");
    }

    [Fact] public void AddDecoration_GetDecorationsInRange_ReturnsCorrect()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        var id = doc.AddDecoration(6, 11, DecorationType.SearchMatch, "match");
        doc.GetDecorationsInRange(0, 20).Should().Contain(d => d.Id == id);
        doc.GetDecorationsInRange(0, 5).Should().NotContain(d => d.Id == id);
    }

    [Fact] public void LargeInsert_ManyLines_LineCountCorrect()
    {
        var doc  = new TextDocument();
        var lines = string.Join("\n", Enumerable.Range(1, 1000).Select(i => $"Line {i}"));
        doc.Load(lines);
        doc.LineCount.Should().Be(1000);
        doc.GetLine(999).Should().Be("Line 1000");
    }

    [Fact] public void Compact_TriggeredAutomatically_ContentPreserved()
    {
        // compactionThreshold = 5 for this test
        var doc = new TextDocument(compactionThreshold: 5);
        doc.Load("Start");
        for (int i = 0; i < 6; i++)
            doc.Insert(doc.Length, $" {i}");
        doc.GetText().Should().Be("Start 0 1 2 3 4 5");
    }
}

// helper to allow inline .SetEnd() calls in test initialisers
internal static class TestDecorationExt
{
    internal static Decoration SetEnd(this Decoration d, int end) { d.End = end; return d; }
}
