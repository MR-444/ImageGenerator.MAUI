using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The single place a companion page (IdeaToPrompt, MutationEngine, the Ideogram structure editor)
/// resolves the host generator's settings into the strings its own services need.
///
/// Those pages bind the pickers straight through <c>Generator.…</c>, but every one of them also had
/// to re-derive the same fallbacks in C#, and the fallbacks are the part that must not drift per
/// page: a blank pick is blank everywhere, and "there is no default Ollama model" is a deliberate
/// contract — a guessed model only ever produced an opaque 404 from a server that never had it, so
/// callers must surface a "pick a model first" error instead of guessing.
///
/// Holding still during the owner's lifetime is fine: these VMs are transient (fresh per
/// navigation) and the generator is a singleton. It takes the settings coordinator rather than the
/// generator, so a companion page can no longer reach any generator member from here.
/// </summary>
internal sealed class GeneratorSettingsReader(AppSettingsCoordinator? settings)
{
    /// <summary>The Ollama endpoint the Local tier talks to: the generator's setting, else the
    /// default (fireEngine). Shared by the unload calls and the GPU-gate same-host check.</summary>
    public string OllamaBaseUrl =>
        settings?.OllamaBaseUrl is { Length: > 0 } u ? u : ModelConstants.Ollama.DefaultBaseUrl;

    /// <summary>The generation model — the image model the host generates with. Companion pages
    /// read it for nothing but reporting; none of them may call it "their" model, which is what the
    /// three role picks below are for.</summary>
    public string OllamaModel =>
        settings?.OllamaModel ?? string.Empty;

    /// <summary>The Local-tier tag this page's own LLM role calls, empty when unset — deliberately
    /// not defaulted, and deliberately NOT <see cref="OllamaModel"/>: sharing that one value across
    /// four roles is the bug documents/plan-model-picker-rows.md exists to fix. The empty string is
    /// what callers test to refuse a run before any model call is made.</summary>
    public string PromptWriterModel =>
        settings?.PromptWriterModel ?? string.Empty;

    /// <inheritdoc cref="PromptWriterModel" />
    public string MutationModel =>
        settings?.MutationModel ?? string.Empty;

    /// <inheritdoc cref="PromptWriterModel" />
    public string EnrichModel =>
        settings?.EnrichModel ?? string.Empty;

    /// <summary>The vision pick, empty when unset. It no longer falls back to the text model: that
    /// value is the image generation model, which cannot observe an image.</summary>
    public string OllamaVisionModel =>
        settings?.OllamaVisionModel ?? string.Empty;

    /// <summary>The picked OpenRouter vision model, empty when unset — unlike the local pick there
    /// is nothing sensible to fall back to, so an empty value blocks the observation with a
    /// "pick a vision model first" error.</summary>
    public string OpenRouterVisionModel =>
        settings?.OpenRouterVisionModel is { Length: > 0 } m ? m : string.Empty;

    /// <summary>
    /// The target an LLM service should be handed for one of the page's still tier-shaped pickers:
    /// Local becomes "call this Ollama tag at this endpoint", Sonnet/Opus become "call that Claude
    /// model". Transitional on purpose — the pages keep their three-value picker until
    /// documents/plan-model-picker-rows.md Phase B replaces it with provider + model, at which point the
    /// page builds the target directly and this trio goes away.
    /// </summary>
    public LlmTarget PromptWriterTarget(ModelTier tier) =>
        TargetForTier(tier, PromptWriterModel);

    /// <inheritdoc cref="PromptWriterTarget" />
    public LlmTarget MutationTarget(ModelTier tier) =>
        TargetForTier(tier, MutationModel);

    /// <inheritdoc cref="PromptWriterTarget" />
    public LlmTarget EnrichTarget(ModelTier tier) =>
        TargetForTier(tier, EnrichModel);

    private LlmTarget TargetForTier(ModelTier tier, string localModel) => tier == ModelTier.Local
        ? new LlmTarget(ModelProvider.Local, localModel, OllamaBaseUrl)
        : new LlmTarget(
            ModelProvider.Anthropic,
            tier == ModelTier.Opus ? AnthropicModels.Opus : AnthropicModels.Sonnet,
            string.Empty);

    /// <summary>True when ComfyUI and Ollama are the same box, i.e. when they share VRAM and the GPU
    /// gate has to serialize a local call against an in-flight render. A null/blank ComfyUI URL
    /// falls back to its own default inside <see cref="GpuColocation"/>.</summary>
    public bool SharesGpuWithComfyUi =>
        GpuColocation.SameHost(settings?.ComfyUiBaseUrl, OllamaBaseUrl);
}
