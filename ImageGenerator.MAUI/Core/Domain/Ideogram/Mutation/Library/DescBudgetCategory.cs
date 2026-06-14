namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// Drop-priority tier of an ornament phrase under the 60-word desc budget. When a desc is over budget,
/// <c>DescBudget</c> drops phrases highest-value-last: environmental micro-detail goes first, then color
/// detail beyond the harmony, then secondary frame devices, then style markers. <see cref="Protected"/>
/// is never dropped (subject identity, scale anchor, facing/occlusion cues). Enum values are ordered so a
/// higher value is dropped earlier.
/// </summary>
public enum DescBudgetCategory
{
    /// <summary>Never trimmed — subject identity, scale anchor, facing/occlusion. (Base desc prose.)</summary>
    Protected = 0,

    /// <summary>Style markers (finish, rendering cues) — dropped last among droppable tiers.</summary>
    StyleMarker = 1,

    /// <summary>Secondary frame devices (borders, vignettes, incidental staging).</summary>
    SecondaryFrameDevice = 2,

    /// <summary>Color detail beyond the established harmony.</summary>
    ColorBeyondHarmony = 3,

    /// <summary>Environmental micro-detail — dropped first.</summary>
    EnvironmentalMicroDetail = 4
}
