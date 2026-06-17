using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Region-aware caption enrichment: rewrites ONLY each element's <c>desc</c> so it reflects the element's
/// spatial relationship to its neighbours (relative position, support/contact, background band, and a soft
/// occlusion hint). A deterministic <c>RegionGraph</c> computes the spatial facts; this seam hands them to
/// an LLM as ground truth. Unlike <see cref="ICaptionMutationLlmService"/> it does NOT reimagine the scene —
/// the count, types, text, and bboxes of the elements (and the headline/style/background) are preserved.
/// </summary>
public interface IEnrichRegionsLlmService
{
    /// <summary>
    /// ONE call → ONE validated caption that is <paramref name="baseCaption"/> with every element's
    /// <c>desc</c> rewritten for spatial awareness. Validator-gated AND preservation-gated (only descs may
    /// change) with one feedback retry. Returns a failure result rather than throwing on a bad model reply.
    /// </summary>
    Task<LlmVariantResult> EnrichAsync(
        V4JsonPrompt baseCaption, ModelTier tier, CancellationToken ct = default);
}
