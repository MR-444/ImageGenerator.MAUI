using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Turns a freeform idea into a schema-valid Ideogram V4 structured prompt via a single text-LLM
/// call. The "authoring half" of the pipeline — the runtime half (<see cref="V4JsonPrompt"/>,
/// serializer, validator, structure editor, mutation engine) already exists. This is the swap seam:
/// the only implementation today is Anthropic Opus 4.8, but a future provider/model picker is purely
/// additive behind this interface (each provider owns its own structured-output mechanism).
/// </summary>
public interface IPromptBuilderService
{
    Task<PromptBuilderResult> BuildAsync(string idea, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a build: either a validated <see cref="V4JsonPrompt"/> or a user-facing error.</summary>
public sealed record PromptBuilderResult(bool Success, V4JsonPrompt? Prompt, string? Error)
{
    public static PromptBuilderResult Ok(V4JsonPrompt prompt) => new(true, prompt, null);

    public static PromptBuilderResult Fail(string error) => new(false, null, error);
}
