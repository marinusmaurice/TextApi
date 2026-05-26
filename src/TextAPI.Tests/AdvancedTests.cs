using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Buffer;
using TextAPI.Core.Commands;
using TextAPI.Core.Decorations;
using TextAPI.Core.EOL;
using TextAPI.Core.Language;
using TextAPI.Core.Search;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// TextDocument state — IsModified, FilePath, LanguageId, Load resets
// ═══════════════════════════════════════════════════════════════════════════

public class TextDocumentStateTests
{
    [Fact] public void IsModified_FalseAfterLoad()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.IsModified.Should().BeFalse();
    }

    [Fact] public void IsModified_TrueAfterInsert()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.IsModified.Should().BeTrue();
    }

    [Fact] public void IsModified_TrueAfterDelete()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Delete(5, 6);
        doc.IsModified.Should().BeTrue();
    }

    [Fact] public void IsModified_TrueAfterReplace()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(6, 5, "Claude");
        doc.IsModified.Should().BeTrue();
    }

    [Fact] public void IsModified_TrueAfterUndo()
    {
        // Undo is still a document mutation — IsModified stays true
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.Undo();
        doc.IsModified.Should().BeTrue();
    }

    [Fact] public void IsModified_FalseAfterSave()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.IsModified.Should().BeTrue();
        using var ms = new MemoryStream();
        doc.SaveAsync(ms).GetAwaiter().GetResult();
        doc.IsModified.Should().BeFalse();
    }

    [Fact] public void FilePath_NullWhenLoadedWithoutPath()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.FilePath.Should().BeNull();
    }

    [Fact] public void FilePath_SetWhenLoadedWithPath()
    {
        var doc = new TextDocument();
        doc.Load("Hello", filePath: "/tmp/test.cs");
        doc.FilePath.Should().Be("/tmp/test.cs");
    }

    [Fact] public void LanguageId_ReflectsTokeniser_CSharp()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.LanguageId.Should().Be("csharp");
    }

    [Fact] public void LanguageId_ReflectsNullTokeniser()
    {
        var doc = new TextDocument();   // NullTokeniser default
        doc.LanguageId.Should().Be("plaintext");
    }

    [Fact] public void CanUndo_FalseAfterLoad()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.CanUndo.Should().BeFalse();
    }

    [Fact] public void CanRedo_FalseAfterLoad()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.CanRedo.Should().BeFalse();
    }

    [Fact] public void SecondLoad_ResetsUndoRedoState()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.CanUndo.Should().BeTrue();
        doc.Load("Fresh content");
        doc.CanUndo.Should().BeFalse();
        doc.CanRedo.Should().BeFalse();
    }

    [Fact] public void SecondLoad_ResetsIsModified()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");
        doc.IsModified.Should().BeTrue();
        doc.Load("Reset");
        doc.IsModified.Should().BeFalse();
    }

    [Fact] public void SecondLoad_ResetsFilePath()
    {
        var doc = new TextDocument();
        doc.Load("Hello", filePath: "/old.cs");
        doc.Load("New content");
        doc.FilePath.Should().BeNull();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Replace() operation — atomic replace, undo, redo
// ═══════════════════════════════════════════════════════════════════════════

public class ReplaceOperationTests
{
    [Fact] public void Replace_SubstitutesText()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(6, 5, "Claude");
        doc.GetText().Should().Be("Hello Claude");
    }

    [Fact] public void Replace_WithLongerText_ExpandsDocument()
    {
        var doc = new TextDocument();
        doc.Load("Hi");
        doc.Replace(0, 2, "Hello World");
        doc.GetText().Should().Be("Hello World");
        doc.Length.Should().Be(11);
    }

    [Fact] public void Replace_WithShorterText_ShrinksDocument()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(0, 11, "Hi");
        doc.GetText().Should().Be("Hi");
        doc.Length.Should().Be(2);
    }

    [Fact] public void Replace_WithEmptyString_DeletesRange()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(5, 6, "");
        doc.GetText().Should().Be("Hello");
    }

    [Fact] public void Replace_SingleCharAtStart()
    {
        var doc = new TextDocument();
        doc.Load("xello World");
        doc.Replace(0, 1, "H");
        doc.GetText().Should().Be("Hello World");
    }

    [Fact] public void Replace_Undo_RestoresOriginal()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(6, 5, "Claude");
        doc.Undo();
        doc.GetText().Should().Be("Hello World");
    }

    [Fact] public void Replace_Redo_ReappliesChange()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.Replace(6, 5, "Claude");
        doc.Undo();
        doc.Redo();
        doc.GetText().Should().Be("Hello Claude");
    }

    [Fact] public void Replace_IsSingleUndoStep()
    {
        var doc = new TextDocument();
        doc.Load("A B C");
        doc.Replace(2, 1, "X");
        doc.Undo();
        doc.GetText().Should().Be("A B C");
        doc.CanUndo.Should().BeFalse("replace is one undo unit");
    }

    [Fact] public void Replace_AcrossNewline_UpdatesLineCount()
    {
        var doc = new TextDocument();
        doc.Load("Line1\nLine2");
        doc.Replace(0, 11, "Single");
        doc.LineCount.Should().Be(1);
        doc.GetText().Should().Be("Single");
    }

    [Fact] public void Replace_WithNewline_SplitsLine()
    {
        var doc = new TextDocument();
        doc.Load("HelloWorld");
        doc.Replace(5, 0, "\n");
        doc.LineCount.Should().Be(2);
        doc.GetLine(0).Should().Be("Hello");
        doc.GetLine(1).Should().Be("World");
    }

    [Fact] public void Replace_Undo_RestoresLineCount()
    {
        var doc = new TextDocument();
        doc.Load("Line1\nLine2");
        doc.Replace(0, 11, "Single");
        doc.Undo();
        doc.LineCount.Should().Be(2);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// CommandHistory — descriptions, undo limit overflow
// ═══════════════════════════════════════════════════════════════════════════

public class CommandHistoryIntrospectionTests
{
    private static (PieceTable Buf, CommandHistory Hist) Setup(string content = "Hello")
    {
        var buf = new PieceTable(); buf.Load(content);
        return (buf, new CommandHistory());
    }

    [Fact] public void UndoDescriptions_ContainsDescription()
    {
        var (buf, hist) = Setup();
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.UndoDescriptions.Should().ContainSingle()
            .Which.Should().Be("Insert 6 chars at 5");
    }

    [Fact] public void RedoDescriptions_PopulatedAfterUndo()
    {
        var (buf, hist) = Setup();
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.RedoDescriptions.Should().ContainSingle()
            .Which.Should().Be("Insert 6 chars at 5");
    }

    [Fact] public void UndoDescriptions_EmptyAfterUndo()
    {
        var (buf, hist) = Setup();
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.UndoDescriptions.Should().BeEmpty();
    }

    [Fact] public void RedoDescriptions_EmptyAfterNewEdit()
    {
        var (buf, hist) = Setup();
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.CanRedo.Should().BeTrue();
        hist.Execute(new InsertCommand(buf, 5, "!"));
        hist.RedoDescriptions.Should().BeEmpty();
    }

    [Fact] public void DeleteCommand_Description_ContainsOffset()
    {
        var (buf, hist) = Setup("Hello World");
        hist.Execute(new DeleteCommand(buf, 5, 6));
        hist.UndoDescriptions.First().Should().Contain("5");
    }

    [Fact] public void ReplaceCommand_Description_ContainsReplacement()
    {
        var (buf, hist) = Setup("Hello World");
        hist.Execute(new ReplaceCommand(buf, 6, 5, "Claude"));
        hist.UndoDescriptions.First().Should().Contain("Claude");
    }

    [Fact] public void UndoHistoryLimit_OldestCommandsDropped()
    {
        var buf = new PieceTable(); buf.Load("");
        var hist = new CommandHistory(maxHistory: 3);
        for (int i = 0; i < 4; i++)
            hist.Execute(new InsertCommand(buf, 0, "x"));

        // Exactly 3 undos available (oldest was dropped)
        int undoCount = 0;
        while (hist.CanUndo) { hist.Undo(); undoCount++; }
        undoCount.Should().Be(3);
    }

    [Fact] public void UndoHistoryLimit_AllEditsUndoable_WhenWithinLimit()
    {
        var buf = new PieceTable(); buf.Load("");
        var hist = new CommandHistory(maxHistory: 10);
        for (int i = 0; i < 10; i++)
            hist.Execute(new InsertCommand(buf, 0, "x"));

        int undoCount = 0;
        while (hist.CanUndo) { hist.Undo(); undoCount++; }
        undoCount.Should().Be(10);
    }

    [Fact] public void Clear_EmptiesBothStacks()
    {
        var (buf, hist) = Setup();
        hist.Execute(new InsertCommand(buf, 5, " World"));
        hist.Undo();
        hist.CanUndo.Should().BeFalse();
        hist.CanRedo.Should().BeTrue();
        hist.Clear();
        hist.CanUndo.Should().BeFalse();
        hist.CanRedo.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// NullTokeniser
// ═══════════════════════════════════════════════════════════════════════════

public class NullTokeniserTests
{
    [Fact] public void NullTokeniser_ReturnsEmptyTokenList()
    {
        var tok = new NullTokeniser();
        tok.TokeniseLine("public class Foo {}").Should().BeEmpty();
    }

    [Fact] public void NullTokeniser_LanguageId_IsPlaintext()
    {
        new NullTokeniser().LanguageId.Should().Be("plaintext");
    }

    [Fact] public void NullTokeniser_EmptyLine_ReturnsEmpty()
    {
        new NullTokeniser().TokeniseLine("").Should().BeEmpty();
    }

    [Fact] public void TextDocument_DefaultTokeniser_ReturnsNoTokens()
    {
        var doc = new TextDocument();   // default = NullTokeniser
        doc.Load("public class Foo {}");
        doc.TokeniseLine(0).Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// CSharpTokeniser — per-token-type correctness
// ═══════════════════════════════════════════════════════════════════════════

public class CSharpTokeniserTests
{
    private static IReadOnlyList<SyntaxToken> Tok(string line, int offset = 0)
        => new CSharpTokeniser().TokeniseLine(line, offset);

    [Fact] public void LanguageId_IsCsharp()
        => new CSharpTokeniser().LanguageId.Should().Be("csharp");

    [Fact] public void EmptyLine_ReturnsNoTokens()
        => Tok("").Should().BeEmpty();

    // ── Keywords ──────────────────────────────────────────────────────────

    [Fact] public void Keyword_Public_Detected()
        => Tok("public").Should().Contain(t => t.Type == "keyword" && t.Start == 0 && t.Length == 6);

    [Fact] public void Keyword_Class_Detected()
        => Tok("class Foo").Should().Contain(t => t.Type == "keyword" && t.Length == 5);

    [Fact] public void Keyword_Namespace_Detected()
        => Tok("namespace Foo").Should().Contain(t => t.Type == "keyword");

    [Fact] public void Keywords_MultipleOnLine_AllDetected()
    {
        var tokens = Tok("public static void Main()");
        tokens.Where(t => t.Type == "keyword").Should().HaveCountGreaterThanOrEqualTo(3,
            "public, static, void are all keywords");
    }

    [Fact] public void Keyword_Inside_Identifier_NotMatched()
    {
        // "returns" contains "return" but is not a keyword
        var tokens = Tok("returns");
        tokens.Should().NotContain(t => t.Type == "keyword" && t.Length == 6,
            "\"returns\" should not match keyword \"return\"");
    }

    // ── Types (PascalCase identifiers) ────────────────────────────────────

    [Fact] public void Type_PascalCase_Detected()
        => Tok("Foo bar").Should().Contain(t => t.Type == "type" && t.Length == 3);

    [Fact] public void Type_MultipleUppercase_EachDetected()
    {
        var tokens = Tok("List<String>");
        tokens.Where(t => t.Type == "type").Select(t => t.Length)
            .Should().Contain(4)   // "List"
            .And.Contain(6);       // "String"
    }

    [Fact] public void Type_StartingWithLowercase_IsIdentifier()
        => Tok("myVar").Should().Contain(t => t.Type == "identifier");

    // ── Strings ───────────────────────────────────────────────────────────

    [Fact] public void String_DoubleQuoted_Detected()
        => Tok(@"""hello world""").Should().Contain(t => t.Type == "string");

    [Fact] public void String_CharLiteral_Detected()
        => Tok("'x'").Should().Contain(t => t.Type == "string");

    [Fact] public void String_EscapeSequence_IncludedInToken()
    {
        var tokens = Tok(@"""hello\nworld""");
        tokens.Should().Contain(t => t.Type == "string" && t.Length > 5);
    }

    [Fact] public void String_InterpolatedPrefix_Detected()
    {
        var tokens = Tok(@"$""hello {name}""");
        tokens.Should().Contain(t => t.Type == "string");
    }

    // ── Comments ──────────────────────────────────────────────────────────

    [Fact] public void Comment_SingleLine_Detected()
    {
        var tokens = Tok("// this is a comment");
        tokens.Should().ContainSingle(t => t.Type == "comment");
        tokens.Single(t => t.Type == "comment").Start.Should().Be(0);
    }

    [Fact] public void Comment_SingleLine_AfterCode()
    {
        var tokens = Tok("int x = 5; // assign");
        tokens.Should().Contain(t => t.Type == "comment");
    }

    [Fact] public void Comment_Block_Detected()
    {
        var tokens = Tok("/* block comment */");
        tokens.Should().ContainSingle(t => t.Type == "comment");
    }

    [Fact] public void Comment_EatsCodeAfterSlashes()
    {
        // Code after // on same line should be part of comment, not keywords
        var tokens = Tok("// public class Foo");
        tokens.Should().ContainSingle();
        tokens[0].Type.Should().Be("comment");
    }

    // ── Numbers ───────────────────────────────────────────────────────────

    [Fact] public void Number_Integer_Detected()
        => Tok("42").Should().Contain(t => t.Type == "number" && t.Length == 2);

    [Fact] public void Number_Float_Detected()
        => Tok("3.14f").Should().Contain(t => t.Type == "number");

    [Fact] public void Number_WithSuffix_Detected()
        => Tok("100L").Should().Contain(t => t.Type == "number");

    [Fact] public void Number_InExpression_Detected()
    {
        var tokens = Tok("x = 42 + 1");
        tokens.Where(t => t.Type == "number").Should().HaveCount(2);
    }

    // ── Operators ─────────────────────────────────────────────────────────

    [Fact] public void Operator_Equals_Detected()
        => Tok("x = 5").Should().Contain(t => t.Type == "operator" && t.Length == 1);

    [Fact] public void Operator_Braces_Detected()
    {
        var tokens = Tok("{ }");
        tokens.Where(t => t.Type == "operator").Should().HaveCount(2);
    }

    [Fact] public void Operator_Semicolon_Detected()
        => Tok("return 0;").Should().Contain(t => t.Type == "operator");

    // ── Identifiers ───────────────────────────────────────────────────────

    [Fact] public void Identifier_LowercaseWord_Detected()
        => Tok("myVariable").Should().Contain(t => t.Type == "identifier");

    [Fact] public void Identifier_Underscore_Prefix_Detected()
        => Tok("_private").Should().Contain(t => t.Type == "identifier");

    // ── Token offsets with non-zero lineOffset ─────────────────────────────

    [Fact] public void Token_Offsets_AreRelativeToLineOffset()
    {
        // lineOffset = 10 means the line starts at doc offset 10
        var tokens = Tok("public", offset: 10);
        tokens.Should().ContainSingle();
        tokens[0].Start.Should().Be(10);
        tokens[0].Length.Should().Be(6);
    }

    [Fact] public void Token_End_Equals_Start_Plus_Length()
    {
        var tokens = Tok("public class Foo {}");
        foreach (var t in tokens)
            t.End.Should().Be(t.Start + t.Length, $"token {t.Type} at {t.Start}");
    }

    [Fact] public void Tokens_SortedByStart()
    {
        var tokens = Tok("public class Foo { int x = 42; }");
        for (int i = 1; i < tokens.Count; i++)
            tokens[i].Start.Should().BeGreaterThanOrEqualTo(tokens[i - 1].Start);
    }

    [Fact] public void Tokens_DoNotOverlap()
    {
        var tokens = Tok("public class Foo { int x = 42; }");
        for (int i = 1; i < tokens.Count; i++)
            tokens[i].Start.Should().BeGreaterThanOrEqualTo(tokens[i - 1].End,
                $"token [{tokens[i].Type}] overlaps previous [{tokens[i - 1].Type}]");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Tokenisation integration — TokeniseLines, SetTokeniser
// ═══════════════════════════════════════════════════════════════════════════

public class TokeniseIntegrationTests
{
    [Fact] public void TokeniseLine_SecondLine_OffsetIsDocumentAbsolute()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("x\npublic");
        // Line 0 = "x", Line 1 = "public" starting at offset 2
        var tokens = doc.TokeniseLine(1);
        tokens.Should().Contain(t => t.Type == "keyword" && t.Start == 2);
    }

    [Fact] public void SetTokeniser_ChangesLanguageId()
    {
        var doc = new TextDocument();
        doc.LanguageId.Should().Be("plaintext");
        doc.SetTokeniser(new CSharpTokeniser());
        doc.LanguageId.Should().Be("csharp");
    }

    [Fact] public void SetTokeniser_ChangesTokenisationBehaviour()
    {
        var doc = new TextDocument();   // NullTokeniser
        doc.Load("public class Foo {}");
        doc.TokeniseLine(0).Should().BeEmpty();

        doc.SetTokeniser(new CSharpTokeniser());
        doc.TokeniseLine(0).Should().NotBeEmpty();
    }

    [Fact] public void TokeniseLines_AddsSyntaxHighlightDecorations()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        doc.TokeniseLines(0, 0);
        doc.GetDecorationsInRange(0, doc.Length).Should().NotBeEmpty();
    }

    [Fact] public void TokeniseLines_AllDecorationsAreSyntaxHighlight()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        doc.TokeniseLines(0, 0);
        doc.GetDecorationsInRange(0, doc.Length)
            .Should().OnlyContain(d => d.Type == DecorationType.SyntaxHighlight);
    }

    [Fact] public void TokeniseLines_ReplacesExistingSyntaxHighlights()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        doc.TokeniseLines(0, 0);
        int firstCount = doc.GetDecorationsInRange(0, doc.Length).Count();

        // Retokenise — should not double the decoration count
        doc.TokeniseLines(0, 0);
        doc.GetDecorationsInRange(0, doc.Length).Count().Should().Be(firstCount);
    }

    [Fact] public void TokeniseLines_PreservesOtherDecorationTypes()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        var errorId = doc.AddDecoration(0, 6, DecorationType.ErrorSquiggle, "err");

        doc.TokeniseLines(0, 0);   // clears SyntaxHighlight, not ErrorSquiggle

        doc.GetDecorationsInRange(0, doc.Length)
            .Should().Contain(d => d.Id == errorId, "ErrorSquiggle is preserved");
    }

    [Fact] public void TokeniseLines_MultipleLines_AllTokenised()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo\n{\n    int x;\n}");
        doc.TokeniseLines(0, doc.LineCount - 1);
        // Should have tokens from multiple lines
        doc.GetDecorationsInRange(0, doc.Length).Count().Should().BeGreaterThan(3);
    }

    [Fact] public void TokeniseLine_DecorationData_IsTokenItself()
    {
        var doc = new TextDocument(new CSharpTokeniser());
        doc.Load("public class Foo {}");
        doc.TokeniseLines(0, 0);
        var decoration = doc.GetDecorationsInRange(0, 6).FirstOrDefault();
        decoration.Should().NotBeNull();
        decoration!.Data.Should().BeOfType<SyntaxToken>();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DecorationTree — advanced shift, partial overlap, RemoveAllOfType
// ═══════════════════════════════════════════════════════════════════════════

public class DecorationAdvancedTests
{
    // ── RemoveAllOfType ────────────────────────────────────────────────────

    [Fact] public void RemoveAllOfType_RemovesOnlyThatType()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 0, Type = DecorationType.SearchMatch }.SetEnd(5));
        tree.AddDecoration(new Decoration { Start = 6, Type = DecorationType.ErrorSquiggle }.SetEnd(10));
        tree.AddDecoration(new Decoration { Start = 11, Type = DecorationType.SearchMatch }.SetEnd(15));

        tree.RemoveAllOfType(DecorationType.SearchMatch);

        tree.Count.Should().Be(1);
        tree.GetDecorationsInRange(0, 100).Should().OnlyContain(d => d.Type == DecorationType.ErrorSquiggle);
    }

    [Fact] public void RemoveAllOfType_NoOp_WhenTypeAbsent()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 0, Type = DecorationType.Bookmark }.SetEnd(5));
        tree.RemoveAllOfType(DecorationType.Selection);
        tree.Count.Should().Be(1);
    }

    [Fact] public void Clear_RemovesAllDecorations()
    {
        var tree = new DecorationTree();
        for (int i = 0; i < 5; i++)
            tree.AddDecoration(new Decoration { Start = i * 10, Type = DecorationType.Custom }.SetEnd(i * 10 + 5));
        tree.Clear();
        tree.Count.Should().Be(0);
    }

    // ── All decoration types ───────────────────────────────────────────────

    [Fact] public void AllDecorationTypes_CanBeAdded()
    {
        var tree = new DecorationTree();
        foreach (var type in Enum.GetValues<DecorationType>())
            tree.AddDecoration(new Decoration { Start = 0, Type = type }.SetEnd(1));
        tree.Count.Should().Be(Enum.GetValues<DecorationType>().Length);
    }

    // ── OnInsert: shift behaviour ──────────────────────────────────────────

    [Fact] public void OnInsert_DecorationBeforeInsert_NotShifted()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 0, Type = DecorationType.Bookmark }.SetEnd(3);
        tree.AddDecoration(d);
        tree.OnInsert(10, 5);   // insert far after decoration
        d.Start.Should().Be(0);
        d.End.Should().Be(3);
    }

    [Fact] public void OnInsert_DecorationAfterInsert_FullyShifted()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 10, Type = DecorationType.Bookmark }.SetEnd(15);
        tree.AddDecoration(d);
        tree.OnInsert(5, 3);
        d.Start.Should().Be(13);
        d.End.Should().Be(18);
    }

    [Fact] public void OnInsert_DecorationSpanningInsertPoint_EndExtended()
    {
        // Decoration [3, 8), insert 2 chars at offset 5 → [3, 10)
        var tree = new DecorationTree();
        var d = new Decoration { Start = 3, Type = DecorationType.Selection }.SetEnd(8);
        tree.AddDecoration(d);
        tree.OnInsert(5, 2);
        d.Start.Should().Be(3, "start before insertion, not shifted");
        d.End.Should().Be(10, "end after insertion, extended");
    }

    [Fact] public void OnInsert_AtExactDecorationStart_ShiftsBoth()
    {
        // Insert exactly at Start — should shift the whole decoration
        var tree = new DecorationTree();
        var d = new Decoration { Start = 5, Type = DecorationType.Bookmark }.SetEnd(10);
        tree.AddDecoration(d);
        tree.OnInsert(5, 3);
        d.Start.Should().Be(8);
        d.End.Should().Be(13);
    }

    // ── OnDelete: shift and removal behaviour ──────────────────────────────

    [Fact] public void OnDelete_DecorationAfterDeletedRange_Shifted()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 15, Type = DecorationType.Bookmark }.SetEnd(20);
        tree.AddDecoration(d);
        tree.OnDelete(5, 5);   // delete [5, 10)
        d.Start.Should().Be(10);
        d.End.Should().Be(15);
    }

    [Fact] public void OnDelete_DecorationBeforeDeletedRange_NotAffected()
    {
        var tree = new DecorationTree();
        var d = new Decoration { Start = 0, Type = DecorationType.Bookmark }.SetEnd(3);
        tree.AddDecoration(d);
        tree.OnDelete(5, 5);
        d.Start.Should().Be(0);
        d.End.Should().Be(3);
    }

    [Fact] public void OnDelete_DecorationFullyInsideDeletedRange_Removed()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 6, Type = DecorationType.SearchMatch }.SetEnd(9));
        tree.OnDelete(5, 10);
        tree.Count.Should().Be(0);
    }

    [Fact] public void OnDelete_DecorationOverlapsDeletedRange_LeftSide_EndTrimmed()
    {
        // Decoration [2, 8), delete [5, 10) — end trimmed to deletion start
        var tree = new DecorationTree();
        var d = new Decoration { Start = 2, Type = DecorationType.WarningSquiggle }.SetEnd(8);
        tree.AddDecoration(d);
        tree.OnDelete(5, 5);
        d.Start.Should().Be(2);
        d.End.Should().Be(5);
    }

    // ── Decoration Tag and Data ────────────────────────────────────────────

    [Fact] public void Decoration_Tag_PreservedInQuery()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 0, Type = DecorationType.SyntaxHighlight, Tag = "keyword" }.SetEnd(6));
        var result = tree.GetDecorationsInRange(0, 10).Single();
        result.Tag.Should().Be("keyword");
    }

    [Fact] public void Decoration_Data_PreservedInQuery()
    {
        var tree = new DecorationTree();
        var payload = new object();
        tree.AddDecoration(new Decoration { Start = 0, Type = DecorationType.Custom, Data = payload }.SetEnd(5));
        var result = tree.GetDecorationsInRange(0, 10).Single();
        result.Data.Should().BeSameAs(payload);
    }

    [Fact] public void Decoration_Length_EqualsEndMinusStart()
    {
        var d = new Decoration { Start = 3, Type = DecorationType.Bookmark }.SetEnd(10);
        d.Length.Should().Be(7);
    }

    // ── Multiple decorations sorted order ─────────────────────────────────

    [Fact] public void MultipleDecorations_AddedOutOfOrder_QueriedCorrectly()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 20, Type = DecorationType.Bookmark }.SetEnd(25));
        tree.AddDecoration(new Decoration { Start = 0,  Type = DecorationType.Bookmark }.SetEnd(5));
        tree.AddDecoration(new Decoration { Start = 10, Type = DecorationType.Bookmark }.SetEnd(15));

        var results = tree.GetDecorationsInRange(0, 100).ToList();
        results.Should().HaveCount(3);
        results.Select(d => d.Start).Should().BeInAscendingOrder();
    }

    [Fact] public void GetDecorationsOnLine_ReturnsOverlappingDecorations()
    {
        var tree = new DecorationTree();
        tree.AddDecoration(new Decoration { Start = 5, Type = DecorationType.ErrorSquiggle }.SetEnd(10));
        tree.GetDecorationsOnLine(lineStart: 0, lineEnd: 20).Should().HaveCount(1);
    }

    // ── TextDocument decoration integration ───────────────────────────────

    [Fact] public void TextDocument_Undo_ClearsDecorations()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        doc.AddDecoration(0, 5, DecorationType.SearchMatch, "tag");
        doc.Undo();   // nothing to undo but triggers clear
        // After undo, decorations cleared (simple safe approach in TextDocument)
        doc.GetDecorationsInRange(0, doc.Length).Should().BeEmpty();
    }

    [Fact] public void TextDocument_ReplaceAll_ClearsDecorations()
    {
        var doc = new TextDocument();
        doc.Load("foo bar foo");
        doc.AddDecoration(0, 3, DecorationType.SearchMatch, "m");
        doc.ReplaceAll("foo", "qux");
        doc.GetDecorationsInRange(0, doc.Length).Should().BeEmpty();
    }

    [Fact] public void TextDocument_Insert_ShiftsDecorationsAfterOffset()
    {
        var doc = new TextDocument();
        doc.Load("Hello World");
        var id = doc.AddDecoration(6, 11, DecorationType.Bookmark, "world");
        doc.Insert(0, ">>>");
        var shifted = doc.GetDecorationsInRange(0, doc.Length)
            .First(d => d.Id == id);
        shifted.Start.Should().Be(9, "decoration shifted by 3 inserted chars");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Async I/O — SaveAsync round-trip, SaveEolStyle override
// ═══════════════════════════════════════════════════════════════════════════

public class AsyncIoTests
{
    [Fact] public async Task SaveAsync_ContentMatchesGetText()
    {
        var doc = new TextDocument();
        doc.Load("Hello World\nLine 2\nLine 3");

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Position = 0;
        var saved = new StreamReader(ms, Encoding.UTF8).ReadToEnd();
        saved.Should().Be(doc.GetText());
    }

    [Fact] public async Task SaveAsync_EmptyDocument_WritesNothing()
    {
        var doc = new TextDocument();
        doc.Load("");

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Length.Should().Be(0);
    }

    [Fact] public async Task SaveAsync_AfterEdit_SavesCurrentContent()
    {
        var doc = new TextDocument();
        doc.Load("Hello");
        doc.Insert(5, " World");

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Position = 0;
        var saved = new StreamReader(ms, Encoding.UTF8).ReadToEnd();
        saved.Should().Be("Hello World");
    }

    [Fact] public async Task SaveAsync_CrLfDoc_DefaultSaveStyleRestoresCrLf()
    {
        // CRLF docs default SaveStyle = CrLf — should write CRLF on save
        var doc = new TextDocument();
        doc.Load("Line1\r\nLine2");
        doc.SaveEolStyle.Should().Be(EolStyle.CrLf, "CRLF doc defaults to CrLf save style");

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Position = 0;
        var saved = new StreamReader(ms, Encoding.UTF8).ReadToEnd();
        saved.Should().Contain("\r\n");
    }

    [Fact] public async Task SaveAsync_SaveEolStyleOverride_WritesDifferentEol()
    {
        var doc = new TextDocument();
        doc.Load("Line1\r\nLine2");
        doc.SaveEolStyle = EolStyle.Lf;   // override to LF

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Position = 0;
        var saved = new StreamReader(ms, Encoding.UTF8).ReadToEnd();
        saved.Should().NotContain("\r\n", "override forces LF output");
        saved.Should().Contain("\n");
    }

    [Fact] public async Task SaveAsync_LfDoc_SaveEolStyleCrLf_WritesCrLf()
    {
        var doc = new TextDocument();
        doc.Load("Line1\nLine2");
        doc.SaveEolStyle = EolStyle.CrLf;

        using var ms = new MemoryStream();
        await doc.SaveAsync(ms);

        ms.Position = 0;
        var saved = new StreamReader(ms, Encoding.UTF8).ReadToEnd();
        saved.Should().Be("Line1\r\nLine2");
    }

    [Fact] public async Task LoadFileAsync_SaveFileAsync_RoundTrip()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var original = "Line1\nLine2\nLine3";
            File.WriteAllText(tmp, original, Encoding.UTF8);

            var doc = new TextDocument();
            await doc.LoadFileAsync(tmp);
            doc.GetText().Should().Be(original);
            doc.FilePath.Should().Be(tmp);

            doc.Insert(doc.Length, "\nLine4");
            await doc.SaveFileAsync(tmp);

            var readBack = File.ReadAllText(tmp, Encoding.UTF8);
            readBack.Should().Be("Line1\nLine2\nLine3\nLine4");
        }
        finally { File.Delete(tmp); }
    }

    [Fact] public async Task LoadFileAsync_SetsIsModifiedFalse()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "content");
            var doc = new TextDocument();
            await doc.LoadFileAsync(tmp);
            doc.IsModified.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }

    [Fact] public async Task SaveFileAsync_SetsIsModifiedFalse()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            var doc = new TextDocument();
            doc.Load("Hello World");
            doc.Insert(0, ">> ");
            doc.IsModified.Should().BeTrue();
            await doc.SaveFileAsync(tmp);
            doc.IsModified.Should().BeFalse();
        }
        finally { File.Delete(tmp); }
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// EOL edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class EolEdgeCaseTests
{
    [Fact] public void Detect_EmptyString_ReturnsLf()
        => EolRegistry.Detect("".AsSpan()).Should().Be(EolStyle.Lf);

    [Fact] public void Detect_SingleNewline_ReturnsLf()
        => EolRegistry.Detect("\n".AsSpan()).Should().Be(EolStyle.Lf);

    [Fact] public void Detect_SingleCr_ReturnsCr()
        => EolRegistry.Detect("\r".AsSpan()).Should().Be(EolStyle.Cr);

    [Fact] public void Detect_CrLfThenLf_ReturnsMixed()
        => EolRegistry.Detect("a\r\nb\nc".AsSpan()).Should().Be(EolStyle.Mixed);

    [Fact] public void CountLf_EmptySpan_ReturnsZero()
        => EolRegistry.CountLf(ReadOnlySpan<char>.Empty).Should().Be(0);

    [Fact] public void CountLf_NoNewlines_ReturnsZero()
        => EolRegistry.CountLf("hello".AsSpan()).Should().Be(0);

    [Fact] public void CountLf_AllNewlines_ReturnsCount()
        => EolRegistry.CountLf("\n\n\n\n".AsSpan()).Should().Be(4);

    [Fact] public void NormaliseInsert_PureLf_ReturnsSameString()
    {
        // Fast path: no \r in string → returns original without allocation
        const string text = "hello\nworld";
        EolRegistry.NormaliseInsert(text).Should().Be(text);
    }

    [Fact] public void NormaliseInsert_CrOnly_BecomesLf()
        => EolRegistry.NormaliseInsert("hello\rworld").Should().Be("hello\nworld");

    [Fact] public void NormaliseInsert_MixedCrAndCrLf_AllBecomeLf()
        => EolRegistry.NormaliseInsert("a\rb\r\nc").Should().Be("a\nb\nc");

    [Fact] public void NormaliseInsert_EmptyString_ReturnsEmpty()
        => EolRegistry.NormaliseInsert("").Should().Be("");

    [Fact] public void NormaliseOnLoad_Mixed_NormalizedAndOriginalStyleIsMixed()
    {
        var reg = new EolRegistry();
        var result = reg.NormaliseOnLoad("a\nb\r\nc\rd");
        result.Should().Be("a\nb\nc\nd");
        reg.OriginalStyle.Should().Be(EolStyle.Mixed);
    }

    [Fact] public void RestoreEol_LfStyle_ReturnsUnchanged()
    {
        var reg = new EolRegistry();
        reg.NormaliseOnLoad("a\nb");           // pure LF doc
        reg.RestoreEol("a\nb").Should().Be("a\nb");
    }

    [Fact] public void OriginalEolStyle_PureText_DefaultsToLf()
    {
        var doc = new TextDocument();
        doc.Load("no line endings here");
        doc.OriginalEolStyle.Should().Be(EolStyle.Lf);
    }

    [Fact] public void SaveEolStyle_DefaultsToOriginalStyle()
    {
        var doc = new TextDocument();
        doc.Load("line1\r\nline2");
        doc.SaveEolStyle.Should().Be(EolStyle.CrLf);
    }

    [Fact] public void SaveEolStyle_CanBeOverridden()
    {
        var doc = new TextDocument();
        doc.Load("line1\r\nline2");
        doc.SaveEolStyle = EolStyle.Lf;
        doc.SaveEolStyle.Should().Be(EolStyle.Lf);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// Position mapping edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class PositionMappingEdgeCaseTests
{
    private static PieceTable Make(string s) { var pt = new PieceTable(); pt.Load(s); return pt; }

    [Fact] public void OffsetToPosition_NegativeOffset_Returns00()
        => Make("Hello").OffsetToPosition(-1).Should().Be((0, 0));

    [Fact] public void OffsetToPosition_ZeroOffset_Returns00()
        => Make("Hello\nWorld").OffsetToPosition(0).Should().Be((0, 0));

    [Fact] public void OffsetToPosition_MultiLineDocument_Line2()
    {
        var pt = Make("A\nB\nC");
        pt.OffsetToPosition(4).Should().Be((2, 0));
    }

    [Fact] public void OffsetToPosition_EndOfDocument_CorrectLine()
    {
        var pt = Make("Hello\nWorld");
        var (line, col) = pt.OffsetToPosition(pt.Length);
        line.Should().Be(1);
        col.Should().Be(5);
    }

    [Fact] public void PositionToOffset_LineEqualsLineCount_ReturnsDocLength()
    {
        var pt = Make("Hello\nWorld");
        pt.PositionToOffset(pt.LineCount, 0).Should().Be(pt.Length);
    }

    [Fact] public void PositionToOffset_Line0_Column0_Returns0()
        => Make("Hello\nWorld").PositionToOffset(0, 0).Should().Be(0);

    [Fact] public void PositionToOffset_Line1_Column0_ReturnsAfterNewline()
        => Make("Hello\nWorld").PositionToOffset(1, 0).Should().Be(6);

    [Fact] public void PositionToOffset_EmptyDoc_Line0Col0_Returns0()
        => Make("").PositionToOffset(0, 0).Should().Be(0);

    [Fact] public void OffsetToPosition_RoundTrip()
    {
        var pt = Make("Line0\nLine1\nLine2\nLine3");
        for (int offset = 0; offset <= pt.Length; offset++)
        {
            var (line, col) = pt.OffsetToPosition(offset);
            pt.PositionToOffset(line, col).Should().Be(offset, $"round-trip at offset {offset}");
        }
    }

    [Fact] public void PositionToOffset_AfterEdit_ReflectsNewContent()
    {
        var pt = Make("Hello");
        pt.Insert(5, "\nWorld");
        pt.PositionToOffset(1, 0).Should().Be(6);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// GetText slice edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class GetTextSliceTests
{
    private static PieceTable Make(string s) { var pt = new PieceTable(); pt.Load(s); return pt; }

    [Fact] public void GetText_ZeroLength_ReturnsEmpty()
        => Make("Hello").GetText(2, 0).Should().Be("");

    [Fact] public void GetText_NegativeLength_ReturnsEmpty()
        => Make("Hello").GetText(2, -1).Should().Be("");

    [Fact] public void GetText_FullDocument_MatchesGetText()
    {
        var pt = Make("Hello World");
        pt.GetText(0, pt.Length).Should().Be(pt.GetText());
    }

    [Fact] public void GetText_SingleChar_ReturnsOneChar()
        => Make("Hello").GetText(1, 1).Should().Be("e");

    [Fact] public void GetText_Slice_SpansPieceBoundary()
    {
        var pt = Make("Hello");
        pt.Insert(5, " World");
        // "Hello World" — slice "o W" spans the piece seam at offset 5
        pt.GetText(4, 3).Should().Be("o W");
    }

    [Fact] public void GetText_AfterEdit_ReflectsNewContent()
    {
        var pt = Make("Hello World");
        pt.Delete(5, 6);
        pt.GetText(0, 5).Should().Be("Hello");
    }

    [Fact] public void GetAllLines_MatchesGetLinePerLine()
    {
        var pt = Make("Line0\nLine1\nLine2");
        var allLines = pt.GetAllLines().ToList();
        allLines.Should().HaveCount(3);
        for (int i = 0; i < allLines.Count; i++)
        {
            allLines[i].Index.Should().Be(i);
            allLines[i].Content.Should().Be(pt.GetLine(i));
        }
    }

    [Fact] public void GetAllLines_EmptyDocument_OneEmptyLine()
    {
        var pt = Make("");
        var lines = pt.GetAllLines().ToList();
        lines.Should().HaveCount(1);
        lines[0].Content.Should().Be("");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// ReplaceAll — advanced scenarios
// ═══════════════════════════════════════════════════════════════════════════

public class ReplaceAllAdvancedTests
{
    // ── Replacement = empty (deletion) ───────────────────────────────────

    [Fact] public void ReplaceAll_WithEmptyReplacement_DeletesAllOccurrences()
    {
        var doc = new TextDocument();
        doc.Load("foo bar foo baz foo");
        doc.ReplaceAll("foo", "").Should().Be(3);
        doc.GetText().Should().Be(" bar  baz ");
    }

    [Fact] public void ReplaceAll_WithEmptyReplacement_Undoable()
    {
        var doc = new TextDocument();
        doc.Load("foo bar foo");
        doc.ReplaceAll("foo", "");
        doc.Undo();
        doc.GetText().Should().Be("foo bar foo");
    }

    // ── Case-insensitive ──────────────────────────────────────────────────

    [Fact] public void ReplaceAll_CaseInsensitive_ReplacesAllCasings()
    {
        var doc = new TextDocument();
        doc.Load("Foo FOO foo");
        int count = doc.ReplaceAll("foo", "bar", new SearchOptions { CaseSensitive = false });
        count.Should().Be(3);
        doc.GetText().Should().Be("bar bar bar");
    }

    [Fact] public void ReplaceAll_CaseSensitive_SkipsWrongCase()
    {
        var doc = new TextDocument();
        doc.Load("Foo FOO foo");
        int count = doc.ReplaceAll("foo", "bar");
        count.Should().Be(1);
        doc.GetText().Should().Be("Foo FOO bar");
    }

    // ── Regex ─────────────────────────────────────────────────────────────

    [Fact] public void ReplaceAll_Regex_ReplacesPatternMatches()
    {
        var doc = new TextDocument();
        doc.Load("abc123def456ghi789");
        int count = doc.ReplaceAll(@"\d+", "NUM", new SearchOptions { UseRegex = true });
        count.Should().Be(3);
        doc.GetText().Should().Be("abcNUMdefNUMghiNUM");
    }

    [Fact] public void ReplaceAll_Regex_IsUndoable()
    {
        var doc = new TextDocument();
        doc.Load("x1 x2 x3");
        doc.ReplaceAll(@"\d", "N", new SearchOptions { UseRegex = true });
        doc.Undo();
        doc.GetText().Should().Be("x1 x2 x3");
    }

    [Fact] public void ReplaceAll_Regex_WholeWordPattern()
    {
        var doc = new TextDocument();
        doc.Load("the theater the");
        int count = doc.ReplaceAll(@"\bthe\b", "a", new SearchOptions { UseRegex = true });
        count.Should().Be(2, "\"theater\" should not match");
        doc.GetText().Should().Be("a theater a");
    }

    // ── Replacement contains pattern (no infinite loop) ───────────────────

    [Fact] public void ReplaceAll_ReplacementContainsPattern_FiniteMutations()
    {
        // Replacing "foo" with "foobar" should do exactly N replacements, not loop
        var doc = new TextDocument();
        doc.Load("foo foo foo");
        int count = doc.ReplaceAll("foo", "foobar");
        count.Should().Be(3);
        doc.GetText().Should().Be("foobar foobar foobar");
    }

    // ── Empty pattern guard ────────────────────────────────────────────────

    [Fact] public void ReplaceAll_EmptyPattern_ReturnsZero_NoChange()
    {
        var doc = new TextDocument();
        doc.Load("hello world");
        doc.ReplaceAll("", "X").Should().Be(0);
        doc.GetText().Should().Be("hello world");
    }

    // ── Content correctness after replace ────────────────────────────────

    [Fact] public void ReplaceAll_ShorterReplacement_ContentCorrect()
    {
        var doc = new TextDocument();
        doc.Load("aaa bbb aaa");
        doc.ReplaceAll("aaa", "x");
        doc.GetText().Should().Be("x bbb x");
    }

    [Fact] public void ReplaceAll_LongerReplacement_ContentCorrect()
    {
        var doc = new TextDocument();
        doc.Load("x bbb x");
        doc.ReplaceAll("x", "aaa");
        doc.GetText().Should().Be("aaa bbb aaa");
    }

    [Fact] public void ReplaceAll_SingleMatch_Correct()
    {
        var doc = new TextDocument();
        doc.Load("hello world");
        doc.ReplaceAll("world", "Claude");
        doc.GetText().Should().Be("hello Claude");
    }

    [Fact] public void ReplaceAll_LineCountUpdatedCorrectly()
    {
        var doc = new TextDocument();
        doc.Load("line1\nline2\nline3");
        doc.ReplaceAll("\n", " ");   // collapse newlines — 3 lines → 1
        doc.LineCount.Should().Be(1);
        doc.GetText().Should().Be("line1 line2 line3");
    }

    [Fact] public void ReplaceAll_SearcherInvalidatedAfterReplace()
    {
        // After ReplaceAll, subsequent searches should reflect new content
        var doc = new TextDocument();
        doc.Load("foo bar foo");
        doc.ReplaceAll("foo", "qux");
        doc.FindFirst("foo").Should().BeNull("foo no longer exists");
        doc.FindFirst("qux").Should().NotBeNull();
    }

    // ── Consecutive replaces + undo chain ─────────────────────────────────

    [Fact] public void ReplaceAll_TwiceConsecutively_BothUndoable()
    {
        var doc = new TextDocument();
        doc.Load("aaa");
        doc.ReplaceAll("aaa", "bbb");
        doc.ReplaceAll("bbb", "ccc");
        doc.GetText().Should().Be("ccc");

        doc.Undo();
        doc.GetText().Should().Be("bbb");

        doc.Undo();
        doc.GetText().Should().Be("aaa");
    }
}
