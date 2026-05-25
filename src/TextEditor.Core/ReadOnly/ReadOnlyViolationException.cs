namespace TextEditor.Core.ReadOnly;

/// <summary>
/// Thrown when an edit attempts to modify a range that is protected by a
/// <see cref="ReadOnlyRegionModel"/> and <see cref="TextDocument.EnforceReadOnly"/> is
/// <see langword="true"/>.
/// </summary>
public sealed class ReadOnlyViolationException : InvalidOperationException
{
    /// <summary>Start of the protected region that was violated.</summary>
    public int RegionStart { get; }

    /// <summary>End (exclusive) of the protected region that was violated.</summary>
    public int RegionEnd { get; }

    /// <summary>The offset at which the edit was attempted.</summary>
    public int EditOffset { get; }

    internal ReadOnlyViolationException(int regionStart, int regionEnd, int editOffset)
        : base($"Edit at offset {editOffset} conflicts with read-only region [{regionStart}, {regionEnd}).")
    {
        RegionStart = regionStart;
        RegionEnd   = regionEnd;
        EditOffset  = editOffset;
    }
}
