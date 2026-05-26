namespace TextAPI.Core.Scripting;

/// <summary>The outcome of running a complete script.</summary>
public sealed class ScriptResult
{
    /// <summary>True if every command executed without error.</summary>
    public bool Success { get; }

    /// <summary>Number of commands that were executed (including last one if it failed).</summary>
    public int StepsExecuted { get; }

    /// <summary>Result per executed step.</summary>
    public IReadOnlyList<StepResult> Steps { get; }

    /// <summary>Non-null when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Script line number of the failing command, or null.</summary>
    public int? ErrorLine { get; }

    /// <summary>Cursor offset at the end of execution.</summary>
    public int CursorOffset { get; }

    /// <summary>Number of replacements made by the last REPLACE_ALL command, or 0.</summary>
    public int LastReplaceCount { get; }

    /// <summary>Matches found by the last FIND_ALL command, or empty.</summary>
    public IReadOnlyList<Search.SearchMatch> LastFindAll { get; }

    internal ScriptResult(bool success, int steps, IReadOnlyList<StepResult> stepList,
        string? errorMessage, int? errorLine, int cursorOffset, int lastReplaceCount,
        IReadOnlyList<Search.SearchMatch> lastFindAll)
    {
        Success          = success;
        StepsExecuted    = steps;
        Steps            = stepList;
        ErrorMessage     = errorMessage;
        ErrorLine        = errorLine;
        CursorOffset     = cursorOffset;
        LastReplaceCount = lastReplaceCount;
        LastFindAll      = lastFindAll;
    }
}

/// <summary>Result of a single executed command step.</summary>
public sealed class StepResult
{
    public int    LineNumber    { get; }
    public string Verb          { get; }
    public bool   Success       { get; }
    public int    CursorAfter   { get; }
    public string? ErrorMessage { get; }

    public StepResult(int lineNumber, string verb, bool success, int cursorAfter, string? errorMessage = null)
    {
        LineNumber   = lineNumber;
        Verb         = verb;
        Success      = success;
        CursorAfter  = cursorAfter;
        ErrorMessage = errorMessage;
    }
}
