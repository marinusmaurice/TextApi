namespace TextEditor.Core.Language;

/// <summary>
/// Utility operations that clean up document whitespace.
/// Each operation that modifies the document is a single undo step.
/// </summary>
public static class DocumentCleanup
{
    /// <summary>
    /// Remove trailing whitespace (spaces and tabs) from every line.
    /// Returns the number of lines modified.
    /// Returns 0 and does not touch the undo stack when the document is already clean.
    /// </summary>
    public static int TrimTrailingWhitespace(TextDocument doc)
    {
        var cmds = new List<Commands.IEditorCommand>();

        // Iterate bottom-to-top so offsets in earlier lines stay valid while we collect.
        for (int li = doc.LineCount - 1; li >= 0; li--)
        {
            string line  = doc.GetLine(li);
            int    clean = TrimEnd(line);
            if (clean == line.Length) continue;

            int start  = doc.PositionToOffset(li, clean);
            int length = line.Length - clean;
            cmds.Add(new Commands.DeleteCommand(doc.InternalBuffer, start, length));
        }

        if (cmds.Count == 0) return 0;
        doc.ExecuteComposite("Trim trailing whitespace", cmds);
        return cmds.Count;
    }

    /// <summary>
    /// Normalise line endings to <paramref name="eol"/> (\n or \r\n).
    ///
    /// <para>Sets <see cref="TextDocument.SaveEolStyle"/> so the target EOL
    /// is written on the next save, then strips any stray <c>\r</c> characters
    /// that may have been inserted programmatically into the internal buffer.
    /// </para>
    /// </summary>
    /// <param name="doc">Target document.</param>
    /// <param name="eol">Target EOL string.  Use "\n" (LF) or "\r\n" (CRLF).</param>
    public static void NormalizeLineEndings(TextDocument doc, string eol = "\n")
    {
        doc.SaveEolStyle = eol == "\r\n"
            ? EOL.EolStyle.CrLf
            : EOL.EolStyle.Lf;

        // Strip any stray \r in the internal LF-normalised buffer.
        if (doc.GetText().Contains('\r'))
            doc.ReplaceAll("\r", "");
    }

    private static int TrimEnd(string s)
    {
        int i = s.Length;
        while (i > 0 && (s[i - 1] == ' ' || s[i - 1] == '\t')) i--;
        return i;
    }
}
