namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// A single mutation operator: applies exactly one change to a caption. Operators are plugins behind
/// this contract so adding, removing, or retuning one never touches the engine, UI, or batch — the
/// engine just iterates a chosen set.
/// </summary>
/// <remarks>
/// Contract for implementations (enforced by tests, not the compiler):
/// <list type="bullet">
/// <item>Pure — never mutate <paramref name="source"/>; work on a deep clone (see <see cref="CaptionClone"/>).</item>
/// <item>Deterministic — draw all randomness from the injected <c>rng</c>, never <c>Random.Shared</c>.</item>
/// <item>Self-validating — return a validator-clean caption, or <c>null</c> when the change cannot be
/// made legal (the engine logs the drop and resamples).</item>
/// </list>
/// </remarks>
public interface ICaptionOperator
{
    /// <summary>The axis this operator belongs to; the engine only runs operators on the pinned axis.</summary>
    MutationAxis Axis { get; }

    /// <summary>Stable identifier recorded in variant provenance (e.g. "SwapStyle").</summary>
    string Name { get; }

    /// <summary>
    /// Produces one mutated caption from <paramref name="source"/>, or <c>null</c> if no legal mutation
    /// could be produced this attempt.
    /// </summary>
    V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context);
}
