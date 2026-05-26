namespace TextAPI.Core.Diff;

/// <summary>Whether a <see cref="DiffHunk"/> represents equal, inserted, or deleted content.</summary>
public enum DiffKind { Equal, Insert, Delete }

/// <summary>
/// A contiguous region of the diff — one of: unchanged lines, inserted lines, or deleted lines.
/// </summary>
public sealed class DiffHunk
{
    /// <summary>Equal, Insert, or Delete.</summary>
    public DiffKind Kind { get; }

    /// <summary>Zero-based start line in the old document. For Insert, the insertion point.</summary>
    public int OldStart { get; }

    /// <summary>Number of old lines involved. Zero for Insert.</summary>
    public int OldCount { get; }

    /// <summary>Zero-based start line in the new document. For Delete, the deletion point.</summary>
    public int NewStart { get; }

    /// <summary>Number of new lines involved. Zero for Delete.</summary>
    public int NewCount { get; }

    /// <summary>
    /// Line content.
    /// <list type="bullet">
    ///   <item>Equal — lines from the old document (same in both).</item>
    ///   <item>Delete — lines from the old document that were removed.</item>
    ///   <item>Insert — lines from the new document that were added.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<string> Lines { get; }

    internal DiffHunk(DiffKind kind,
                      int oldStart, int oldCount,
                      int newStart, int newCount,
                      IReadOnlyList<string> lines)
    {
        Kind     = kind;
        OldStart = oldStart;
        OldCount = oldCount;
        NewStart = newStart;
        NewCount = newCount;
        Lines    = lines;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Kind} old={OldStart}+{OldCount} new={NewStart}+{NewCount}";
}

/// <summary>
/// A character-level diff span — one piece of text that is equal, deleted, or inserted.
/// Returned by <see cref="TextDiff.DiffChars"/>.
/// </summary>
public readonly record struct DiffSpan(DiffKind Kind, string Text);
