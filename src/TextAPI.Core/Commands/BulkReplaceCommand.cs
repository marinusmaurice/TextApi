namespace TextAPI.Core.Commands;

/// <summary>
/// O(n) bulk replace command — single-pass rewrite into a fresh buffer.
///
/// WHY THIS EXISTS:
///   The naive approach (N individual ReplaceCommands in a CompositeCommand) is
///   O(N × log N): each Delete+Insert walks the RB tree, rebalances, and updates
///   subtree metadata. For 128k replacements that is ~128k × log(128k) ≈ 2.2M tree
///   operations — measured at ~17 seconds in tests.
///
/// THIS APPROACH:
///   1. Collect all match offsets in one forward scan — O(n).
///   2. Allocate one output char[] sized exactly: len + (matches × delta).
///   3. Single forward memcopy pass: copy gap → copy replacement → repeat.
///   4. Hand the result directly to PieceTable.LoadRaw() which resets the tree
///      to a single root piece — O(1) tree work regardless of match count.
///   5. Undo snapshots the ORIGINAL char[] (already in memory as the orig buffer).
///
/// COMPLEXITY: O(n) time, O(n) space — same as a single string.Replace().
/// UNDO COST:  O(n) — reload original buffer, same as Compact().
/// </summary>
public sealed class BulkReplaceCommand : IEditorCommand
{
    private readonly Buffer.PieceTable _table;
    private readonly string            _description;

    // Undo state — captured at Execute time
    private char[]? _originalChars;
    private int     _originalLength;
    private EOL.EolStyle _originalEolStyle;
    private EOL.EolStyle _originalSaveStyle;

    // The pre-computed output — built by the caller (TextDocument.ReplaceAll)
    // so the search work is not duplicated.
    private readonly char[] _newChars;
    private readonly int    _newLength;
    private readonly int    _matchCount;

    public string Description => _description;

    public BulkReplaceCommand(
        Buffer.PieceTable table,
        char[]            newChars,
        int               newLength,
        int               matchCount,
        string            pattern,
        string            replacement)
    {
        _table       = table;
        _newChars    = newChars;
        _newLength   = newLength;
        _matchCount  = matchCount;
        _description = $"Replace all '{pattern}' → '{replacement}' ({matchCount:N0} occurrences) [bulk O(n)]";
    }

    public void Execute()
    {
        // Snapshot original for undo
        (_originalChars, _originalLength, _originalEolStyle, _originalSaveStyle)
            = _table.SnapshotForUndo();

        // Swap in the new buffer — O(1) tree work
        _table.LoadRaw(_newChars, _newLength);
    }

    public void Undo()
    {
        if (_originalChars == null) return;
        _table.LoadRaw(_originalChars, _originalLength, _originalEolStyle, _originalSaveStyle);
    }
}
