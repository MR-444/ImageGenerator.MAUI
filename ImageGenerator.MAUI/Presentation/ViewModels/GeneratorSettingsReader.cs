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

    /// <summary>The picked local model, empty when unset — deliberately not defaulted; the empty
    /// string is what the callers test to refuse the run before any model call is made.</summary>
    public string OllamaModel =>
        settings?.OllamaModel ?? string.Empty;

    /// <summary>The vision pick, falling back to the text model: a vision-capable local model
    /// doubles as the captioner, so the explicit vision pick is an override, not a requirement.</summary>
    public string OllamaVisionModel =>
        settings?.OllamaVisionModel is { Length: > 0 } m ? m : OllamaModel;

    /// <summary>The picked OpenRouter vision model, empty when unset — unlike the local pick there
    /// is nothing sensible to fall back to, so an empty value blocks the observation with a
    /// "pick a vision model first" error.</summary>
    public string OpenRouterVisionModel =>
        settings?.OpenRouterVisionModel is { Length: > 0 } m ? m : string.Empty;

    /// <summary>True when ComfyUI and Ollama are the same box, i.e. when they share VRAM and the GPU
    /// gate has to serialize a local call against an in-flight render. A null/blank ComfyUI URL
    /// falls back to its own default inside <see cref="GpuColocation"/>.</summary>
    public bool SharesGpuWithComfyUi =>
        GpuColocation.SameHost(settings?.ComfyUiBaseUrl, OllamaBaseUrl);
}
