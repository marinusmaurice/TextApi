namespace TextEditor.Core.Navigation;

/// <summary>
/// Tracks bookmarked lines in a document.
/// Line indices are automatically remapped when lines are inserted or deleted.
/// </summary>
public sealed class BookmarkModel
{
    private readonly SortedSet<int> _bookmarks = new();

    /// <summary>Fired whenever the set of bookmarked lines changes.</summary>
    public event Action? BookmarksChanged;

    /// <summary>
    /// Toggle the bookmark on <paramref name="line"/>.
    /// Returns <see langword="true"/> if the line is now bookmarked,
    /// <see langword="false"/> if it was removed.
    /// </summary>
    public bool Toggle(int line)
    {
        bool added = !_bookmarks.Remove(line);
        if (added) _bookmarks.Add(line);
        BookmarksChanged?.Invoke();
        return added;
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="line"/> is bookmarked.</summary>
    public bool IsBookmarked(int line) => _bookmarks.Contains(line);

    /// <summary>All bookmarked lines in ascending order.</summary>
    public IReadOnlyList<int> GetAll() => _bookmarks.ToList();

    /// <summary>
    /// Returns the first bookmarked line strictly after <paramref name="fromLine"/>,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    public int? NextBookmark(int fromLine)
    {
        var view = _bookmarks.GetViewBetween(fromLine + 1, int.MaxValue);
        return view.Count > 0 ? view.Min : null;
    }

    /// <summary>
    /// Returns the last bookmarked line strictly before <paramref name="fromLine"/>,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    public int? PrevBookmark(int fromLine)
    {
        if (fromLine == 0) return null;
        var view = _bookmarks.GetViewBetween(0, fromLine - 1);
        return view.Count > 0 ? view.Max : null;
    }

    /// <summary>Remap bookmarks after <paramref name="count"/> lines are inserted at <paramref name="line"/>.</summary>
    public void OnInsert(int line, int count)
    {
        if (count <= 0) return;
        var toShift = _bookmarks.GetViewBetween(line, int.MaxValue).ToList();
        if (toShift.Count == 0) return;
        foreach (int l in toShift) { _bookmarks.Remove(l); _bookmarks.Add(l + count); }
        BookmarksChanged?.Invoke();
    }

    /// <summary>Remap (and remove covered) bookmarks after <paramref name="count"/> lines are deleted starting at <paramref name="line"/>.</summary>
    public void OnDelete(int line, int count)
    {
        if (count <= 0) return;
        bool changed = false;
        int end = line + count;
        foreach (int l in _bookmarks.GetViewBetween(line, end - 1).ToList())
        {
            _bookmarks.Remove(l);
            changed = true;
        }
        foreach (int l in _bookmarks.GetViewBetween(end, int.MaxValue).ToList())
        {
            _bookmarks.Remove(l);
            _bookmarks.Add(l - count);
            changed = true;
        }
        if (changed) BookmarksChanged?.Invoke();
    }

    /// <summary>Remove all bookmarks.</summary>
    public void Clear()
    {
        if (_bookmarks.Count == 0) return;
        _bookmarks.Clear();
        BookmarksChanged?.Invoke();
    }
}
