namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// One produced variant: its canonical compact caption string (ready to feed the batch) plus the
/// provenance needed to reproduce it.
/// </summary>
/// <param name="Caption">Canonical, validator-clean compact caption string.</param>
/// <param name="OperatorName">The single operator applied, or <c>null</c> for the reference base (variant 0).</param>
/// <param name="SubSeed">Sub-seed derived from the run seed that produced this variant.</param>
public sealed record MutationVariant(string Caption, string? OperatorName, int SubSeed);

/// <summary>
/// Result of a mutation run: the produced <see cref="MutationVariant"/>s in order (the reference base
/// first when included) and a human-readable log of dropped attempts (operator + reason) for diagnostics.
/// </summary>
/// <param name="Variants">Produced variants, in emission order.</param>
/// <param name="DropLog">Diagnostic lines for attempts the engine rejected and resampled.</param>
public sealed record MutationRunResult(
    IReadOnlyList<MutationVariant> Variants,
    IReadOnlyList<string> DropLog);
