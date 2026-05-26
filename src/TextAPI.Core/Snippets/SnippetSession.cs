namespace TextAPI.Core.Snippets;

/// <summary>
/// An active snippet expansion in a document.
///
/// Lifecycle
/// ---------
///   1. <see cref="SnippetEngine.BeginSnippet"/> inserts the snippet text and
///      returns a SnippetSession.
///   2. Caller invokes <see cref="NextTabStop"/> / <see cref="PrevTabStop"/> to
///      navigate between tab stops — each returns the tab stop's offset + length
///      so the caller can set the cursor selection.
///   3. When editing inside a tab stop, call <see cref="UpdateTabStop"/> so the
///      session can adjust sibling offsets.  Mirror fields (same index, multiple
///      occurrences) are updated in the document automatically.
///   4. <see cref="Commit"/> or <see cref="Cancel"/> ends the session.
///
/// Mirror fields
/// -------------
///   When the same tab-stop index appears multiple times in the snippet body, all
///   occurrences are mirrors.  <see cref="UpdateTabStop"/> replaces placeholder
///   text in every mirror and keeps their offsets in sync.
/// </summary>
public sealed class SnippetSession
{
    private readonly TextDocument _doc;

    /// <summary>All tab stops, in insertion order (may contain duplicates for mirrors).</summary>
    private readonly List<TabStop> _tabStops;

    /// <summary>The ordered navigation sequence (unique indices, ascending, $0 last).</summary>
    private readonly List<int> _navOrder;
    private int _navIndex = -1; // index into _navOrder

    public bool IsActive { get; private set; } = true;

    /// <summary>The currently focused tab-stop index, or -1 if not yet navigated.</summary>
    public int CurrentTabStopIndex => _navIndex >= 0 && _navIndex < _navOrder.Count
        ? _navOrder[_navIndex] : -1;

    internal SnippetSession(TextDocument doc, List<TabStop> tabStops, List<int> navOrder)
    {
        _doc      = doc;
        _tabStops = tabStops;
        _navOrder = navOrder;
    }

    // -- Navigation -------------------------------------------------------

    /// <summary>
    /// Move to the next tab stop.
    /// Returns the primary <see cref="TabStop"/> for that stop, or <see langword="null"/>
    /// when all tab stops have been visited (session ends automatically).
    /// </summary>
    public TabStop? NextTabStop()
    {
        if (!IsActive) return null;
        _navIndex++;
        if (_navIndex >= _navOrder.Count)
        {
            IsActive = false;
            return null;
        }
        return GetPrimary(_navOrder[_navIndex]);
    }

    /// <summary>Move to the previous tab stop. Returns null when already at first.</summary>
    public TabStop? PrevTabStop()
    {
        if (!IsActive || _navIndex <= 0) return null;
        _navIndex--;
        return GetPrimary(_navOrder[_navIndex]);
    }

    /// <summary>Returns all tab stops with the given index (primary + mirrors).</summary>
    public IReadOnlyList<TabStop> GetTabStops(int index)
        => _tabStops.Where(t => t.Index == index).ToList();

    /// <summary>Returns the primary (first) tab stop for the given index.</summary>
    public TabStop? GetPrimary(int index)
        => _tabStops.FirstOrDefault(t => t.Index == index);

    // -- Mirror sync ------------------------------------------------------

    /// <summary>
    /// Call this when the user edits text within a tab stop.
    /// <paramref name="tabStopIndex"/> — which tab stop was edited.
    /// <paramref name="newText"/>      — the new placeholder text.
    /// Updates all mirrors in the document and adjusts subsequent offsets.
    /// </summary>
    public void UpdateTabStop(int tabStopIndex, string newText)
    {
        if (!IsActive) return;
        var mirrors = _tabStops.Where(t => t.Index == tabStopIndex).ToList();
        // Process in reverse offset order to avoid offset interference.
        foreach (var mirror in mirrors.OrderByDescending(t => t.Offset))
        {
            int oldLen = mirror.Length;
            int delta  = newText.Length - oldLen;

            _doc.Replace(mirror.Offset, oldLen, newText);
            mirror.Length = newText.Length;

            // Shift all tab stops that come after this mirror.
            foreach (var other in _tabStops)
            {
                if (other != mirror && other.Offset > mirror.Offset)
                    other.Offset += delta;
            }
        }
    }

    // -- Session lifecycle ------------------------------------------------

    /// <summary>End the session, leaving all inserted text in place.</summary>
    public void Commit()  => IsActive = false;

    /// <summary>
    /// Cancel the session and remove all inserted snippet text from the document.
    /// Uses undo to restore the document to its pre-snippet state.
    /// </summary>
    public void Cancel()
    {
        if (!IsActive) return;
        IsActive = false;
        _doc.Undo();
    }
}
