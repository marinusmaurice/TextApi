namespace TextEditor.Core.Snippets;

/// <summary>
/// Parses snippet templates and expands them into a <see cref="TextDocument"/>,
/// returning a <see cref="SnippetSession"/> for tab-stop navigation.
/// </summary>
public static class SnippetEngine
{
    // -- Parse ------------------------------------------------------------

    /// <summary>Parse a snippet body string into a reusable <see cref="Snippet"/>.</summary>
    public static Snippet Parse(string body) => new(SnippetParser.Parse(body));

    // -- Expand -----------------------------------------------------------

    /// <summary>
    /// Insert a snippet at <paramref name="insertOffset"/> in <paramref name="doc"/>
    /// and return an active <see cref="SnippetSession"/>.
    ///
    /// Variables are substituted immediately:
    ///   $TM_FILENAME      → doc.FilePath filename (or "")
    ///   $TM_FILENAME_BASE → filename without extension (or "")
    ///   $CLIPBOARD        → clipboardText parameter (or "")
    ///   Unknown vars      → placeholder text or ""
    ///
    /// The entire insertion is a single undo step, so <see cref="SnippetSession.Cancel"/>
    /// can undo it with one <c>doc.Undo()</c>.
    /// </summary>
    public static SnippetSession BeginSnippet(
        TextDocument doc, Snippet snippet, int insertOffset,
        string? clipboardText = null, string? filename = null)
    {
        filename      ??= System.IO.Path.GetFileName(doc.FilePath) ?? "";
        clipboardText ??= "";

        // -- Phase 1: Build the expanded text and record tab-stop positions -
        var sb       = new System.Text.StringBuilder();
        var rawStops = new List<(int TabIndex, int OffsetInExpansion, int Length)>();

        foreach (var part in snippet.Parts)
        {
            switch (part.Kind)
            {
                case SnippetPartKind.Literal:
                    sb.Append(part.Text);
                    break;

                case SnippetPartKind.TabStop:
                    int stopOffset = sb.Length;
                    string placeholder = part.Text;
                    sb.Append(placeholder);
                    rawStops.Add((part.TabIndex, stopOffset, placeholder.Length));
                    break;

                case SnippetPartKind.Variable:
                    string value = part.VarName switch
                    {
                        "TM_FILENAME"           => filename,
                        "TM_FILENAME_BASE"      => System.IO.Path.GetFileNameWithoutExtension(filename),
                        "CLIPBOARD"             => clipboardText,
                        "TM_CURRENT_LINE"       => "",
                        "TM_SELECTED_TEXT"      => "",
                        _                       => part.Text, // fallback to placeholder
                    };
                    sb.Append(value);
                    break;
            }
        }

        string expandedText = sb.ToString();

        // -- Phase 2: Insert into document (single undo unit) ---------------
        doc.FlushUndoGroup();
        doc.Insert(insertOffset, expandedText);
        doc.FlushUndoGroup(); // seal this insert as its own undo unit

        // -- Phase 3: Build TabStop objects (absolute document offsets) -----
        var tabStops = rawStops.Select(r => new TabStop(
            r.TabIndex,
            insertOffset + r.OffsetInExpansion,
            r.Length)).ToList();

        // -- Phase 4: Build navigation order (ascending index, $0 last) -----
        var navOrder = rawStops
            .Select(r => r.TabIndex)
            .Distinct()
            .Where(idx => idx != 0)
            .OrderBy(idx => idx)
            .ToList();
        if (rawStops.Any(r => r.TabIndex == 0))
            navOrder.Add(0);

        return new SnippetSession(doc, tabStops, navOrder);
    }
}
