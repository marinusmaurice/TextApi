using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit.Abstractions;

namespace TextEditor.Tests;

public sealed class BenchmarkResult
{
    public string Suite { get; set; } = "";
    public string Name { get; set; } = "";
    public long Ms { get; set; }
    public string Label { get; set; } = "";
    public string Extra { get; set; } = "";
}

public sealed class BenchmarkRun
{
    public string RunId { get; set; } = "";
    public string Timestamp { get; set; } = "";
    public string Machine { get; set; } = "";
    public List<BenchmarkResult> Results { get; set; } = [];
}

/// <summary>
/// Appends individual benchmark results to a JSON log file.
/// Each call to Record() writes one result immediately so we don't lose data
/// even if the test runner crashes or OOMs mid-suite.
/// The history file lives at the solution root: TextEditorApi/BenchmarkHistory.json
/// </summary>
public static class BenchmarkHistory
{
    // Walks up from bin/Debug/net8.0 → Tests → src → TextEditorApi (solution root)
    private static readonly string HistoryFile =
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "BenchmarkHistory.json"));

    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    // Shared run ID for the entire process lifetime — all tests in one dotnet test run
    // share the same run ID so they group together in the history table.
    internal static readonly string CurrentRunId =
        Guid.NewGuid().ToString("N")[..8];
    private static readonly string CurrentTimestamp =
        DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm");

    private static readonly object _lock = new();

    public static void Record(string suite, string name, long ms, string label = "", string extra = "")
    {
        lock (_lock)
        {
            try
            {
                var history = Load();

                // Find or create the current run entry
                var run = history.FirstOrDefault(r => r.RunId == CurrentRunId);
                if (run == null)
                {
                    run = new BenchmarkRun
                    {
                        RunId = CurrentRunId,
                        Timestamp = CurrentTimestamp,
                        Machine = Environment.MachineName,
                        Results = []
                    };
                    history.Add(run);
                }

                // Update or add the result
                var existing = run.Results.FirstOrDefault(r => r.Suite == suite && r.Name == name && r.Label == label);
                if (existing != null) { existing.Ms = ms; existing.Extra = extra; }
                else run.Results.Add(new BenchmarkResult { Suite = suite, Name = name, Ms = ms, Label = label, Extra = extra });

                // Keep last 50 runs
                if (history.Count > 50) history = history[^50..];
                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(history, Opts));
            }
            catch { /* never fail a test due to history I/O */ }
        }
    }

    public static List<BenchmarkRun> Load()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return [];
            return JsonSerializer.Deserialize<List<BenchmarkRun>>(
                File.ReadAllText(HistoryFile), Opts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>
    /// Print a comparison table of the last N runs to the test output.
    /// Δ% is vs the immediately previous run.
    /// Green = faster (negative Δ), Red = slower (positive Δ).
    /// </summary>
    public static void PrintComparison(ITestOutputHelper out_, int lastNRuns = 5)
    {
        var history = Load();
        if (history.Count == 0) return;

        var runs = history.TakeLast(lastNRuns).ToList();
        var names = runs.SelectMany(r => r.Results.Select(b => $"{b.Suite}|{b.Name}|{b.Label}"))
                        .Distinct().ToList();

        out_.WriteLine("");
        out_.WriteLine("┌──────────────────────────────────────────────────────────────────────────────┐");
        out_.WriteLine($"│  BENCHMARK HISTORY — last {runs.Count} run(s){"".PadRight(Math.Max(0, 51 - runs.Count.ToString().Length))}│");
        out_.WriteLine("├──────────────────────────────────────────────────────────────────────────────┤");

        var hdr = "│  Benchmark + label".PadRight(38);
        foreach (var r in runs) hdr += $"  {r.RunId,8}";
        out_.WriteLine(Clip(hdr + "  │"));

        var ts = "│  " + "Date/time".PadRight(36);
        foreach (var r in runs) ts += $"  {r.Timestamp[5..],8}";
        out_.WriteLine(Clip(ts + "  │"));

        out_.WriteLine("├──────────────────────────────────────────────────────────────────────────────┤");

        foreach (var key in names)
        {
            var parts = key.Split('|');
            // parts[0]=Suite, parts[1]=Name, parts[2]=Label
            string nameLabel = parts[1] + (parts[2].Length > 0 ? $" [{parts[2]}]" : "");
            string suitePrefix = parts[0].Length > 0 ? $"{parts[0]}: " : "";
            string display = (suitePrefix + nameLabel).Length > 35
                ? (suitePrefix + nameLabel)[..35]
                : suitePrefix + nameLabel;

            var row = "│  " + display.PadRight(36);
            BenchmarkResult? prev = null;
            foreach (var run in runs)
            {
                var b = run.Results.FirstOrDefault(x => $"{x.Suite}|{x.Name}|{x.Label}" == key);
                if (b == null) { row += "         -"; prev = null; continue; }

                string cell;
                if (prev != null && prev.Ms > 0)
                {
                    double pct = (b.Ms - prev.Ms) * 100.0 / prev.Ms;
                    string sign = pct > 0 ? "+" : "";
                    cell = $"{b.Ms}ms{sign}{pct:0}%".PadLeft(10);
                }
                else cell = $"{b.Ms}ms".PadLeft(10);

                row += cell;
                prev = b;
            }
            out_.WriteLine(Clip(row + "  │"));
        }

        out_.WriteLine("└──────────────────────────────────────────────────────────────────────────────┘");
        out_.WriteLine("");

        static string Clip(string s) => s.Length > 80 ? s[..80] : s;
    }
}

/// <summary>
/// Lightweight recorder — call Record() in each timed test.
/// Call PrintHistory() at the end of a test to show the comparison table.
/// </summary>
public sealed class BenchmarkSession
{
    private readonly string _suite;
    private readonly ITestOutputHelper _out;

    public BenchmarkSession(string suite, ITestOutputHelper out_)
    {
        _suite = suite;
        _out = out_;
    }

    public long Record(string name, string label, Action action, string extra = "")
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        action();
        long ms = sw.ElapsedMilliseconds;
        _out.WriteLine($"{name} [{label}]: {ms}ms  {extra}");
        Debug.WriteLine("REcord");
        BenchmarkHistory.Record(_suite, name, ms, label, extra);
        return ms;
    }

    public void PrintHistory(int lastNRuns = 5)
        => BenchmarkHistory.PrintComparison(_out, lastNRuns);
}
