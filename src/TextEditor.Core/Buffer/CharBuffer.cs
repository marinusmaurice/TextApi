namespace TextEditor.Core.Buffer;

/// <summary>
/// A growable, array-backed char buffer that exposes ReadOnlySpan/ReadOnlyMemory
/// slices with ZERO allocation on hot paths.
///
/// PERFORMANCE RATIONALE:
///   • string.Substring() and StringBuilder.ToString(start, len) both heap-allocate
///     on every call. At 100 MB with thousands of pieces that's constant GC pressure.
///   • char[] lets us call AsSpan(start, length) → ReadOnlySpan → ZERO alloc.
///   • The original buffer is sealed once on load; the add buffer doubles in capacity
///     like List<T> so appends are amortised O(1) with no copying on the hot path.
/// </summary>
internal sealed class CharBuffer
{
    private char[] _data;
    private int    _length;

    internal CharBuffer(int initialCapacity = 64)
    {
        _data   = new char[Math.Max(initialCapacity, 64)];
        _length = 0;
    }

    /// <summary>Build a sealed original buffer directly from a char span (zero extra alloc).</summary>
    internal CharBuffer(ReadOnlySpan<char> source)
    {
        _data   = source.ToArray();   // one alloc, sealed from here
        _length = _data.Length;
    }

    internal int    Length   => _length;
    /// <summary>Direct access to backing array for ArrayPool-style patterns. Never resize while holding this.</summary>
    internal char[] RawArray => _data;

    /// <summary>Zero-alloc span slice. This is the hot-path read primitive.</summary>
    internal ReadOnlySpan<char> Slice(int start, int length)
        => new ReadOnlySpan<char>(_data, start, length);

    /// <summary>Zero-alloc memory slice (for async / non-span contexts).</summary>
    internal ReadOnlyMemory<char> SliceMemory(int start, int length)
        => new ReadOnlyMemory<char>(_data, start, length);

    /// <summary>
    /// Append chars to the add buffer. Returns the start offset of the appended data.
    /// </summary>
    internal int Append(ReadOnlySpan<char> chars)
    {
        int start = _length;
        EnsureCapacity(_length + chars.Length);
        chars.CopyTo(new Span<char>(_data, _length, chars.Length));
        _length += chars.Length;
        return start;
    }

    /// <summary>Replace contents entirely (used by Compact).</summary>
    internal void Reset(ReadOnlySpan<char> newContent)
    {
        if (newContent.Length > _data.Length)
            _data = new char[newContent.Length];
        newContent.CopyTo(_data);
        _length = newContent.Length;
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _data.Length) return;
        int next = Math.Max(_data.Length * 2, required);
        var newData = new char[next];
        _data.AsSpan(0, _length).CopyTo(newData);
        _data = newData;
    }
}
