namespace TextEditor.Core.Language.TmLanguage;

/// <summary>
/// An immutable scope stack frame representing an active begin/end rule.
/// </summary>
internal sealed class TmScopeFrame : IEquatable<TmScopeFrame>
{
    /// <summary>The rule that opened this scope.</summary>
    public TmRule Rule { get; }

    /// <summary>The resolved end pattern (with \1 etc. substituted from begin capture).</summary>
    public string ResolvedEnd { get; }

    /// <summary>The scope name for content inside this frame.</summary>
    public string? ContentName { get; }

    public TmScopeFrame(TmRule rule, string resolvedEnd, string? contentName)
    {
        Rule        = rule;
        ResolvedEnd = resolvedEnd;
        ContentName = contentName;
    }

    public bool Equals(TmScopeFrame? other) =>
        other is not null &&
        ReferenceEquals(Rule, other.Rule) &&
        ResolvedEnd == other.ResolvedEnd;

    public override bool Equals(object? obj) => obj is TmScopeFrame f && Equals(f);

    public override int GetHashCode() => HashCode.Combine(
        System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Rule),
        ResolvedEnd);
}

/// <summary>
/// An immutable snapshot of the scope stack at the end of a line.
/// Value-equality semantics allow the LineHighlightCache to detect state changes.
/// </summary>
internal sealed class TmScopeStack : IEquatable<TmScopeStack>
{
    public static readonly TmScopeStack Root = new([]);

    public IReadOnlyList<TmScopeFrame> Frames { get; }

    public TmScopeStack(IReadOnlyList<TmScopeFrame> frames)
    {
        Frames = frames;
    }

    public bool Equals(TmScopeStack? other)
    {
        if (other is null) return false;
        if (Frames.Count != other.Frames.Count) return false;
        for (int i = 0; i < Frames.Count; i++)
            if (!Frames[i].Equals(other.Frames[i])) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is TmScopeStack s && Equals(s);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var f in Frames) hc.Add(f);
        return hc.ToHashCode();
    }
}

/// <summary>
/// Maps TmScopeStack instances to small integer IDs and back.
/// State 0 is always TmScopeStack.Root.
/// Thread-safe: all mutations are lock-protected.
/// </summary>
internal sealed class TmStateTable
{
    private readonly List<TmScopeStack>               _stacks  = [TmScopeStack.Root];
    private readonly Dictionary<TmScopeStack, int>    _index   = new() { [TmScopeStack.Root] = 0 };
    private readonly object                           _lock    = new();

    public int GetOrAdd(TmScopeStack stack)
    {
        lock (_lock)
        {
            if (_index.TryGetValue(stack, out int id)) return id;
            id = _stacks.Count;
            _stacks.Add(stack);
            _index[stack] = id;
            return id;
        }
    }

    public TmScopeStack Get(int id)
    {
        lock (_lock)
        {
            return id >= 0 && id < _stacks.Count ? _stacks[id] : TmScopeStack.Root;
        }
    }
}
