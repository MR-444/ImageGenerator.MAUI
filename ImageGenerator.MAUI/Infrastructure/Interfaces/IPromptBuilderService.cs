using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Turns a freeform idea into a usable prompt in two passes. Pass 1 (<see cref="BuildProseAsync"/>,
/// "VPE") emits a normal prose prompt every plain-prompt model in the catalog can consume directly.
/// Pass 2 (<see cref="BuildJsonAsync"/>) is an Ideogram adapter that maps that prose onto the
/// schema-valid V4 structure. The "authoring half" of the pipeline — the runtime half
/// (<see cref="V4JsonPrompt"/>, serializer, validator, structure editor, mutation engine) already
/// exists. This is the swap seam: callers can pick a cloud Anthropic tier or the local Ollama tier,
/// while each provider owns its own structured-output mechanism.
/// </summary>
public interface IPromptBuilderService
{
    /// <summary>Pass 1: idea → a normal prose image prompt (no JSON, no validator). Usable on its own.</summary>
    Task<ProseResult> BuildProseAsync(
        string idea,
        CancellationToken cancellationToken = default,
        ModelTier tier = ModelTier.Opus);

    /// <summary>Pass 2: prose → a schema-valid Ideogram V4 structured prompt (structured output + validate-retry).</summary>
    Task<PromptBuilderResult> BuildJsonAsync(
        string prose,
        CancellationToken cancellationToken = default,
        ModelTier tier = ModelTier.Opus);
}

/// <summary>Outcome of the VPE pass: either a prose prompt or a user-facing error.</summary>
public sealed record ProseResult(bool Success, string? Prose, string? Error)
{
    public static ProseResult Ok(string prose) => new(true, prose, null);

    public static ProseResult Fail(string error) => new(false, null, error);
}

/// <summary>Outcome of the JSON pass: either a validated <see cref="V4JsonPrompt"/> or a user-facing error.</summary>
public sealed record PromptBuilderResult(bool Success, V4JsonPrompt? Prompt, string? Error)
{
    public static PromptBuilderResult Ok(V4JsonPrompt prompt) => new(true, prompt, null);

    public static PromptBuilderResult Fail(string error) => new(false, null, error);
}
