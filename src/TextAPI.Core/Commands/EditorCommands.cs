namespace TextAPI.Core.Commands;

/// <summary>Base interface for all editor commands.</summary>
public interface IEditorCommand
{
    void Execute();
    void Undo();
    string Description { get; }
}

/// <summary>Insert text at an offset.</summary>
public sealed class InsertCommand : IEditorCommand
{
    private readonly Buffer.PieceTable _buffer;
    private readonly int    _offset;
    private readonly string _text;

    public string Description => $"Insert {_text.Length} chars at {_offset}";

    public InsertCommand(Buffer.PieceTable buffer, int offset, string text)
    {
        _buffer = buffer;
        _offset = offset;
        _text   = text;
    }

    public void Execute() => _buffer.Insert(_offset, _text);
    public void Undo()    => _buffer.Delete(_offset, _text.Length);
}

/// <summary>Delete a range of text.</summary>
public sealed class DeleteCommand : IEditorCommand
{
    private readonly Buffer.PieceTable _buffer;
    private readonly int    _offset;
    private readonly int    _length;
    private          string _deletedText = string.Empty;

    public string Description => $"Delete {_length} chars at {_offset}";

    public DeleteCommand(Buffer.PieceTable buffer, int offset, int length)
    {
        _buffer = buffer;
        _offset = offset;
        _length = length;
    }

    public void Execute()
    {
        _deletedText = _buffer.GetText(_offset, _length);
        _buffer.Delete(_offset, _length);
    }

    public void Undo() => _buffer.Insert(_offset, _deletedText);
}

/// <summary>Replace a range with new text (combines delete + insert atomically).</summary>
public sealed class ReplaceCommand : IEditorCommand
{
    private readonly Buffer.PieceTable _buffer;
    private readonly int    _offset;
    private readonly int    _deleteLength;
    private readonly string _insertText;
    private          string _originalText = string.Empty;

    public string Description => $"Replace {_deleteLength} chars at {_offset} with '{_insertText[..Math.Min(20, _insertText.Length)]}'";

    public ReplaceCommand(Buffer.PieceTable buffer, int offset, int deleteLength, string insertText)
    {
        _buffer       = buffer;
        _offset       = offset;
        _deleteLength = deleteLength;
        _insertText   = insertText;
    }

    public void Execute()
    {
        _originalText = _buffer.GetText(_offset, _deleteLength);
        _buffer.Delete(_offset, _deleteLength);
        _buffer.Insert(_offset, _insertText);
    }

    public void Undo()
    {
        _buffer.Delete(_offset, _insertText.Length);
        _buffer.Insert(_offset, _originalText);
    }
}

/// <summary>
/// Composite command — groups multiple commands into one undoable unit.
/// Use for multi-cursor edits, find-replace-all, etc.
/// </summary>
public sealed class CompositeCommand : IEditorCommand
{
    private readonly List<IEditorCommand> _commands;
    public  string Description { get; }

    public CompositeCommand(string description, IEnumerable<IEditorCommand> commands)
    {
        Description = description;
        _commands   = [.. commands];
    }

    public void Execute()
    {
        foreach (var cmd in _commands)
            cmd.Execute();
    }

    public void Undo()
    {
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}

/// <summary>Direction hint for undo grouping.</summary>
internal enum GroupKind { None, Insert, Delete }

/// <summary>
/// Undo/redo stack with smart coalescing.
///
/// Single-grapheme-cluster inserts at adjacent positions are coalesced into one
/// undo unit (so Ctrl+Z undoes a word, not a letter).  Single-cluster backspaces
/// and single-cluster forward-deletes are coalesced separately.
///
/// The pending group is flushed (committed to the undo stack) on:
///   • Any non-groupable command (paste, composite, replace)
///   • Explicit <see cref="FlushGroup"/> call (cursor navigation, Undo, Redo)
///   • Exceeding <see cref="MaxGroupCodeUnits"/>
/// </summary>
public sealed class CommandHistory
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private readonly int _maxHistory;

    // ── Grouping state ─────────────────────────────────────────────────────
    private readonly List<IEditorCommand> _pending = [];
    private GroupKind _groupKind    = GroupKind.None;
    private int  _insertTail        = -1;   // expected offset of next grouped insert
    private int  _deleteFwd         = -1;   // forward-delete stable anchor
    private int  _deleteBack        = -1;   // backspace left edge (shrinks leftward)
    private bool _deleteDirSet      = false; // direction locked after 2nd delete
    private bool _deleteIsForward   = false;
    private int  _groupUnits        = 0;    // total code units buffered
    private const int MaxGroupCodeUnits = 200;

    public CommandHistory(int maxHistory = 1000) => _maxHistory = maxHistory;

    // ── Public grouping API ───────────────────────────────────────────────

    /// <summary>
    /// Execute a command that may be coalesced with the previous grouped command.
    /// Called by <see cref="TextDocument"/> for single-cluster insert/delete operations.
    /// </summary>
    internal void ExecuteGrouped(IEditorCommand command,
                                  GroupKind kind,
                                  int insertOffset  = 0, int insertLength  = 0,
                                  int deleteOffset  = 0, int deleteLength  = 0)
    {
        bool canJoin = false;

        if (_groupKind == kind && _pending.Count > 0)
        {
            int addedUnits = kind == GroupKind.Insert ? insertLength : deleteLength;
            if (_groupUnits + addedUnits <= MaxGroupCodeUnits)
            {
                if (kind == GroupKind.Insert)
                {
                    canJoin = insertOffset == _insertTail;
                }
                else // Delete
                {
                    bool fwd = deleteOffset == _deleteFwd;
                    bool bck = deleteOffset + deleteLength == _deleteBack;

                    if (!_deleteDirSet)
                    {
                        // Direction established by the second delete
                        canJoin = fwd || bck;
                        if (canJoin)
                        {
                            _deleteIsForward = fwd;
                            _deleteDirSet    = true;
                        }
                    }
                    else
                    {
                        canJoin = _deleteIsForward ? fwd : bck;
                    }
                }
            }
        }

        if (!canJoin)
        {
            FlushGroup();          // commit previous group
            _redoStack.Clear();    // new edit branch kills redo
        }

        command.Execute();
        _pending.Add(command);
        _groupKind   = kind;
        _groupUnits += kind == GroupKind.Insert ? insertLength : deleteLength;

        if (kind == GroupKind.Insert)
        {
            _insertTail = insertOffset + insertLength;
        }
        else // Delete
        {
            if (_pending.Count == 1)
            {
                _deleteFwd    = deleteOffset;
                _deleteBack   = deleteOffset;
                _deleteDirSet = false;
            }
            else if (_deleteDirSet && !_deleteIsForward)
            {
                _deleteBack = deleteOffset; // backspace: left edge advances leftward
            }
            // forward delete: _deleteFwd stays fixed (offset stays same as doc shrinks)
        }
    }

    /// <summary>
    /// Commit any pending coalesced group to the undo stack.
    /// Call this before cursor navigation, Undo, Redo, or any non-groupable operation.
    /// </summary>
    public void FlushGroup()
    {
        if (_pending.Count == 0) return;

        IEditorCommand committed = _pending.Count == 1
            ? _pending[0]
            : new CompositeCommand("typing", _pending.ToList());

        _undoStack.Push(committed);
        if (_undoStack.Count > _maxHistory) TrimUndoStack();

        _pending.Clear();
        _groupKind    = GroupKind.None;
        _insertTail   = _deleteFwd = _deleteBack = -1;
        _groupUnits   = 0;
        _deleteDirSet = false;
    }

    // ── Stack operations ──────────────────────────────────────────────────

    /// <summary>Execute a command as a standalone undo unit (not grouped).</summary>
    public void Execute(IEditorCommand command)
    {
        FlushGroup();           // commit any pending group first
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        if (_undoStack.Count > _maxHistory) TrimUndoStack();
    }

    public bool CanUndo => _undoStack.Count > 0 || _pending.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        FlushGroup();
        if (!_undoStack.Any()) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
    }

    public void Redo()
    {
        FlushGroup();
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
    }

    public IEnumerable<string> UndoDescriptions => _undoStack.Select(c => c.Description);
    public IEnumerable<string> RedoDescriptions => _redoStack.Select(c => c.Description);

    public void Clear()
    {
        // Discard pending without committing (document is being replaced)
        _pending.Clear();
        _groupKind    = GroupKind.None;
        _insertTail   = _deleteFwd = _deleteBack = -1;
        _groupUnits   = 0;
        _deleteDirSet = false;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void TrimUndoStack()
    {
        var items = _undoStack.ToArray();
        _undoStack.Clear();
        for (int i = 0; i < _maxHistory; i++)
            _undoStack.Push(items[i]);
    }
}
