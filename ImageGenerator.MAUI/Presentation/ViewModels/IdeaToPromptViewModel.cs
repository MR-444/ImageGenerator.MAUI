using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Drives the "Describe an idea…" front door as a two-pass flow. Pass 1 (always) turns the idea into a
/// normal <em>prose</em> prompt via <see cref="IPromptBuilderService.BuildProseAsync"/> — usable on its
/// own by every plain-prompt model and copyable for reuse. Pass 2 (optional, gated by the
/// <see cref="BuildJson"/> checkbox) maps that prose onto a schema-valid <see cref="V4JsonPrompt"/>. The
/// prose is never discarded: results stay on the page so the user can copy them, apply the prose as the
/// prompt, or apply the JSON. Handoff to the generator mirrors the structure editor
/// (<c>Parameters.Prompt</c> + <c>UseJsonPrompt</c>). Singleton so an in-progress idea + results survive
/// a navigation round-trip; the optional <see cref="GeneratorViewModel"/> lets tests build it stand-alone.
/// </summary>
public partial class IdeaToPromptViewModel : ObservableObject
{
    private readonly IPromptBuilderService _builder;
    private readonly IClipboardService _clipboard;
    private readonly GeneratorViewModel? _generator;
    private readonly IOllamaModelCatalog? _ollamaCatalog;
    private readonly IGpuGate? _gpuGate;
    private readonly ILogger<IdeaToPromptViewModel> _logger;

    private V4JsonPrompt? _builtJson;

    // Owns the in-flight build so the user can cancel a slow (paid) Opus call. Recreated per build,
    // disposed in BuildAsync's finally. The VM is a Singleton, so without this an abandoned build would
    // keep running — and keep billing — after the user navigates away.
    private CancellationTokenSource? _cts;

    public IdeaToPromptViewModel(
        IPromptBuilderService builder,
        IClipboardService clipboard,
        ILogger<IdeaToPromptViewModel> logger,
        GeneratorViewModel? generator = null,
        IOllamaModelCatalog? ollamaCatalog = null,
        IGpuGate? gpuGate = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;
        _ollamaCatalog = ollamaCatalog;
        _gpuGate = gpuGate;

        // Default the JSON pass on for structured-JSON models (Ideogram V4 / ComfyUI), off otherwise.
        BuildJson = generator?.SupportsJsonPromptEditor ?? false;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _idea = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseJsonCommand))]
    private bool _isBusy;

    /// <summary>The checkbox: also run pass 2 (prose → Ideogram V4 JSON). Defaults from the model.</summary>
    [ObservableProperty]
    private bool _buildJson;

    public IReadOnlyList<ModelTier> ModelTierOptions { get; } = [ModelTier.Opus, ModelTier.Sonnet, ModelTier.Local];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalTier))]
    [NotifyPropertyChangedFor(nameof(ModelSummary))]
    private ModelTier _selectedModelTier = ModelTier.Opus;

    public bool IsLocalTier => SelectedModelTier == ModelTier.Local;

    public string ModelSummary => SelectedModelTier switch
    {
        ModelTier.Sonnet => "Claude Sonnet: paid Anthropic call, faster and cheaper than Opus.",
        ModelTier.Local => "Local Ollama: free, uses the configured Ollama server/model from Settings.",
        _ => "Claude Opus: paid Anthropic call, strongest prompt-builder default."
    };

    /// <summary>Expose the host generator so the page can reuse the shared Ollama model picker.</summary>
    public GeneratorViewModel? Generator => _generator;

    /// <summary>The pass-1 prose prompt. Shown on the page, copyable, and applyable on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProse))]
    [NotifyCanExecuteChangedFor(nameof(CopyProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseProseCommand))]
    private string _prose = string.Empty;

    /// <summary>True once pass 2 has produced a JSON prompt — gates the "Use JSON prompt" button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseJsonCommand))]
    private bool _hasJson;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    /// <summary>Drives the result card's visibility — prose exists once a build has succeeded.</summary>
    public bool HasProse => !string.IsNullOrEmpty(Prose);

    private bool CanBuild() => !IsBusy && !string.IsNullOrWhiteSpace(Idea);

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        // Fresh token source per build; the previous one (if any) is disposed in this method's finally.
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        IsBusy = true;
        // A fresh build invalidates any JSON from a previous run; keep the prose until it's replaced.
        HasJson = false;
        _builtJson = null;
        var tier = SelectedModelTier;
        var gpuGated = tier == ModelTier.Local
            && _gpuGate is not null
            && GpuColocation.SameHost(_generator?.ComfyUiBaseUrl, ResolveOllamaBaseUrl());
        IDisposable? gpuLease = null;
        try
        {
            if (gpuGated)
            {
                SetStatus("Waiting for the current render to finish…", StatusKind.Info);
                gpuLease = await _gpuGate!.AcquireAsync();
                if (_generator is not null) _generator.IsGpuBusy = true;
            }

            SetStatus($"Asking {ModelDisplay(tier)} to write a prompt…", StatusKind.Info);

            // Pass 1 (always): idea → prose.
            var proseResult = await _builder.BuildProseAsync(Idea, token, tier);
            if (!proseResult.Success || proseResult.Prose is null)
            {
                SetStatus(proseResult.Error ?? "Couldn't build a prompt.", StatusKind.Error);
                return;
            }

            Prose = proseResult.Prose;

            if (!BuildJson)
            {
                SetStatus("Prose prompt ready — copy it or use it as your prompt.", StatusKind.Success);
                return;
            }

            // Pass 2 (optional): prose → Ideogram V4 JSON. On failure the prose above stays usable.
            SetStatus("Prose ready — building the Ideogram V4 JSON prompt…", StatusKind.Info);
            var jsonResult = await _builder.BuildJsonAsync(Prose, token, tier);
            if (!jsonResult.Success || jsonResult.Prompt is null)
            {
                SetStatus(
                    (jsonResult.Error ?? "Couldn't build the JSON prompt.") + " The prose prompt above is still usable.",
                    StatusKind.Error);
                return;
            }

            _builtJson = jsonResult.Prompt;
            HasJson = true;
            SetStatus("Prose and Ideogram V4 JSON ready. Use either below.", StatusKind.Success);
        }
        catch (OperationCanceledException)
        {
            // User-initiated cancel — not an error. Any prose built in pass 1 stays on the page for reuse.
            SetStatus("Build cancelled.", StatusKind.Info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IdeaToPrompt.{Op}", "Build");
            SetStatus($"Couldn't build the prompt: {ex.Message}", StatusKind.Error);
        }
        finally
        {
            if (tier == ModelTier.Local && _ollamaCatalog is not null)
                await _ollamaCatalog.UnloadAsync(ResolveOllamaBaseUrl(), ResolveOllamaModel());

            gpuLease?.Dispose();
            if (gpuGated && _generator is not null) _generator.IsGpuBusy = false;

            IsBusy = false;
            _cts = null;
            cts.Dispose();
        }
    }

    private bool CanCancel() => IsBusy;

    /// <summary>Aborts the in-flight build. The token flows through to the underlying Anthropic call,
    /// so a slow paid request is actually stopped (not just hidden behind a disabled button).</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The build already finished and disposed its source between the CanExecute check and here.
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseProse))]
    private async Task CopyProseAsync()
    {
        try
        {
            await _clipboard.SetTextAsync(Prose);
            SetStatus("Prose prompt copied to the clipboard.", StatusKind.Info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: copy prose failed");
            SetStatus("Couldn't copy to the clipboard.", StatusKind.Error);
        }
    }

    private bool CanUseProse() => !IsBusy && HasProse;

    [RelayCommand(CanExecute = nameof(CanUseProse))]
    private async Task UseProseAsync()
    {
        if (_generator is null)
        {
            // Stand-alone (tests): nothing to hand off to.
            SetStatus("Prose prompt ready.", StatusKind.Success);
            return;
        }

        _generator.Parameters.Prompt = Prose;
        _generator.Parameters.UseJsonPrompt = false;
        await NavigateBackAsync();
    }

    private bool CanUseJson() => !IsBusy && HasJson;

    [RelayCommand(CanExecute = nameof(CanUseJson))]
    private async Task UseJsonAsync()
    {
        if (_builtJson is null) return;

        if (_generator is null)
        {
            SetStatus("JSON prompt ready.", StatusKind.Success);
            return;
        }

        ApplyToGenerator(_builtJson);
        await NavigateBackAsync();
    }

    /// <summary>The established handoff: serialize compact into the prompt box and flip on JSON mode —
    /// identical to <c>IdeogramStructureEditorViewModel.ApplyToGenerator</c>. Internal so tests assert it.</summary>
    internal void ApplyToGenerator(V4JsonPrompt prompt)
    {
        if (_generator is null) return;
        _generator.Parameters.Prompt = V4JsonPromptSerializer.Serialize(prompt);
        _generator.Parameters.UseJsonPrompt = true;
    }

    // Navigation is best-effort and isolated so a Shell failure can't undo the handoff above.
    private async Task NavigateBackAsync()
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: navigation back failed");
        }
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    private static string ModelDisplay(ModelTier tier) => tier switch
    {
        ModelTier.Sonnet => "Claude Sonnet",
        ModelTier.Local => "local Ollama",
        _ => "Claude Opus"
    };

    private string ResolveOllamaBaseUrl() =>
        _generator?.OllamaBaseUrl is { Length: > 0 } u ? u : ModelConstants.Ollama.DefaultBaseUrl;

    private string ResolveOllamaModel() =>
        _generator?.OllamaModel is { Length: > 0 } m ? m : ModelConstants.Ollama.DefaultModel;
}
