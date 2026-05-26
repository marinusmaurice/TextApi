// @plugin
// Name: Document Stats
// Description: Prints detailed statistics about the current document — lines, words, graphemes, longest line, blank lines, and top 5 most frequent words.
// Tags: stats, info, utility
// @end

var stats = doc.GetStats();

// ── Basic counts ──────────────────────────────────────────────────────────
Print($"Lines     : {stats.LineCount}");
Print($"Words     : {stats.WordCount}");
Print($"Graphemes : {stats.GraphemeCount}");
Print($"Code units: {stats.CodeUnitCount}");

// ── Blank lines ───────────────────────────────────────────────────────────
int blank = Enumerable.Range(0, doc.LineCount).Count(i => doc.GetLine(i).Trim().Length == 0);
Print($"Blank     : {blank}");

// ── Longest line ──────────────────────────────────────────────────────────
int longestIdx = 0, longestLen = 0;
for (int i = 0; i < doc.LineCount; i++)
{
    int len = doc.GetLine(i).Length;
    if (len > longestLen) { longestLen = len; longestIdx = i; }
}
Print($"Longest   : line {longestIdx} ({longestLen} chars)");

// ── Top-5 words ───────────────────────────────────────────────────────────
var wordFreq = System.Text.RegularExpressions.Regex
    .Matches(doc.GetText().ToLowerInvariant(), @"\b[a-z]{2,}\b")
    .GroupBy(m => m.Value)
    .OrderByDescending(g => g.Count())
    .Take(5)
    .ToList();

if (wordFreq.Count > 0)
{
    Print("Top words :");
    foreach (var g in wordFreq)
        Print($"  {g.Key,-20} × {g.Count()}");
}
