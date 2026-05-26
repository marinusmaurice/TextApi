namespace TextAPI.Core.Scripting;

/// <summary>
/// Parses a script string into a list of <see cref="ScriptCommand"/> objects.
///
/// Syntax (one command per line):
///   VERB [arg1] [arg2] ...
///   # comment
///   (blank lines ignored)
///
/// Argument forms:
///   42            — integer
///   "hello\nworld" — double-quoted string (supports \n \t \r \\ \")
///   /flag         — option flag (e.g. /i /w /r)
/// </summary>
public static class ScriptParser
{
    public static ParseResult Parse(string script)
    {
        var commands = new List<ScriptCommand>();
        var errors   = new List<ParseError>();

        int lineNum = 0;
        foreach (var rawLine in SplitLines(script))
        {
            lineNum++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            var (verb, rest) = SplitVerb(line);
            if (string.IsNullOrEmpty(verb))
            {
                errors.Add(new ParseError(lineNum, "Empty command verb."));
                continue;
            }

            var (args, err) = ParseArgs(rest, lineNum);
            if (err != null)
                errors.Add(err.Value);
            else
                commands.Add(new ScriptCommand(verb.ToUpperInvariant(), args!, lineNum));
        }

        return new ParseResult(commands, errors);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static IEnumerable<string> SplitLines(string s)
    {
        int start = 0;
        for (int i = 0; i <= s.Length; i++)
        {
            if (i == s.Length || s[i] == '\n')
            {
                // trim trailing \r
                int end = i;
                if (end > start && s[end - 1] == '\r') end--;
                yield return s[start..end];
                start = i + 1;
            }
        }
    }

    private static (string verb, string rest) SplitVerb(string line)
    {
        int i = 0;
        while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
        string verb = line[..i];
        string rest = i < line.Length ? line[i..].TrimStart() : string.Empty;
        return (verb, rest);
    }

    private static (List<ScriptArg>? args, ParseError? error) ParseArgs(string s, int lineNum)
    {
        var args = new List<ScriptArg>();
        int pos  = 0;

        while (pos < s.Length)
        {
            // skip whitespace
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
            if (pos >= s.Length) break;

            char ch = s[pos];

            // inline comment
            if (ch == '#') break;

            // quoted string
            if (ch == '"')
            {
                pos++;
                var sb = new System.Text.StringBuilder();
                while (pos < s.Length && s[pos] != '"')
                {
                    if (s[pos] == '\\' && pos + 1 < s.Length)
                    {
                        pos++;
                        sb.Append(s[pos] switch
                        {
                            'n'  => '\n',
                            't'  => '\t',
                            'r'  => '\r',
                            '\\' => '\\',
                            '"'  => '"',
                            _    => s[pos]
                        });
                    }
                    else
                    {
                        sb.Append(s[pos]);
                    }
                    pos++;
                }
                if (pos >= s.Length)
                    return (null, new ParseError(lineNum, "Unterminated string literal."));
                pos++; // closing "
                args.Add(ScriptArg.Text(sb.ToString()));
                continue;
            }

            // flag: /word
            if (ch == '/')
            {
                pos++;
                int start = pos;
                while (pos < s.Length && char.IsLetterOrDigit(s[pos])) pos++;
                string flag = s[start..pos];
                if (flag.Length == 0)
                    return (null, new ParseError(lineNum, "Empty flag after '/'."));
                args.Add(ScriptArg.Flag(flag));
                continue;
            }

            // number (optional leading -)
            if (char.IsDigit(ch) || (ch == '-' && pos + 1 < s.Length && char.IsDigit(s[pos + 1])))
            {
                int start = pos;
                if (ch == '-') pos++;
                while (pos < s.Length && char.IsDigit(s[pos])) pos++;
                if (!int.TryParse(s[start..pos], out int n))
                    return (null, new ParseError(lineNum, $"Integer overflow near '{s[start..pos]}'."));
                args.Add(ScriptArg.Number(n));
                continue;
            }

            // unquoted token (e.g. bare word used as text) — read to next whitespace
            {
                int start = pos;
                while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '#') pos++;
                args.Add(ScriptArg.Text(s[start..pos]));
            }
        }

        return (args, null);
    }
}

public readonly record struct ParseError(int Line, string Message)
{
    public override string ToString() => $"Line {Line}: {Message}";
}

public sealed class ParseResult
{
    public IReadOnlyList<ScriptCommand> Commands { get; }
    public IReadOnlyList<ParseError>   Errors   { get; }
    public bool Success => Errors.Count == 0;

    public ParseResult(IReadOnlyList<ScriptCommand> commands, IReadOnlyList<ParseError> errors)
    {
        Commands = commands;
        Errors   = errors;
    }
}
