using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextEditor.PerfViewer;

public sealed class BenchmarkResult
{
    public string Suite { get; set; } = "";
    public string Name  { get; set; } = "";
    public long   Ms    { get; set; }
    public string Label { get; set; } = "";
    public string Extra { get; set; } = "";

    [JsonIgnore]
    public string FullName => Label.Length > 0 ? $"{Name} [{Label}]" : Name;

    [JsonIgnore]
    public string SuiteFullName => Suite.Length > 0 ? $"{Suite}: {FullName}" : FullName;
}

public sealed class BenchmarkRun
{
    public string              RunId     { get; set; } = "";
    public string              Timestamp { get; set; } = "";
    public string              Machine   { get; set; } = "";
    public List<BenchmarkResult> Results { get; set; } = [];
    public object Suite { get; internal set; }
}

public static class HistoryLoader
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static List<BenchmarkRun> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<BenchmarkRun>>(
                File.ReadAllText(path), Opts) ?? [];
        }
        catch { return []; }
    }

    /// <summary>Resolve the history file from a starting path, walking up to find it.</summary>
    public static string? FindHistoryFile(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "BenchmarkHistory.json");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>All unique benchmark keys (Suite|Name|Label) across all runs, sorted.</summary>
    public static List<string> GetAllBenchmarkNames(List<BenchmarkRun> runs)
        => runs.SelectMany(r => r.Results.Select(b => $"{b.Suite}|{b.Name}|{b.Label}"))
               .Distinct()
               .OrderBy(n => n)
               .ToList();

    /// <summary>Group runs by their dominant suite (most common suite in Results).</summary>
    public static IEnumerable<IGrouping<string, BenchmarkRun>> GroupBySuite(List<BenchmarkRun> runs)
        => runs.GroupBy(r =>
        {
            var dominant = r.Results
                .GroupBy(b => b.Suite)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key ?? "Other";
            return string.IsNullOrEmpty(dominant) ? "Other" : dominant;
        });

    /// <summary>Delta % between two values, null if prev is zero.</summary>
    public static double? Delta(long prev, long cur)
        => prev == 0 ? null : (cur - prev) * 100.0 / prev;
}

/// <summary>ListBox item that stores the Suite|Name|Label key separately from the display string.</summary>
public sealed class BenchItem(string key, string display)
{
    public string Key { get; } = key;
    public override string ToString() => display;
}
