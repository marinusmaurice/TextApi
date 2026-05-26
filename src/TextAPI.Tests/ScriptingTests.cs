using FluentAssertions;
using TextAPI.Core;
using TextAPI.Core.Scripting;
using Xunit;

namespace TextAPI.Tests;

// ═══════════════════════════════════════════════════════════════════════════
// Helpers
// ═══════════════════════════════════════════════════════════════════════════

file static class SR
{
    public static (TextDocument doc, ScriptRunner runner) Make(string text = "")
    {
        var doc = new TextDocument();
        if (!string.IsNullOrEmpty(text)) doc.Load(text);
        return (doc, new ScriptRunner(doc));
    }

    public static ScriptResult Run(string text, string script)
    {
        var (_, runner) = Make(text);
        return runner.Run(script);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 1. Parser
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptParserTests
{
    [Fact] public void Empty_Script_ParsesOk()
    {
        var r = ScriptParser.Parse("");
        r.Success.Should().BeTrue();
        r.Commands.Should().BeEmpty();
    }

    [Fact] public void Comment_Lines_Ignored()
    {
        var r = ScriptParser.Parse("# this is a comment\n# another");
        r.Success.Should().BeTrue();
        r.Commands.Should().BeEmpty();
    }

    [Fact] public void Blank_Lines_Ignored()
    {
        var r = ScriptParser.Parse("\n\n\nMOVE 5\n\n");
        r.Success.Should().BeTrue();
        r.Commands.Should().HaveCount(1);
    }

    [Fact] public void Verb_Is_UpperCased()
    {
        var r = ScriptParser.Parse("move 0");
        r.Commands[0].Verb.Should().Be("MOVE");
    }

    [Fact] public void Integer_Arg_Parsed()
    {
        var r = ScriptParser.Parse("MOVE 42");
        var arg = r.Commands[0].Args[0];
        arg.Kind.Should().Be(ScriptArgKind.Number);
        arg.Int.Should().Be(42);
    }

    [Fact] public void Negative_Integer_Arg_Parsed()
    {
        var r = ScriptParser.Parse("DELETE -1");
        r.Commands[0].Args[0].Int.Should().Be(-1);
    }

    [Fact] public void String_Arg_Parsed()
    {
        var r = ScriptParser.Parse("INSERT \"hello world\"");
        var arg = r.Commands[0].Args[0];
        arg.Kind.Should().Be(ScriptArgKind.Text);
        arg.Str.Should().Be("hello world");
    }

    [Fact] public void String_Escape_Sequences()
    {
        var r = ScriptParser.Parse("INSERT \"a\\nb\\tc\"");
        r.Commands[0].Args[0].Str.Should().Be("a\nb\tc");
    }

    [Fact] public void Flag_Arg_Parsed()
    {
        var r = ScriptParser.Parse("FIND \"x\" /i /w");
        r.Commands[0].Args[1].Kind.Should().Be(ScriptArgKind.Flag);
        r.Commands[0].Args[1].Str.Should().Be("i");
        r.Commands[0].Args[2].Str.Should().Be("w");
    }

    [Fact] public void Unterminated_String_IsError()
    {
        var r = ScriptParser.Parse("INSERT \"oops");
        r.Success.Should().BeFalse();
        r.Errors[0].Line.Should().Be(1);
    }

    [Fact] public void MultiLine_Script_ParsesAllCommands()
    {
        var r = ScriptParser.Parse("MOVE 0\nINSERT \"hello\"\nDELETE 3");
        r.Commands.Should().HaveCount(3);
        r.Commands[0].Verb.Should().Be("MOVE");
        r.Commands[1].Verb.Should().Be("INSERT");
        r.Commands[2].Verb.Should().Be("DELETE");
    }

    [Fact] public void InlineComment_Stops_ArgParsing()
    {
        var r = ScriptParser.Parse("MOVE 5 # move to 5");
        r.Commands[0].Args.Should().HaveCount(1);
        r.Commands[0].Args[0].Int.Should().Be(5);
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 2. MOVE / GOTO
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptMoveTests
{
    [Fact] public void Move_SetsCursor()
    {
        var r = SR.Run("hello world", "MOVE 5");
        r.Success.Should().BeTrue();
        r.CursorOffset.Should().Be(5);
    }

    [Fact] public void Move_ToDocumentEnd()
    {
        var r = SR.Run("hello", "MOVE 5");
        r.CursorOffset.Should().Be(5);
    }

    [Fact] public void Move_OutOfRange_Fails()
    {
        var r = SR.Run("hello", "MOVE 99");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("out of range");
    }

    [Fact] public void Goto_Line1_Col0()
    {
        var r = SR.Run("hello\nworld", "GOTO 1 0");
        r.Success.Should().BeTrue();
        r.CursorOffset.Should().Be(0);
    }

    [Fact] public void Goto_Line2_Col0()
    {
        var r = SR.Run("hello\nworld", "GOTO 2 0");
        r.Success.Should().BeTrue();
        r.CursorOffset.Should().Be(6);
    }

    [Fact] public void Goto_DefaultCol_IsZero()
    {
        var r = SR.Run("hello\nworld", "GOTO 2");
        r.CursorOffset.Should().Be(6);
    }

    [Fact] public void Goto_BeyondLines_Fails()
    {
        var r = SR.Run("hello", "GOTO 99");
        r.Success.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 3. INSERT
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptInsertTests
{
    [Fact] public void Insert_AtCursor_Start()
    {
        var r = SR.Run("world", "INSERT \"hello \"");
        r.Success.Should().BeTrue();
    }

    [Fact] public void Insert_At_Cursor_UpdatesText()
    {
        var (doc, runner) = SR.Make("world");
        runner.Run("INSERT \"hello \"");
        doc.GetText().Should().Be("hello world");
    }

    [Fact] public void Insert_AdvancesCursor()
    {
        var r = SR.Run("", "INSERT \"abc\"");
        r.CursorOffset.Should().Be(3);
    }

    [Fact] public void InsertAt_ExplicitOffset()
    {
        var (doc, runner) = SR.Make("helloworld");
        runner.Run("INSERT_AT 5 \" \"");
        doc.GetText().Should().Be("hello world");
    }

    [Fact] public void InsertAt_BeforeCursor_ShiftsCursor()
    {
        var (doc, runner) = SR.Make("abc");
        runner.Run("MOVE 3\nINSERT_AT 0 \"XX\"");
        runner.CursorOffset.Should().Be(5);   // 3 + 2 inserted before
    }

    [Fact] public void Insert_Newline()
    {
        var (doc, runner) = SR.Make("ab");
        runner.Run("MOVE 1\nINSERT \"\\n\"");
        doc.GetText().Should().Be("a\nb");
    }

    [Fact] public void Insert_EmptyString_NoChange()
    {
        var (doc, runner) = SR.Make("hello");
        var r = runner.Run("INSERT \"\"");
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 4. DELETE / DELETE_AT / DELETE_LINE
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptDeleteTests
{
    [Fact] public void Delete_RemovesCharsAtCursor()
    {
        var (doc, runner) = SR.Make("hello world");
        runner.Run("MOVE 5\nDELETE 6");
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void Delete_AtEnd_NoChange()
    {
        var (doc, runner) = SR.Make("hello");
        var r = runner.Run("MOVE 5\nDELETE 10");
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void DeleteAt_RemovesCharsAtOffset()
    {
        var (doc, runner) = SR.Make("foobar");
        runner.Run("DELETE_AT 3 3");
        doc.GetText().Should().Be("foo");
    }

    [Fact] public void DeleteAt_BeforeCursor_ShiftsCursor()
    {
        var (doc, runner) = SR.Make("abcdef");
        runner.Run("MOVE 4\nDELETE_AT 0 2");
        runner.CursorOffset.Should().Be(2);   // 4 - 2 deleted before
    }

    [Fact] public void DeleteLine_RemovesLineAndNewline()
    {
        var (doc, runner) = SR.Make("line1\nline2\nline3");
        runner.Run("DELETE_LINE 2");
        doc.GetText().Should().Be("line1\nline3");
    }

    [Fact] public void DeleteLine_LastLine_NoTrailingNewline()
    {
        var (doc, runner) = SR.Make("line1\nline2");
        runner.Run("DELETE_LINE 2");
        doc.GetText().Should().Be("line1");
    }

    [Fact] public void DeleteLine_Default_UsesCurrentLine()
    {
        var (doc, runner) = SR.Make("line1\nline2\nline3");
        runner.Run("GOTO 2\nDELETE_LINE");
        doc.GetText().Should().Be("line1\nline3");
    }

    [Fact] public void DeleteLine_BeyondEnd_Fails()
    {
        var r = SR.Run("hello", "DELETE_LINE 99");
        r.Success.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 5. SELECT
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptSelectTests
{
    [Fact] public void Select_SetsCursorAndEnd()
    {
        var (doc, runner) = SR.Make("hello world");
        var r = runner.Run("SELECT 0 5");
        r.Success.Should().BeTrue();
        r.CursorOffset.Should().Be(0);
    }

    [Fact] public void Select_InvalidRange_Fails()
    {
        var r = SR.Run("hello", "SELECT 4 2");
        r.Success.Should().BeFalse();
    }

    [Fact] public void Select_OutOfRange_Fails()
    {
        var r = SR.Run("hello", "SELECT 0 99");
        r.Success.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 6. REPLACE_ALL
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptReplaceAllTests
{
    [Fact] public void ReplaceAll_ReplacesLiteral()
    {
        var (doc, runner) = SR.Make("foo foo foo");
        var r = runner.Run("REPLACE_ALL \"foo\" \"bar\"");
        r.Success.Should().BeTrue();
        r.LastReplaceCount.Should().Be(3);
        doc.GetText().Should().Be("bar bar bar");
    }

    [Fact] public void ReplaceAll_CaseInsensitive()
    {
        var (doc, runner) = SR.Make("Foo FOO foo");
        runner.Run("REPLACE_ALL \"foo\" \"X\" /i");
        doc.GetText().Should().Be("X X X");
    }

    [Fact] public void ReplaceAll_Regex()
    {
        var (doc, runner) = SR.Make("a1 b2 c3");
        runner.Run("REPLACE_ALL \"\\\\d\" \"N\" /r");
        doc.GetText().Should().Be("aN bN cN");
    }

    [Fact] public void ReplaceAll_WholeWord()
    {
        var (doc, runner) = SR.Make("foo foobar foofoo");
        runner.Run("REPLACE_ALL \"foo\" \"X\" /w");
        doc.GetText().Should().Be("X foobar foofoo");
    }

    [Fact] public void ReplaceAll_NoMatch_ZeroCount()
    {
        var (doc, runner) = SR.Make("hello");
        var r = runner.Run("REPLACE_ALL \"xyz\" \"A\"");
        r.LastReplaceCount.Should().Be(0);
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void ReplaceAll_MissingArgs_Fails()
    {
        var r = SR.Run("hello", "REPLACE_ALL \"foo\"");
        r.Success.Should().BeFalse();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 7. FIND / FIND_PREV / FIND_ALL
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptFindTests
{
    [Fact] public void Find_MovesCursorToMatch()
    {
        var r = SR.Run("hello world", "FIND \"world\"");
        r.Success.Should().BeTrue();
        r.CursorOffset.Should().Be(6);
    }

    [Fact] public void Find_FromCursor_SkipsBefore()
    {
        var (doc, runner) = SR.Make("ab ab ab");
        runner.Run("MOVE 3\nFIND \"ab\"");
        runner.CursorOffset.Should().Be(3);
    }

    [Fact] public void Find_NotFound_Fails()
    {
        var r = SR.Run("hello", "FIND \"xyz\"");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("not found");
    }

    [Fact] public void Find_CaseInsensitive()
    {
        var r = SR.Run("Hello World", "FIND \"hello\" /i");
        r.CursorOffset.Should().Be(0);
    }

    [Fact] public void FindPrev_MovesCursorToPreviousMatch()
    {
        var (doc, runner) = SR.Make("foo bar foo");
        runner.Run("MOVE 11\nFIND_PREV \"foo\"");
        runner.CursorOffset.Should().Be(8);
    }

    [Fact] public void FindPrev_NotFound_Fails()
    {
        var r = SR.Run("hello", "FIND_PREV \"xyz\"");
        r.Success.Should().BeFalse();
    }

    [Fact] public void FindAll_ReturnsAllMatches()
    {
        var r = SR.Run("foo bar foo baz foo", "FIND_ALL \"foo\"");
        r.Success.Should().BeTrue();
        r.LastFindAll.Should().HaveCount(3);
        r.LastFindAll[0].Offset.Should().Be(0);
        r.LastFindAll[1].Offset.Should().Be(8);
        r.LastFindAll[2].Offset.Should().Be(16);
    }

    [Fact] public void FindAll_NoMatch_EmptyList()
    {
        var r = SR.Run("hello", "FIND_ALL \"xyz\"");
        r.Success.Should().BeTrue();
        r.LastFindAll.Should().BeEmpty();
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 8. UNDO / REDO
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptUndoRedoTests
{
    [Fact] public void Undo_RevertsInsert()
    {
        var (doc, runner) = SR.Make("hello");
        runner.Run("MOVE 5\nINSERT \" world\"");
        doc.GetText().Should().Be("hello world");
        runner.Run("UNDO");
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void Redo_ReappliesInsert()
    {
        var (doc, runner) = SR.Make("hello");
        runner.Run("MOVE 5\nINSERT \" world\"");
        runner.Run("UNDO");
        runner.Run("REDO");
        doc.GetText().Should().Be("hello world");
    }

    [Fact] public void Undo_NothingToUndo_Fails()
    {
        var r = SR.Run("hello", "UNDO");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Nothing to undo");
    }

    [Fact] public void Redo_NothingToRedo_Fails()
    {
        var r = SR.Run("hello", "REDO");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Nothing to redo");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 9. Multi-step scripts / integration
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptIntegrationTests
{
    [Fact] public void MultiStep_InsertsAndMoves()
    {
        var (doc, runner) = SR.Make("Hello");
        var r = runner.Run("""
            MOVE 5
            INSERT ", World"
            """);
        r.Success.Should().BeTrue();
        r.StepsExecuted.Should().Be(2);
        doc.GetText().Should().Be("Hello, World");
    }

    [Fact] public void StepResults_TrackCursorPerStep()
    {
        var (doc, runner) = SR.Make("");
        var r = runner.Run("INSERT \"ab\"\nINSERT \"c\"");
        r.Steps[0].CursorAfter.Should().Be(2);
        r.Steps[1].CursorAfter.Should().Be(3);
    }

    [Fact] public void Error_StopsExecution()
    {
        var (doc, runner) = SR.Make("hello");
        var r = runner.Run("MOVE 5\nINSERT \" world\"\nMOVE 999\nINSERT \" more\"");
        r.Success.Should().BeFalse();
        r.StepsExecuted.Should().Be(3);   // step 1 ok, step 2 ok, step 3 fails, step 4 not reached
        doc.GetText().Should().Be("hello world");  // steps 1-2 already applied
    }

    [Fact] public void ErrorLine_ReportsCorrectLine()
    {
        var r = SR.Run("hello", "MOVE 0\nMOVE 999");
        r.ErrorLine.Should().Be(2);
    }

    [Fact] public void UnknownVerb_Fails()
    {
        var r = SR.Run("hello", "FROBNICATE 42");
        r.Success.Should().BeFalse();
        r.ErrorMessage.Should().Contain("Unknown command");
    }

    [Fact] public void NOP_SucceedsWithNoChange()
    {
        var (doc, runner) = SR.Make("hello");
        var r = runner.Run("NOP");
        r.Success.Should().BeTrue();
        doc.GetText().Should().Be("hello");
    }

    [Fact] public void RealWorldMacro_CommentDeletion()
    {
        // Remove C-style single-line comments from each line of a 3-line "file"
        const string source = "int x = 1; // set x\nint y = 2; // set y\nreturn x + y;";
        var (doc, runner) = SR.Make(source);

        // For each line, find the comment start and delete to end of line
        var r = runner.Run("""
            GOTO 1
            FIND " //"
            DELETE 9
            GOTO 2
            FIND " //"
            DELETE 9
            """);
        r.Success.Should().BeTrue();
        doc.GetText().Should().Contain("int x = 1;\nint y = 2;\nreturn x + y;");
    }

    [Fact] public void Regex_ReplaceAll_MatchesComplexPattern()
    {
        var (doc, runner) = SR.Make("2024-01-15 and 2025-12-31");
        runner.Run("REPLACE_ALL \"\\\\d{4}-\\\\d{2}-\\\\d{2}\" \"DATE\" /r");
        doc.GetText().Should().Be("DATE and DATE");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// 10. Edge cases
// ═══════════════════════════════════════════════════════════════════════════

public class ScriptEdgeCaseTests
{
    [Fact] public void EmptyScript_Succeeds()
    {
        var r = SR.Run("hello", "");
        r.Success.Should().BeTrue();
        r.StepsExecuted.Should().Be(0);
    }

    [Fact] public void OnlyComments_Succeeds()
    {
        var r = SR.Run("hello", "# just a comment");
        r.Success.Should().BeTrue();
        r.StepsExecuted.Should().Be(0);
    }

    [Fact] public void ParseError_ReportedBeforeExecution()
    {
        var r = SR.Run("hello", "INSERT \"unterminated");
        r.Success.Should().BeFalse();
        r.StepsExecuted.Should().Be(0);   // parse failed — nothing executed
    }

    [Fact] public void Insert_CrLf_IsNormalized()
    {
        // TextDocument.Insert passes text through to PieceTable which normalises CRLF → LF
        var (doc, runner) = SR.Make("ab");
        runner.Run("MOVE 1\nINSERT \"\\r\\n\"");
        doc.GetText().Should().Be("a\nb");
    }

    [Fact] public void Goto_LineAndColumn_ReachCorrectOffset()
    {
        // "Hello\nWorld" — line 2 col 3 = "lo" start = offset 9
        var r = SR.Run("Hello\nWorld", "GOTO 2 3");
        r.CursorOffset.Should().Be(9);
    }

    [Fact] public void Delete_NegativeCount_Fails()
    {
        var r = SR.Run("hello", "DELETE -1");
        r.Success.Should().BeFalse();
    }

    [Fact] public void Move_ZeroOffset_SetsCursorToStart()
    {
        var (doc, runner) = SR.Make("hello");
        runner.Run("MOVE 3\nMOVE 0");
        runner.CursorOffset.Should().Be(0);
    }
}
