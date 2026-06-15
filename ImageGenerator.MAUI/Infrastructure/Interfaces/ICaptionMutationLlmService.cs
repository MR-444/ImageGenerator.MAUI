using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Which model the AI caption mutator talks to for a run.</summary>
public enum ModelTier
{
    /// <summary>Claude Sonnet 4.6 — the affordable default for directed mutation.</summary>
    Sonnet,

    /// <summary>Claude Opus 4.8 — the stronger (pricier) tier.</summary>
    Opus,

    /// <summary>A local Ollama model (e.g. on fireEngine) — FREE, for verifying the technical
    /// round-trip; output quality is not a goal of this tier.</summary>
    Local
}

/// <summary>
/// LLM-driven caption mutation: rewrites a structured Ideogram V4 caption to a new creative direction,
/// coherently across the WHOLE scene (medium + lighting + palette + every element desc together) — the
/// thing the deterministic <c>CaptionMutationEngine</c> cannot do. The additive, model-agnostic swap
/// seam: a better model drops in behind this interface. NOT reproducible (no seed); the render seed is
/// still pinned across the batch so only the caption differs.
/// </summary>
public interface ICaptionMutationLlmService
{
    /// <summary>
    /// ONE call → ONE validated variant of <paramref name="baseCaption"/> steered by
    /// <paramref name="steer"/> (free text, e.g. "make it winter"). Callers fan out N with bounded
    /// concurrency; <paramref name="index"/> nudges the model toward a distinct direction per variant
    /// (no seed/temperature is available). Validator-gated with one feedback retry.
    /// </summary>
    Task<LlmVariantResult> MutateAsync(
        V4JsonPrompt baseCaption, string steer, int index, ModelTier tier, CancellationToken ct = default);

    /// <summary>
    /// Crossbreed/mutate around the marked <paramref name="winners"/> into ONE new validated offspring
    /// caption, optionally steered. Same one-call/fan-out/validate-retry contract as
    /// <see cref="MutateAsync"/>.
    /// </summary>
    Task<LlmVariantResult> BreedAsync(
        IReadOnlyList<V4JsonPrompt> winners, string steer, int index, ModelTier tier, CancellationToken ct = default);
}

/// <summary>Outcome of one LLM mutation call: either a validated V4 caption (+ a short job-card label)
/// or a user-facing error.</summary>
public sealed record LlmVariantResult(bool Success, V4JsonPrompt? Prompt, string? Label, string? Error)
{
    public static LlmVariantResult Ok(V4JsonPrompt prompt, string label) => new(true, prompt, label, null);
    public static LlmVariantResult Fail(string error) => new(false, null, null, error);
}
