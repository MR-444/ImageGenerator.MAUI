namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Read-only context handed to every <see cref="ICaptionOperator"/> for a single run. Carries the
/// target frame dimensions (so bbox operators can stay aspect-ratio aware) and the resolved slot-tag
/// map for the base caption's elements, keyed by element reference.
/// </summary>
/// <remarks>
/// A <c>MutationLibrary</c> member (style fragments / ornament kits) joins this context once the
/// library type exists in a later phase; operators that need it are not built yet.
/// </remarks>
public sealed class MutationContext
{
    public MutationContext(int targetWidth, int targetHeight, IReadOnlyDictionary<Element, string> tags)
    {
        TargetWidth = targetWidth;
        TargetHeight = targetHeight;
        Tags = tags;
    }

    /// <summary>Target output width in pixels — the AR reference for bbox operators.</summary>
    public int TargetWidth { get; }

    /// <summary>Target output height in pixels — the AR reference for bbox operators.</summary>
    public int TargetHeight { get; }

    /// <summary>
    /// Resolved slot tags for the base caption's elements, keyed by element reference. Operators read
    /// tags from here rather than from a cloned element (the clone path strips <see cref="Element.SlotTag"/>).
    /// </summary>
    public IReadOnlyDictionary<Element, string> Tags { get; }
}
