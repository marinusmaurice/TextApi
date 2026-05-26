namespace TextAPI.Core.Language;

/// <summary>
/// Toggles line comments on a range of document lines (matching VS Code Ctrl+/).
///
/// <para><b>Toggle semantics:</b>
/// <list type="bullet">
///   <item>If <em>every</em> non-empty line in the range already starts with
///   <paramref name="prefix"/> (after optional leading whitespace) →
///   <em>remove</em> the prefix from all of them.</item>
///   <item>Otherwise → <em>add</em> <c>prefix + " "</c> at the column of the
///   least-indented non-empty line.  Empty lines are skipped.</item>
/// </list>
/// The whole operation is a single undo step.
/// </para>
/// </summary>
public static class LineCommentToggle
{
    /// <summary>
    /// Toggle comments on lines [<paramref name="startLine"/>, <paramref name="endLine"/>]
    /// (both inclusive, zero-based).
    /// </summary>
    public static void Toggle(
        TextDocument doc, int startLine, int endLine, string prefix = "//")
    {
        if (string.IsNullOrEmpty(prefix)) return;
        endLine = Math.Min(endLine, doc.LineCount - 1);
        if (startLine > endLine) return;

        string prefixWithSpace = prefix + " ";

        // ── Gather per-line info ──────────────────────────────────────────
        var infos = new (string Text, int Indent, bool IsEmpty)[endLine - startLine + 1];
        for (int li = startLine; li <= endLine; li++)
        {
            string t  = doc.GetLine(li);
            int indent = LeadingWhitespace(t);
            infos[li - startLine] = (t, indent, t.Trim().Length == 0);
        }

        // ── Determine direction ───────────────────────────────────────────
        bool allCommented = infos.All(x => x.IsEmpty
            || x.Text.AsSpan(x.Indent).StartsWith(prefix));

        // ── Build composite edit — bottom-to-top ─────────────────────────
        var cmds = new List<Commands.IEditorCommand>();

        if (allCommented)
        {
            // Uncomment: remove prefix (and one optional following space).
            for (int li = endLine; li >= startLine; li--)
            {
                var (text, indent, isEmpty) = infos[li - startLine];
                if (isEmpty) continue;

                int prefixStart = doc.PositionToOffset(li, indent);
                bool hasSpace   = indent + prefix.Length < text.Length
                                  && text[indent + prefix.Length] == ' ';
                int removeLen   = prefix.Length + (hasSpace ? 1 : 0);
                cmds.Add(new Commands.DeleteCommand(doc.InternalBuffer, prefixStart, removeLen));
            }
        }
        else
        {
            // Comment: insert at the minimum-indent column of non-empty lines.
            int minIndent = infos
                .Where(x => !x.IsEmpty)
                .Select(x => x.Indent)
                .DefaultIfEmpty(0)
                .Min();

            for (int li = endLine; li >= startLine; li--)
            {
                if (infos[li - startLine].IsEmpty) continue;
                int insertOffset = doc.PositionToOffset(li, minIndent);
                cmds.Add(new Commands.InsertCommand(doc.InternalBuffer, insertOffset, prefixWithSpace));
            }
        }

        if (cmds.Count > 0)
            doc.ExecuteComposite(
                allCommented ? "Uncomment lines" : "Comment lines", cmds);
    }

    private static int LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }
}
