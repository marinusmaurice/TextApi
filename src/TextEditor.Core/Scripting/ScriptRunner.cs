using TextEditor.Core.Search;

namespace TextEditor.Core.Scripting;

/// <summary>
/// Executes a text-manipulation script against a <see cref="TextDocument"/>.
///
/// Supported commands (case-insensitive verb):
///
///   MOVE   &lt;offset&gt;                    — move cursor to absolute offset
///   GOTO   &lt;line&gt; [&lt;col&gt;]              — move cursor to 1-based line, 0-based col
///   INSERT "&lt;text&gt;"                    — insert at cursor
///   INSERT_AT &lt;offset&gt; "&lt;text&gt;"       — insert at explicit offset
///   DELETE &lt;n&gt;                         — delete n chars at cursor
///   DELETE_AT &lt;offset&gt; &lt;n&gt;            — delete n chars at offset
///   DELETE_LINE [&lt;line&gt;]               — delete 1-based line (default = cursor line)
///   SELECT &lt;start&gt; &lt;end&gt;              — set cursor selection
///   REPLACE_ALL "&lt;pat&gt;" "&lt;rep&gt;" [/i] [/w] [/r]  — replace all occurrences
///   FIND      "&lt;pat&gt;" [/i] [/w] [/r]  — move cursor to next match after cursor
///   FIND_PREV "&lt;pat&gt;" [/i] [/w] [/r]  — move cursor to previous match before cursor
///   FIND_ALL  "&lt;pat&gt;" [/i] [/w] [/r]  — collect all matches (stored in result)
///   UNDO                               — undo one step
///   REDO                               — redo one step
///   NOP                                — no-operation (useful for comments)
///
/// Flags (for REPLACE_ALL / FIND*):
///   /i  case-insensitive
///   /w  whole-word only
///   /r  pattern is a regular expression
/// </summary>
public sealed class ScriptRunner
{
    private readonly TextDocument _doc;
    private int _cursor = 0;
    private int _selEnd = 0;   // when > _cursor, there's an active selection

    public ScriptRunner(TextDocument doc) => _doc = doc;

    /// <summary>Current cursor offset (updated after each step).</summary>
    public int CursorOffset => _cursor;

    // ── Entry points ──────────────────────────────────────────────────────

    /// <summary>
    /// Parse and execute a script string.
    /// Stops at the first runtime error.
    /// </summary>
    public ScriptResult Run(string script)
    {
        var parseResult = ScriptParser.Parse(script);
        if (!parseResult.Success)
        {
            var pe = parseResult.Errors[0];
            return Fail(0, new List<StepResult>(), pe.Line, pe.Message, 0, []);
        }
        return Execute(parseResult.Commands);
    }

    /// <summary>Execute a pre-parsed list of commands.</summary>
    public ScriptResult Execute(IReadOnlyList<ScriptCommand> commands)
    {
        var steps        = new List<StepResult>(commands.Count);
        int replCount    = 0;
        var findAll      = new List<SearchMatch>();

        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            var (ok, error, rc, fa) = RunOne(cmd);
            steps.Add(new StepResult(cmd.LineNumber, cmd.Verb, ok, _cursor, error));

            if (rc  >= 0) replCount = rc;
            if (fa  != null) findAll = fa;

            if (!ok)
                return Fail(i + 1, steps, cmd.LineNumber, error!, replCount, findAll);
        }

        return new ScriptResult(true, steps.Count, steps, null, null, _cursor, replCount, findAll);
    }

    // ── Command dispatch ─────────────────────────────────────────────────

    private (bool ok, string? error, int replCount, List<SearchMatch>? findAll) RunOne(ScriptCommand cmd)
    {
        try
        {
            return cmd.Verb switch
            {
                "MOVE"        => ExecMove(cmd),
                "GOTO"        => ExecGoto(cmd),
                "INSERT"      => ExecInsert(cmd),
                "INSERT_AT"   => ExecInsertAt(cmd),
                "DELETE"      => ExecDelete(cmd),
                "DELETE_AT"   => ExecDeleteAt(cmd),
                "DELETE_LINE" => ExecDeleteLine(cmd),
                "SELECT"      => ExecSelect(cmd),
                "REPLACE_ALL" => ExecReplaceAll(cmd),
                "FIND"        => ExecFind(cmd),
                "FIND_PREV"   => ExecFindPrev(cmd),
                "FIND_ALL"    => ExecFindAll(cmd),
                "UNDO"        => ExecUndo(cmd),
                "REDO"        => ExecRedo(cmd),
                "NOP"         => Ok(),
                _             => Error($"Unknown command '{cmd.Verb}'.")
            };
        }
        catch (Exception ex)
        {
            return Error($"Runtime error: {ex.Message}");
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecMove(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 1, out var e)) return Error(e!);
        if (!GetInt(cmd, 0, out int off, out e)) return Error(e!);
        if (!ValidateOffset(off, out e)) return Error(e!);
        _cursor = off;
        _selEnd = off;
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecGoto(ScriptCommand cmd)
    {
        if (cmd.Args.Count < 1) return Error("GOTO requires at least 1 argument (line).");
        if (!GetInt(cmd, 0, out int line, out var e)) return Error(e!);
        int col = 0;
        if (cmd.Args.Count >= 2 && !GetInt(cmd, 1, out col, out e)) return Error(e!);
        if (line < 1) return Error($"Line number must be ≥ 1, got {line}.");
        int lineIdx = line - 1;
        if (lineIdx >= _doc.LineCount) return Error($"Line {line} is beyond document end (doc has {_doc.LineCount} lines).");
        int offset = _doc.PositionToOffset(lineIdx, col);
        _cursor = Clamp(offset);
        _selEnd = _cursor;
        return Ok();
    }

    // ── Insert ───────────────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecInsert(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 1, out var e)) return Error(e!);
        if (!GetStr(cmd, 0, out string text, out e)) return Error(e!);
        _doc.Insert(_cursor, text);
        _cursor += text.Length;
        _selEnd  = _cursor;
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecInsertAt(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 2, out var e)) return Error(e!);
        if (!GetInt(cmd, 0, out int off, out e)) return Error(e!);
        if (!GetStr(cmd, 1, out string text, out e)) return Error(e!);
        if (!ValidateOffset(off, out e)) return Error(e!);
        _doc.Insert(off, text);
        if (off <= _cursor) { _cursor += text.Length; _selEnd += text.Length; }
        return Ok();
    }

    // ── Delete ───────────────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecDelete(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 1, out var e)) return Error(e!);
        if (!GetInt(cmd, 0, out int n, out e)) return Error(e!);
        if (n < 0) return Error($"DELETE count must be ≥ 0, got {n}.");
        int len = Math.Min(n, _doc.Length - _cursor);
        if (len > 0) _doc.Delete(_cursor, len);
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecDeleteAt(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 2, out var e)) return Error(e!);
        if (!GetInt(cmd, 0, out int off, out e)) return Error(e!);
        if (!GetInt(cmd, 1, out int n,   out e)) return Error(e!);
        if (!ValidateOffset(off, out e)) return Error(e!);
        if (n < 0) return Error($"DELETE_AT count must be ≥ 0, got {n}.");
        int len = Math.Min(n, _doc.Length - off);
        if (len > 0)
        {
            _doc.Delete(off, len);
            if (_cursor > off) _cursor = Math.Max(off, _cursor - len);
            if (_selEnd > off) _selEnd = Math.Max(off, _selEnd - len);
        }
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecDeleteLine(ScriptCommand cmd)
    {
        int lineIdx;
        if (cmd.Args.Count == 0)
        {
            lineIdx = _doc.OffsetToPosition(_cursor).Line;
        }
        else
        {
            if (!GetInt(cmd, 0, out int line, out var e)) return Error(e!);
            if (line < 1) return Error($"Line number must be ≥ 1, got {line}.");
            lineIdx = line - 1;
        }

        if (lineIdx >= _doc.LineCount) return Error($"Line {lineIdx + 1} is beyond document end.");

        int start  = _doc.PositionToOffset(lineIdx, 0);
        int length = _doc.GetLine(lineIdx).Length;
        int end    = start + length;

        if (end < _doc.Length)
            end++;          // consume the trailing '\n'
        else if (start > 0)
            start--;        // last line: eat the preceding '\n' instead

        _doc.Delete(start, end - start);
        _cursor = Clamp(start);
        _selEnd = _cursor;
        return Ok();
    }

    // ── Selection ────────────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecSelect(ScriptCommand cmd)
    {
        if (!RequireArgs(cmd, 2, out var e)) return Error(e!);
        if (!GetInt(cmd, 0, out int s, out e)) return Error(e!);
        if (!GetInt(cmd, 1, out int en, out e)) return Error(e!);
        if (!ValidateOffset(s,  out e)) return Error(e!);
        if (!ValidateOffset(en, out e)) return Error(e!);
        if (s > en) return Error($"SELECT start ({s}) must be ≤ end ({en}).");
        _cursor = s;
        _selEnd = en;
        return Ok();
    }

    // ── Replace / Find ───────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecReplaceAll(ScriptCommand cmd)
    {
        if (cmd.Args.Count < 2) return Error("REPLACE_ALL requires pattern and replacement arguments.");
        if (!GetStr(cmd, 0, out string pattern,     out var e)) return Error(e!);
        if (!GetStr(cmd, 1, out string replacement, out e)) return Error(e!);
        var opts = BuildSearchOpts(cmd, startArgIndex: 2);
        int count = _doc.ReplaceAll(pattern, replacement, opts);
        _cursor = Clamp(_cursor);
        _selEnd = _cursor;
        return (true, null, count, null);
    }

    private (bool, string?, int, List<SearchMatch>?) ExecFind(ScriptCommand cmd)
    {
        if (cmd.Args.Count < 1) return Error("FIND requires a pattern argument.");
        if (!GetStr(cmd, 0, out string pattern, out var e)) return Error(e!);
        var opts = BuildSearchOpts(cmd, startArgIndex: 1);
        var match = _doc.FindNext(pattern, _cursor, opts);
        if (match == null) return Error($"Pattern not found: \"{pattern}\".");
        _cursor = match.Value.Offset;
        _selEnd = _cursor + match.Value.Length;
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecFindPrev(ScriptCommand cmd)
    {
        if (cmd.Args.Count < 1) return Error("FIND_PREV requires a pattern argument.");
        if (!GetStr(cmd, 0, out string pattern, out var e)) return Error(e!);
        var opts = BuildSearchOpts(cmd, startArgIndex: 1);
        var match = _doc.FindPrev(pattern, _cursor, opts);
        if (match == null) return Error($"Pattern not found before cursor: \"{pattern}\".");
        _cursor = match.Value.Offset;
        _selEnd = _cursor + match.Value.Length;
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecFindAll(ScriptCommand cmd)
    {
        if (cmd.Args.Count < 1) return Error("FIND_ALL requires a pattern argument.");
        if (!GetStr(cmd, 0, out string pattern, out var e)) return Error(e!);
        var opts = BuildSearchOpts(cmd, startArgIndex: 1);
        var matches = _doc.FindAll(pattern, opts).ToList();
        return (true, null, -1, matches);
    }

    // ── Undo / Redo ──────────────────────────────────────────────────────

    private (bool, string?, int, List<SearchMatch>?) ExecUndo(ScriptCommand cmd)
    {
        if (!_doc.CanUndo) return Error("Nothing to undo.");
        _doc.Undo();
        _cursor = Clamp(_cursor);
        _selEnd = _cursor;
        return Ok();
    }

    private (bool, string?, int, List<SearchMatch>?) ExecRedo(ScriptCommand cmd)
    {
        if (!_doc.CanRedo) return Error("Nothing to redo.");
        _doc.Redo();
        _cursor = Clamp(_cursor);
        _selEnd = _cursor;
        return Ok();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static SearchOptions BuildSearchOpts(ScriptCommand cmd, int startArgIndex)
    {
        bool ci = false, ww = false, rx = false;
        for (int i = startArgIndex; i < cmd.Args.Count; i++)
        {
            if (cmd.Args[i].Kind == ScriptArgKind.Flag)
                switch (cmd.Args[i].Str.ToLowerInvariant())
                {
                    case "i": ci = true; break;
                    case "w": ww = true; break;
                    case "r": rx = true; break;
                }
        }
        return new SearchOptions { CaseSensitive = !ci, WholeWord = ww, UseRegex = rx };
    }

    private static bool RequireArgs(ScriptCommand cmd, int count, out string? error)
    {
        if (cmd.Args.Count >= count) { error = null; return true; }
        error = $"{cmd.Verb} requires {count} argument(s), got {cmd.Args.Count}.";
        return false;
    }

    private static bool GetInt(ScriptCommand cmd, int idx, out int value, out string? error)
    {
        var arg = cmd.Args[idx];
        if (arg.Kind == ScriptArgKind.Number) { value = arg.Int; error = null; return true; }
        if (arg.Kind == ScriptArgKind.Text && int.TryParse(arg.Str, out value)) { error = null; return true; }
        value = 0;
        error = $"{cmd.Verb} argument {idx + 1} must be an integer, got '{arg}'.";
        return false;
    }

    private static bool GetStr(ScriptCommand cmd, int idx, out string value, out string? error)
    {
        var arg = cmd.Args[idx];
        if (arg.Kind == ScriptArgKind.Text) { value = arg.Str; error = null; return true; }
        value = string.Empty;
        error = $"{cmd.Verb} argument {idx + 1} must be a string, got '{arg}'.";
        return false;
    }

    private bool ValidateOffset(int off, out string? error)
    {
        if (off >= 0 && off <= _doc.Length) { error = null; return true; }
        error = $"Offset {off} is out of range [0, {_doc.Length}].";
        return false;
    }

    private int Clamp(int off) => Math.Clamp(off, 0, _doc.Length);

    private static (bool, string?, int, List<SearchMatch>?) Ok()
        => (true, null, -1, null);

    private static (bool, string?, int, List<SearchMatch>?) Error(string msg)
        => (false, msg, -1, null);

    private static ScriptResult Fail(int steps, List<StepResult> stepList,
        int? errorLine, string errorMessage, int replCount, List<SearchMatch> findAll)
        => new(false, steps, stepList, errorMessage, errorLine, 0, replCount, findAll);
}
