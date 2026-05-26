namespace TextAPI.Core.Scripting;

/// <summary>
/// A single parsed script command with its argument tokens.
/// </summary>
public sealed class ScriptCommand
{
    public string         Verb      { get; }
    public IReadOnlyList<ScriptArg> Args { get; }
    public int            LineNumber { get; }

    public ScriptCommand(string verb, IReadOnlyList<ScriptArg> args, int lineNumber)
    {
        Verb       = verb;
        Args       = args;
        LineNumber = lineNumber;
    }

    public override string ToString()
        => $"[L{LineNumber}] {Verb} {string.Join(" ", Args)}";
}

/// <summary>A single argument token — either an integer, a string, or a flag (/i /w /r).</summary>
public sealed class ScriptArg
{
    public ScriptArgKind Kind  { get; }
    public int           Int   { get; }
    public string        Str   { get; }

    private ScriptArg(ScriptArgKind kind, int i, string s)
    { Kind = kind; Int = i; Str = s; }

    public static ScriptArg Number(int n)  => new(ScriptArgKind.Number, n, string.Empty);
    public static ScriptArg Text(string s) => new(ScriptArgKind.Text,   0, s);
    public static ScriptArg Flag(string f) => new(ScriptArgKind.Flag,   0, f);

    public override string ToString() => Kind switch
    {
        ScriptArgKind.Number => Int.ToString(),
        ScriptArgKind.Text   => $"\"{Str}\"",
        ScriptArgKind.Flag   => $"/{Str}",
        _                    => Str
    };
}

public enum ScriptArgKind { Number, Text, Flag }
