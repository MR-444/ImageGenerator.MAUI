using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Read-only context handed to every <see cref="ICaptionOperator"/> for a single run. Carries the
/// target frame dimensions (so bbox operators can stay aspect-ratio aware), the resolved slot-tag map
/// for the base caption's elements, and the library operators draw style fragments / ornament kits from.
/// </summary>
public sealed class MutationContext
{
    public MutationContext(
        int targetWidth,
        int targetHeight,
        IReadOnlyDictionary<Element, string> tags,
        MutationLibrary library,
        MutationStrength strength = MutationStrength.Moderate,
        string? pinnedStyleName = null)
    {
        TargetWidth = targetWidth;
        TargetHeight = targetHeight;
        Tags = tags;
        Library = library;
        Strength = strength;
        PinnedStyleName = pinnedStyleName;
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

    /// <summary>Style fragments and ornament kits operators choose from.</summary>
    public MutationLibrary Library { get; }

    /// <summary>Perturbation magnitude for the geometry operators (bbox / placement). Defaults to Moderate.</summary>
    public MutationStrength Strength { get; }

    /// <summary>When set, <see cref="Operators.SwapStyleOperator"/> swaps to exactly this named fragment
    /// instead of a random one. <c>null</c> = random draw (the default).</summary>
    public string? PinnedStyleName { get; }
}
