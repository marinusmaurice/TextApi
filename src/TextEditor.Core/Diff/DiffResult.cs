using System.Text;

namespace TextEditor.Core.Diff;

/// <summary>
/// The result of a line-level diff between two documents.
/// Contains an ordered sequence of <see cref="DiffHunk"/> values that,
/// when applied in order, transform the old document into the new one.
/// </summary>
public sealed class DiffResult
{
    private readonly IReadOnlyList<DiffHunk> _hunks;

    internal DiffResult(IReadOnlyList<DiffHunk> hunks, int addedLines, int deletedLines)
    {
        _hunks       = hunks;
        AddedLines   = addedLines;
        DeletedLines = deletedLines;
    }

    /// <summary>All hunks in document order.</summary>
    public IReadOnlyList<DiffHunk> Hunks => _hunks;

    /// <summary>Total number of lines inserted in the new document.</summary>
    public int AddedLines { get; }

    /// <summary>Total number of lines deleted from the old document.</summary>
    public int DeletedLines { get; }

    /// <summary>True when the two documents differ.</summary>
    public bool HasChanges => AddedLines > 0 || DeletedLines > 0;

    // ── Unified diff ──────────────────────────────────────────────────────

    /// <summary>
    /// Render the diff in unified-diff format (as produced by <c>git diff</c>).
    /// Returns an empty string when the documents are identical.
    /// </summary>
    /// <param name="oldPath">Label used in the <c>---</c> header.</param>
    /// <param name="newPath">Label used in the <c>+++</c> header.</param>
    /// <param name="contextLines">Number of unchanged lines to show before and after each hunk.</param>
    public string ToUnifiedDiff(string oldPath = "a",
                                string newPath = "b",
                                int contextLines = 3)
    {
        if (!HasChanges) return string.Empty;

        // ── Phase 1: flatten hunks into individual line entries ───────────
        // Each entry: prefix (' ', '-', '+'), 0-based old-line index, 0-based new-line index, text.
        var flat = new List<(char Prefix, int OldLine, int NewLine, string Text)>(
            _hunks.Sum(h => Math.Max(h.OldCount, h.NewCount)));

        int ol = 0, nl = 0;
        foreach (var hunk in _hunks)
        {
            switch (hunk.Kind)
            {
                case DiffKind.Equal:
                    for (int k = 0; k < hunk.OldCount; k++)
                        flat.Add((' ', ol++, nl++, hunk.Lines[k]));
                    break;

                case DiffKind.Delete:
                    for (int k = 0; k < hunk.OldCount; k++)
                        flat.Add(('-', ol++, nl, hunk.Lines[k]));
                    break;

                case DiffKind.Insert:
                    for (int k = 0; k < hunk.NewCount; k++)
                        flat.Add(('+', ol, nl++, hunk.Lines[k]));
                    break;
            }
        }

        // ── Phase 2: mark lines that should be included ───────────────────
        // Every changed line is included, plus up to contextLines equal lines
        // on each side.
        var include = new bool[flat.Count];
        for (int i = 0; i < flat.Count; i++)
        {
            if (flat[i].Prefix == ' ') continue;
            include[i] = true;
            for (int j = Math.Max(0, i - contextLines); j < i; j++)             include[j] = true;
            for (int j = i + 1; j < Math.Min(flat.Count, i + 1 + contextLines); j++) include[j] = true;
        }

        // ── Phase 3: emit sections ────────────────────────────────────────
        var sb = new StringBuilder();
        sb.Append($"--- {oldPath}\n+++ {newPath}\n");

        int idx = 0;
        while (idx < flat.Count)
        {
            if (!include[idx]) { idx++; continue; }

            // Collect the contiguous included block.
            int sectionStart = idx;
            while (idx < flat.Count && include[idx]) idx++;
            int sectionEnd = idx;

            // Compute @@ header values.
            int oldFirst  = flat[sectionStart].OldLine;
            int newFirst  = flat[sectionStart].NewLine;
            int oldCount  = 0, newCount = 0;

            for (int k = sectionStart; k < sectionEnd; k++)
            {
                if (flat[k].Prefix != '+') oldCount++;
                if (flat[k].Prefix != '-') newCount++;
            }

            // Unified diff uses 1-based line numbers; 0-count ranges use the
            // insertion/deletion point (which matches git behaviour).
            string oldRange = oldCount == 0
                ? $"{oldFirst},0"
                : $"{oldFirst + 1},{oldCount}";
            string newRange = newCount == 0
                ? $"{newFirst},0"
                : $"{newFirst + 1},{newCount}";

            sb.Append($"@@ -{oldRange} +{newRange} @@\n");

            for (int k = sectionStart; k < sectionEnd; k++)
                sb.Append($"{flat[k].Prefix}{flat[k].Text}\n");
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"DiffResult +{AddedLines}/-{DeletedLines} ({_hunks.Count} hunks)";
}
