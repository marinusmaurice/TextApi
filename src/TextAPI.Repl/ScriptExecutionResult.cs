namespace TextAPI.Repl;

/// <summary>The result of a single REPL submission.</summary>
public sealed class ScriptExecutionResult
{
    /// <summary>True when the script compiled and ran without exception.</summary>
    public bool Success { get; }

    /// <summary>
    /// The string representation of the last expression's value, or null when the
    /// last statement was void / had no return value.
    /// </summary>
    public string? ReturnValue { get; }

    /// <summary>
    /// Everything written via <c>Print()</c> or <c>Console.Write*</c> during this run.
    /// Empty string when nothing was printed.
    /// </summary>
    public string Output { get; }

    /// <summary>Non-null when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; }

    /// <summary>True when the failure is a compile-time error (as opposed to a runtime exception).</summary>
    public bool IsCompileError { get; }

    internal ScriptExecutionResult(bool success, string? returnValue, string output,
        string? errorMessage, bool isCompileError)
    {
        Success        = success;
        ReturnValue    = returnValue;
        Output         = output;
        ErrorMessage   = errorMessage;
        IsCompileError = isCompileError;
    }

    /// <summary>
    /// The text to display in the output panel.
    /// Combines printed output + the return value (if any), separated by a newline.
    /// </summary>
    public string DisplayText
    {
        get
        {
            if (!Success)
                return ErrorMessage ?? string.Empty;

            var parts = new System.Text.StringBuilder();
            if (Output.Length > 0) parts.Append(Output);
            if (ReturnValue != null)
            {
                if (parts.Length > 0 && !parts.ToString().EndsWith('\n'))
                    parts.Append('\n');
                parts.Append(ReturnValue);
            }
            return parts.ToString();
        }
    }
}
