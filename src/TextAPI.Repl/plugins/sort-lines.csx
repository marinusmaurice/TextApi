// @plugin
// Name: Sort Lines
// Description: Sorts all lines in the document alphabetically (case-insensitive). Undo with doc.Undo().
// Tags: sort, lines, utility
// @end

var lines = Enumerable.Range(0, doc.LineCount)
    .Select(i => doc.GetLine(i))
    .ToList();

var sorted = lines.OrderBy(l => l, StringComparer.OrdinalIgnoreCase).ToList();

if (lines.SequenceEqual(sorted, StringComparer.OrdinalIgnoreCase))
{
    Print("Already sorted — nothing to do.");
}
else
{
    doc.Load(string.Join("\n", sorted));
    Print($"Sorted {doc.LineCount} lines.");
}
