namespace TextEditor.Core.Commands;

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

/// <summary>
/// Undo/redo stack.
/// All mutations must flow through here to participate in undo history.
/// </summary>
public sealed class CommandHistory
{
    private readonly Stack<IEditorCommand> _undoStack = new();
    private readonly Stack<IEditorCommand> _redoStack = new();
    private readonly int _maxHistory;

    public CommandHistory(int maxHistory = 1000) => _maxHistory = maxHistory;

    /// <summary>Execute a command and push it onto the undo stack.</summary>
    public void Execute(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();   // any new edit kills the redo branch
        if (_undoStack.Count > _maxHistory)
            TrimUndoStack();
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack.Pop();
        cmd.Undo();
        _redoStack.Push(cmd);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack.Pop();
        cmd.Execute();
        _undoStack.Push(cmd);
    }

    public IEnumerable<string> UndoDescriptions => _undoStack.Select(c => c.Description);
    public IEnumerable<string> RedoDescriptions => _redoStack.Select(c => c.Description);

    public void Clear() { _undoStack.Clear(); _redoStack.Clear(); }

    private void TrimUndoStack()
    {
        var items = _undoStack.ToArray();
        _undoStack.Clear();
        for (int i = 0; i < _maxHistory; i++)
            _undoStack.Push(items[i]);
    }
}
