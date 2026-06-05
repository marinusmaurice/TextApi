using System.Text.Json.Serialization;
using TextAPI.Core;
using TextAPI.Core.Search;

namespace TextAPI.Operations;

/// <summary>
/// Cuts a section of text (defined by two anchors) and inserts it at a destination anchor.
/// After the cut, the destination anchor is re-resolved on the modified document.
/// </summary>
public sealed class MoveSectionOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "MOVE_SECTION";

    public string SourceStartAnchor { get; init; } = "";
    public string SourceEndAnchor { get; init; } = "";
    public string DestinationAnchor { get; init; } = "";
    public bool InsertAfterDestination { get; init; } = true;

    public OperationResult Execute(TextDocument doc)
    {
        // Resolve source section
        var srcStart = AnchorHelper.FindAnchor(doc, SourceStartAnchor, 1, caseSensitive: true);
        if (srcStart is null)
            return OperationResult.Fail($"Source start anchor '{SourceStartAnchor}' not found");

        int srcSearchFrom = srcStart.Value.Offset + srcStart.Value.Length;
        var opts = new SearchOptions { CaseSensitive = true };
        SearchMatch? srcEnd = null;
        foreach (var m in doc.FindAll(SourceEndAnchor, opts))
        {
            if (m.Offset >= srcSearchFrom)
            {
                srcEnd = m;
                break;
            }
        }
        if (srcEnd is null)
            return OperationResult.Fail($"Source end anchor '{SourceEndAnchor}' not found");

        int sectionOffset = srcStart.Value.Offset;
        int sectionEnd = srcEnd.Value.Offset + srcEnd.Value.Length;
        int sectionLength = sectionEnd - sectionOffset;
        string sectionText = doc.GetText(sectionOffset, sectionLength);

        // Cut the source section
        doc.Delete(sectionOffset, sectionLength);

        // Re-resolve destination anchor on the modified document (position may have shifted)
        var dest = AnchorHelper.FindAnchor(doc, DestinationAnchor, 1, caseSensitive: true);
        if (dest is null)
        {
            // Destination not found after cut — attempt to recover would be complex; roll-up to caller
            return OperationResult.Fail($"Destination anchor '{DestinationAnchor}' not found after cutting source section");
        }

        int insertAt = InsertAfterDestination
            ? dest.Value.Offset + dest.Value.Length
            : dest.Value.Offset;

        doc.Insert(insertAt, sectionText);
        return OperationResult.Ok(charsInserted: sectionText.Length, charsDeleted: sectionLength, resolvedOffset: insertAt);
    }
}

/// <summary>
/// Swaps the content of two non-overlapping sections defined by anchor pairs.
/// Section 1 must appear before section 2 in the document.
/// Section 2 is replaced first to avoid offset drift.
/// </summary>
public sealed class SwapSectionsOperation : IDocumentOperation
{
    [JsonPropertyName("type")]
    public string Type => "SWAP_SECTIONS";

    public string Section1Start { get; init; } = "";
    public string Section1End { get; init; } = "";
    public string Section2Start { get; init; } = "";
    public string Section2End { get; init; } = "";

    private static (SearchMatch Start, SearchMatch End)? ResolveSection(
        TextDocument doc, string startAnchor, string endAnchor, int searchAfter = 0)
    {
        var opts = new SearchOptions { CaseSensitive = true };

        SearchMatch? start = null;
        foreach (var m in doc.FindAll(startAnchor, opts))
        {
            if (m.Offset >= searchAfter)
            {
                start = m;
                break;
            }
        }
        if (start is null) return null;

        int endSearch = start.Value.Offset + start.Value.Length;
        SearchMatch? end = null;
        foreach (var m in doc.FindAll(endAnchor, opts))
        {
            if (m.Offset >= endSearch)
            {
                end = m;
                break;
            }
        }
        if (end is null) return null;

        return (start.Value, end.Value);
    }

    public OperationResult Execute(TextDocument doc)
    {
        var sec1 = ResolveSection(doc, Section1Start, Section1End);
        if (sec1 is null)
            return OperationResult.Fail($"Section 1 ('{Section1Start}'..'{Section1End}') not found");

        var sec2 = ResolveSection(doc, Section2Start, Section2End, searchAfter: sec1.Value.End.Offset + sec1.Value.End.Length);
        if (sec2 is null)
            return OperationResult.Fail($"Section 2 ('{Section2Start}'..'{Section2End}') not found");

        int s1Start = sec1.Value.Start.Offset;
        int s1End = sec1.Value.End.Offset + sec1.Value.End.Length;
        int s2Start = sec2.Value.Start.Offset;
        int s2End = sec2.Value.End.Offset + sec2.Value.End.Length;

        if (s1Start >= s2Start)
            return OperationResult.Fail("Section 1 must appear before section 2 in the document");

        if (s1End > s2Start)
            return OperationResult.Fail("Sections overlap; cannot swap");

        string text1 = doc.GetText(s1Start, s1End - s1Start);
        string text2 = doc.GetText(s2Start, s2End - s2Start);

        // Replace section 2 first (higher offset) to avoid shifting section 1's position
        doc.Replace(s2Start, s2End - s2Start, text1);
        doc.Replace(s1Start, s1End - s1Start, text2);

        return OperationResult.Ok(
            charsInserted: text1.Length + text2.Length,
            charsDeleted: text1.Length + text2.Length);
    }
}
