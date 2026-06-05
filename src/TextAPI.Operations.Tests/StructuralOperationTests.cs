using FluentAssertions;
using TextAPI.Core;
using TextAPI.Operations;
using TextAPI.Operations.Pipeline;
using Xunit;

namespace TextAPI.Operations.Tests;

file static class Helpers
{
    public static TextDocument Doc(string text)
    {
        var d = new TextDocument();
        d.Load(text);
        return d;
    }

    public static PipelineResult Run(TextDocument doc, params IDocumentOperation[] ops)
    {
        var p = new DocumentPipeline(doc);
        foreach (var op in ops) p.Add(op);
        return p.Execute();
    }
}

public class StructuralOperationTests
{
    // ── MoveSection ───────────────────────────────────────────────────────────

    [Fact]
    public void MoveSection_HappyPath_MovesTextFromSourceToDestination()
    {
        var doc = Helpers.Doc("intro [S]moved[E] middle [DEST] end");

        var result = new MoveSectionOperation
        {
            SourceStartAnchor = "[S]",
            SourceEndAnchor = "[E]",
            DestinationAnchor = "[DEST]",
            InsertAfterDestination = true
        }.Execute(doc);

        result.Success.Should().BeTrue();
        var text = doc.GetText();
        text.Should().Contain("[DEST][S]moved[E]");
        // Original position should no longer have the moved content
        text.Should().NotContain("intro [S]moved[E] middle");
    }

    [Fact]
    public void MoveSection_InsertBeforeDestination_PlacesTextBeforeAnchor()
    {
        var doc = Helpers.Doc("[S]section[E] gap [DEST]");

        var result = new MoveSectionOperation
        {
            SourceStartAnchor = "[S]",
            SourceEndAnchor = "[E]",
            DestinationAnchor = "[DEST]",
            InsertAfterDestination = false
        }.Execute(doc);

        result.Success.Should().BeTrue();
        doc.GetText().Should().Contain("[S]section[E][DEST]");
    }

    [Fact]
    public void MoveSection_SourceStartNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("content [DEST] more");

        var result = new MoveSectionOperation
        {
            SourceStartAnchor = "[MISSING]",
            SourceEndAnchor = "[E]",
            DestinationAnchor = "[DEST]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("MISSING");
    }

    [Fact]
    public void MoveSection_DestinationNotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[S]source[E] content");

        var result = new MoveSectionOperation
        {
            SourceStartAnchor = "[S]",
            SourceEndAnchor = "[E]",
            DestinationAnchor = "[NOWHERE]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void MoveSection_TextRemovedFromSource()
    {
        var doc = Helpers.Doc("A [S]remove me[E] B [DEST] C");

        new MoveSectionOperation
        {
            SourceStartAnchor = "[S]",
            SourceEndAnchor = "[E]",
            DestinationAnchor = "[DEST]",
            InsertAfterDestination = true
        }.Execute(doc);

        // The text between anchors should no longer be at original position
        // (anchors themselves move)
        doc.GetText().Should().NotContain("A [S]remove me[E] B");
    }

    // ── SwapSections ──────────────────────────────────────────────────────────

    [Fact]
    public void SwapSections_HappyPath_SwapsContentOfBothSections()
    {
        var doc = Helpers.Doc("[S1]hello[E1] gap [S2]world[E2]");

        var result = new SwapSectionsOperation
        {
            Section1Start = "[S1]",
            Section1End = "[E1]",
            Section2Start = "[S2]",
            Section2End = "[E2]"
        }.Execute(doc);

        result.Success.Should().BeTrue();
        var text = doc.GetText();
        // Each section's full span (including anchors) is swapped:
        // pos1 now holds the original section2 text "[S2]world[E2]"
        // pos2 now holds the original section1 text "[S1]hello[E1]"
        text.Should().StartWith("[S2]world[E2]");
        text.Should().EndWith("[S1]hello[E1]");
    }

    [Fact]
    public void SwapSections_Section1NotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[S2]world[E2]");

        var result = new SwapSectionsOperation
        {
            Section1Start = "[S1]",
            Section1End = "[E1]",
            Section2Start = "[S2]",
            Section2End = "[E2]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void SwapSections_Section2NotFound_ReturnsFail()
    {
        var doc = Helpers.Doc("[S1]hello[E1] gap");

        var result = new SwapSectionsOperation
        {
            Section1Start = "[S1]",
            Section1End = "[E1]",
            Section2Start = "[S2]",
            Section2End = "[E2]"
        }.Execute(doc);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public void SwapSections_OverlappingSections_ReturnsFail()
    {
        // Sections share the anchor [E1]/[S2] which makes them overlap in concept;
        // here we simulate an actual character-level overlap using same start marker
        var doc = Helpers.Doc("[A]content[B]more[C]");

        // S1 = [A]..[B], S2 = [A]..[C] — section1 start is same as section2 start => overlap
        var result = new SwapSectionsOperation
        {
            Section1Start = "[A]",
            Section1End = "[B]",
            Section2Start = "[A]",
            Section2End = "[C]"
        }.Execute(doc);

        // Section 1 must appear before section 2 check - S2 is found searching after S1's end,
        // so S2 start = [A] which appears before S1's [B] end => should fail with overlap or ordering error
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void SwapSections_ReturnsSymmetricInsertedAndDeleted()
    {
        var doc = Helpers.Doc("[S1]AAA[E1] gap [S2]BB[E2]");

        var result = new SwapSectionsOperation
        {
            Section1Start = "[S1]",
            Section1End = "[E1]",
            Section2Start = "[S2]",
            Section2End = "[E2]"
        }.Execute(doc);

        // Total chars inserted == total chars deleted (sections swapped, same chars)
        result.CharsInserted.Should().Be(result.CharsDeleted);
    }
}
