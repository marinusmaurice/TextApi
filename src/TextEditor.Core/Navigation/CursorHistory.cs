namespace TextEditor.Core.Navigation;

/// <summary>
/// One saved position in the cursor navigation history.
/// </summary>
/// <param name="Offset">Zero-based character offset in the document.</param>
/// <param name="FilePath">
/// Optional file path, for cross-file navigation (e.g. Go to Definition).
/// <see langword="null"/> when navigating within the same document.
/// </param>
public readonly record struct HistoryEntry(int Offset, string? FilePath = null);

/// <summary>
/// Bounded ring buffer of cursor jump positions — the Back / Forward
/// (Alt+Left / Alt+Right) navigation stack.
///
/// <para>
/// The caller is responsible for deciding which moves are "jumps" and
/// calling <see cref="Push"/>.  Normal cursor movement (arrows, word
/// motion) should NOT push; jumps (Find Next, Go to Line, Go to
/// Definition, clicking in the scrollbar) SHOULD push.
/// </para>
///
/// <para>
/// Semantics mirror a browser's Back/Forward:
/// <list type="bullet">
///   <item><see cref="Push"/> appends a position and truncates any
///   forward history, then evicts the oldest entry when over
///   <see cref="Capacity"/>.</item>
///   <item><see cref="Back"/> moves the current pointer one step
///   backwards and returns the entry at the new position.</item>
///   <item><see cref="Forward"/> moves the pointer one step forwards.</item>
/// </list>
/// </para>
///
/// <para>
/// <see cref="TextDocument.Load"/> and
/// <see cref="TextDocument.LoadFileAsync"/> call <see cref="Clear"/>
/// automatically.
/// </para>
/// </summary>
public sealed class CursorHistory
{
    private readonly List<HistoryEntry> _entries = [];
    private int _current = -1; // index into _entries; -1 when empty

    /// <summary>Maximum number of entries retained (oldest evicted when exceeded).</summary>
    public int Capacity { get; }

    /// <summary>Number of entries currently stored.</summary>
    public int Count => _entries.Count;

    /// <summary>The entry the pointer is currently resting on, or <see langword="null"/> when empty.</summary>
    public HistoryEntry? Current => _current >= 0 ? _entries[_current] : null;

    /// <summary><see langword="true"/> when <see cref="Back"/> would return an entry.</summary>
    public bool CanGoBack => _current > 0;

    /// <summary><see langword="true"/> when <see cref="Forward"/> would return an entry.</summary>
    public bool CanGoForward => _current < _entries.Count - 1;

    /// <param name="capacity">Maximum entries to retain (default 100).</param>
    public CursorHistory(int capacity = 100)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be ≥ 1.");
        Capacity = capacity;
    }

    // ── Mutation ──────────────────────────────────────────────────────────

    /// <summary>
    /// Record a jump to <paramref name="offset"/>.
    ///
    /// <list type="bullet">
    ///   <item>No-op when the new position is identical to the current one.</item>
    ///   <item>Any forward history (entries after the current pointer) is discarded.</item>
    ///   <item>When <see cref="Count"/> would exceed <see cref="Capacity"/>,
    ///   the oldest entry is evicted.</item>
    /// </list>
    /// </summary>
    public void Push(int offset, string? filePath = null)
    {
        var entry = new HistoryEntry(offset, filePath);

        // Don't record a no-op jump.
        if (_current >= 0 && _entries[_current] == entry) return;

        // Truncate forward history.
        if (_current < _entries.Count - 1)
            _entries.RemoveRange(_current + 1, _entries.Count - _current - 1);

        _entries.Add(entry);
        _current = _entries.Count - 1;

        // Evict oldest entries while over capacity.
        while (_entries.Count > Capacity)
        {
            _entries.RemoveAt(0);
            _current--;
        }
    }

    /// <summary>
    /// Move the pointer one step backwards and return the entry now pointed to.
    /// Returns <see langword="null"/> when already at the oldest entry (no-op).
    /// </summary>
    public HistoryEntry? Back()
    {
        if (!CanGoBack) return null;
        _current--;
        return _entries[_current];
    }

    /// <summary>
    /// Move the pointer one step forwards and return the entry now pointed to.
    /// Returns <see langword="null"/> when already at the newest entry (no-op).
    /// </summary>
    public HistoryEntry? Forward()
    {
        if (!CanGoForward) return null;
        _current++;
        return _entries[_current];
    }

    /// <summary>Remove all entries and reset the pointer.</summary>
    public void Clear()
    {
        _entries.Clear();
        _current = -1;
    }
}
