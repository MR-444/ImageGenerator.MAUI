using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The single place a companion page (IdeaToPrompt, MutationEngine, the Ideogram structure editor)
/// resolves the host generator's settings into the two facts it cannot derive for itself: which Ollama
/// endpoint the Local provider answers on, and whether that endpoint shares a GPU with ComfyUI.
///
/// Those pages bind their pickers straight through <c>Generator.…</c>, but every one of them also had to
/// re-derive the same fallbacks in C#, and the fallbacks are the part that must not drift per page: a
/// blank URL is a blank URL everywhere, and a blank URL means "use the default server" on every page or
/// on none of them.
///
/// It carries no model picks: every model a companion page can call is picked in a
/// <see cref="ModelChoiceState"/>, which resolves its own target and persists it per role.
///
/// Holding still during the owner's lifetime is fine: these VMs are transient (fresh per navigation) and
/// the generator is a singleton. It takes the settings coordinator rather than the generator, so a
/// companion page cannot reach any generator member from here.
/// </summary>
internal sealed class GeneratorSettingsReader(AppSettingsCoordinator? settings)
{
    /// <summary>The Ollama endpoint the Local provider talks to: the generator's setting, else the
    /// default (fireEngine). Shared by the model rows' base URLs and the GPU-gate same-host check.</summary>
    public string OllamaBaseUrl =>
        settings?.OllamaBaseUrl is { Length: > 0 } u ? u : ModelConstants.Ollama.DefaultBaseUrl;

    /// <summary>True when ComfyUI and Ollama are the same box, i.e. when they share VRAM and the GPU
    /// gate has to serialize a local call against an in-flight render. A null/blank ComfyUI URL
    /// falls back to its own default inside <see cref="GpuColocation"/>.</summary>
    public bool SharesGpuWithComfyUi =>
        GpuColocation.SameHost(settings?.ComfyUiBaseUrl, OllamaBaseUrl);
}
