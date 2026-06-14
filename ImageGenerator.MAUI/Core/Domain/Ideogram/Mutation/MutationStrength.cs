namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Perturbation magnitude for the continuous geometry mutations (bbox translate/scale/jitter and new-element
/// placement). Maps to a Gaussian sigma in grid units on the fixed 0–1000 canvas via
/// <see cref="BboxMath.SigmaFor"/>: Subtle ≈ 2%, Moderate ≈ 5%, Bold ≈ 10%. Categorical / library draws
/// ignore this; only spatial operators read it.
/// </summary>
public enum MutationStrength
{
    Subtle,
    Moderate,
    Bold
}
