namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Behavior of a single mutation run. Lives as data (not code branches) so later phases can add
/// configurable-k, per-gene probability, operator/library selection, and bbox strength as new fields
/// without rearchitecting the engine. Phase 0 carries only the fields the skeleton needs.
/// </summary>
public sealed record MutationRunConfig
{
    /// <summary>The pinned axis; the engine mutates only operators of this axis.</summary>
    public required MutationAxis Axis { get; init; }

    /// <summary>Number of variants to produce, before <see cref="IncludeBaseAsReference"/>. Clamped by the engine.</summary>
    public required int Count { get; init; }

    /// <summary>Seed for the single run-wide <see cref="Random"/>; same base + config ⇒ identical output.</summary>
    public required int Seed { get; init; }

    /// <summary>Target frame width in pixels (AR reference for bbox operators).</summary>
    public required int TargetWidth { get; init; }

    /// <summary>Target frame height in pixels (AR reference for bbox operators).</summary>
    public required int TargetHeight { get; init; }

    /// <summary>When true (default), the unmutated base is emitted as "variant 0" for side-by-side comparison.</summary>
    public bool IncludeBaseAsReference { get; init; } = true;

    /// <summary>
    /// Geometry-mutation magnitude for the SCENE bbox / placement operators. Defaults to Moderate. The
    /// Phase 3 engine threads this onto the <see cref="MutationContext"/> it builds.
    /// </summary>
    public MutationStrength Strength { get; init; } = MutationStrength.Moderate;

    /// <summary>
    /// LOOK-only: when set to a library style fragment's name, every LOOK variant swaps to exactly that
    /// style instead of a random one (the engine restricts the run to the style swap and the swap
    /// operator targets this fragment). <c>null</c> (default) = the original random-per-variant behavior.
    /// </summary>
    public string? PinnedStyleName { get; init; }
}
