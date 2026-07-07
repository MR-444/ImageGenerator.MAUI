using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public enum IdeaSourceMode
{
    Text,
    Image
}

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
public partial class IdeaToPromptViewModel : ObservableObject, IStatusOwner
{
    private readonly IPromptBuilderService _builder;
    private readonly IClipboardService _clipboard;
    private readonly GeneratorViewModel? _generator;
    private readonly IOllamaModelCatalog? _ollamaCatalog;
    private readonly IGpuGate? _gpuGate;
    private readonly IVisionObservationService? _visionObserver;
    private readonly IReferenceImageDownloadService? _referenceImageDownloader;
    private readonly IUiStateStore? _uiStateStore;
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
        IGpuGate? gpuGate = null,
        IVisionObservationService? visionObserver = null,
        IReferenceImageDownloadService? referenceImageDownloader = null,
        IUiStateStore? uiStateStore = null)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;
        _ollamaCatalog = ollamaCatalog;
        _gpuGate = gpuGate;
        _visionObserver = visionObserver;
        _referenceImageDownloader = referenceImageDownloader;
        _uiStateStore = uiStateStore;

        // Default the JSON pass on for structured-JSON models (Ideogram V4 / ComfyUI), off otherwise.
        BuildJson = generator?.SupportsJsonPromptEditor ?? false;

        // Restore the last-used prompt writer so the picker survives an app restart (null = never
        // picked → the "Pick a prompt writer…" placeholder stays). The assignment echoes back through
        // OnSelectedModelTierChanged as a harmless skip-identical persist.
        if (_uiStateStore?.LoadPromptWriterTier() is { } savedTier)
            SelectedModelTier = savedTier;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _idea = string.Empty;

    public IReadOnlyList<IdeaSourceMode> SourceModeOptions { get; } = [IdeaSourceMode.Text, IdeaSourceMode.Image];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextSource))]
    [NotifyPropertyChangedFor(nameof(IsImageSource))]
    [NotifyPropertyChangedFor(nameof(ShowObservation))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private IdeaSourceMode _sourceMode = IdeaSourceMode.Text;

    public bool IsTextSource => SourceMode == IdeaSourceMode.Text;
    public bool IsImageSource => SourceMode == IdeaSourceMode.Image;

    public IReadOnlyList<VisionObservationProvider> VisionProviderOptions { get; } =
        [VisionObservationProvider.LocalOllama, VisionObservationProvider.OpenRouter];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalVisionProvider))]
    [NotifyPropertyChangedFor(nameof(IsOpenRouterVisionProvider))]
    private VisionObservationProvider _selectedVisionProvider = VisionObservationProvider.LocalOllama;

    public bool IsLocalVisionProvider => SelectedVisionProvider == VisionObservationProvider.LocalOllama;
    public bool IsOpenRouterVisionProvider => SelectedVisionProvider == VisionObservationProvider.OpenRouter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferenceImage))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _referenceImageBase64 = string.Empty;

    [ObservableProperty]
    private ImageSource? _referenceImagePreview;

    [ObservableProperty]
    private string _referenceImageFileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasObservation))]
    [NotifyPropertyChangedFor(nameof(ShowObservation))]
    private string _observedImageDescription = string.Empty;

    public bool HasReferenceImage => !string.IsNullOrWhiteSpace(ReferenceImageBase64);

    public bool HasObservation => !string.IsNullOrWhiteSpace(ObservedImageDescription);

    public bool ShowObservation => IsImageSource && HasObservation;

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

    public IReadOnlyList<ModelTier> ModelTierOptions { get; } = [ModelTier.Local, ModelTier.Sonnet, ModelTier.Opus];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocalTier))]
    [NotifyPropertyChangedFor(nameof(ModelSummary))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private ModelTier? _selectedModelTier;

    public bool IsLocalTier => SelectedModelTier == ModelTier.Local;

    public string ModelSummary => SelectedModelTier switch
    {
        ModelTier.Sonnet => "Claude Sonnet: paid Anthropic call, faster and cheaper than Opus.",
        ModelTier.Local => "Local Ollama: free, uses the configured Ollama server/model from Settings.",
        null => "Pick a prompt writer before building. Local is free; Claude tiers are paid.",
        _ => "Claude Opus: paid Anthropic call, strongest prompt-builder default."
    };

    // Persist the picked tier so it survives an app restart, mirroring GeneratorViewModel's vision-model
    // hook. Only persist a real selection — a Picker can't return to null, so we never overwrite a saved
    // tier with the placeholder.
    partial void OnSelectedModelTierChanged(ModelTier? value)
    {
        if (value is { } tier) _uiStateStore?.PersistPromptWriterTier(tier);
    }

    /// <summary>Expose the host generator so the page can reuse the shared Ollama model picker.</summary>
    public GeneratorViewModel? Generator => _generator;

    public void PrepareForNavigation()
    {
        if (IsBusy) return;
        BuildJson = _generator?.SupportsJsonPromptEditor ?? false;
    }

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

    private bool CanBuild() =>
        !IsBusy
        && SelectedModelTier is not null
        && (IsTextSource
            ? !string.IsNullOrWhiteSpace(Idea)
            : HasReferenceImage);

    [RelayCommand]
    private async Task PickReferenceImageAsync()
    {
        if (IsBusy) return;

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Pick a reference image",
            FileTypes = FilePickerFileType.Images
        });

        if (result is null)
            return;

        await using var stream = await result.OpenReadAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        SetReferenceImage(result.FileName, memoryStream.ToArray(), result.FullPath, createPreview: true);
        SetStatus($"Reference image ready: {result.FileName}", StatusKind.Info);
    }

    public async Task SetReferenceImageFromPathAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            SetStatus("File not found.", StatusKind.Error);
            return;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(filePath);
            SetReferenceImage(Path.GetFileName(filePath), imageBytes, filePath, createPreview: true);
            SetStatus($"Reference image ready: {Path.GetFileName(filePath)}", StatusKind.Info);
        }
        catch (Exception ex)
        {
            SetStatus($"Error using reference image: {ex.Message}", StatusKind.Error);
        }
    }

    public bool SetReferenceImageFromBytes(string fileName, byte[] bytes, string? sourcePath = null)
    {
        if (bytes.Length == 0)
        {
            SetStatus("The dropped image was empty.", StatusKind.Error);
            return false;
        }

        SetReferenceImage(fileName, bytes, sourcePath, createPreview: true);
        SetStatus($"Reference image ready: {ReferenceImageFileName}", StatusKind.Info);
        return true;
    }

    public async Task<bool> SetReferenceImageFromUrlAsync(string url)
    {
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SetStatus("Only http and https image URLs can be imported.", StatusKind.Error);
            return false;
        }

        if (_referenceImageDownloader is null)
        {
            SetStatus("Browser image import is not available in this build.", StatusKind.Error);
            return false;
        }

        try
        {
            var result = await _referenceImageDownloader.DownloadAsync(
                uri,
                maxBytes: 20L * 1024 * 1024);
            if (!result.Success || result.Bytes is null)
            {
                SetStatus(result.Error ?? "Couldn't import that image URL.", StatusKind.Error);
                return false;
            }

            SetReferenceImage(result.FileName ?? "browser-reference", result.Bytes, uri.ToString(), createPreview: true);
            SetStatus($"Reference image ready: {ReferenceImageFileName}", StatusKind.Info);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: browser reference image import failed Url={Url}", uri.GetLeftPart(UriPartial.Path));
            SetStatus($"Error importing reference image: {ex.Message}", StatusKind.Error);
            return false;
        }
    }

    [RelayCommand]
    private void ClearReferenceImage()
    {
        ReferenceImageBase64 = string.Empty;
        ReferenceImagePreview = null;
        ReferenceImageFileName = string.Empty;
        ObservedImageDescription = string.Empty;
        SetStatus("Reference image cleared.", StatusKind.Info);
    }

    internal void SetReferenceImageForTest(string fileName, byte[] bytes)
    {
        SetReferenceImage(fileName, bytes, sourcePath: null, createPreview: false);
    }

    private void SetReferenceImage(string fileName, byte[] bytes, string? sourcePath, bool createPreview)
    {
        ReferenceImageBase64 = Convert.ToBase64String(bytes);
        ReferenceImageFileName = string.IsNullOrWhiteSpace(fileName)
            ? sourcePath is { Length: > 0 } path ? Path.GetFileName(path) : "reference image"
            : fileName;
        ObservedImageDescription = string.Empty;

        if (!createPreview)
        {
            ReferenceImagePreview = null;
            return;
        }

        try
        {
            ReferenceImagePreview = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: reference image preview failed");
            ReferenceImagePreview = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (SelectedModelTier is not { } tier)
        {
            SetStatus("Pick a prompt writer first. Local is free; Claude tiers are paid.", StatusKind.Error);
            return;
        }

        // Fresh token source per build; the previous one (if any) is disposed in this method's finally.
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        IsBusy = true;
        // A fresh build starts from a clean slate: drop the previous run's prose, observation,
        // and JSON so stale results never linger (including when this build errors or is cancelled
        // before producing new output). They repopulate below as each pass succeeds.
        Prose = string.Empty;
        ObservedImageDescription = string.Empty;
        HasJson = false;
        _builtJson = null;

        var usesLocalVision = IsImageSource && SelectedVisionProvider == VisionObservationProvider.LocalOllama;
        var gpuGated = (tier == ModelTier.Local || usesLocalVision)
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

            var ideaForPrompt = Idea.Trim();
            if (IsImageSource)
            {
                if (_visionObserver is null)
                {
                    SetStatus("Image observation is not available in this build.", StatusKind.Error);
                    return;
                }

                var visionModel = ResolveVisionModel(SelectedVisionProvider);
                if (SelectedVisionProvider == VisionObservationProvider.OpenRouter
                    && string.IsNullOrWhiteSpace(visionModel))
                {
                    SetStatus("Pick an OpenRouter vision model first. Refresh the list, then choose a model.", StatusKind.Error);
                    return;
                }

                SetStatus($"Asking {VisionProviderDisplay(SelectedVisionProvider)} to observe the reference image…", StatusKind.Info);
                var observationResult = await _visionObserver.ObserveAsync(
                    new VisionObservationRequest(
                        SelectedVisionProvider,
                        ReferenceImageBase64,
                        ReferenceImageFileName,
                        visionModel),
                    token);

                if (!observationResult.Success || observationResult.Observation is null)
                {
                    SetStatus(observationResult.Error ?? "Couldn't observe the reference image.", StatusKind.Error);
                    return;
                }

                ObservedImageDescription = observationResult.Observation;
                ideaForPrompt = BuildIdeaFromObservation(ObservedImageDescription, Idea);
            }

            SetStatus($"Asking {ModelDisplay(tier)} to write a prompt…", StatusKind.Info);

            // Pass 1 (always): idea → prose.
            var proseResult = await _builder.BuildProseAsync(ideaForPrompt, token, tier);
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
            if (_ollamaCatalog is not null)
            {
                if (tier == ModelTier.Local)
                    await _ollamaCatalog.UnloadAsync(ResolveOllamaBaseUrl(), ResolveOllamaModel());
                if (usesLocalVision)
                    await _ollamaCatalog.UnloadAsync(ResolveOllamaBaseUrl(), ResolveOllamaVisionModel());
            }

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

    private string ResolveOllamaVisionModel() =>
        _generator?.OllamaVisionModel is { Length: > 0 } m
            ? m
            : ResolveOllamaModel();

    private string ResolveVisionModel(VisionObservationProvider provider) =>
        provider == VisionObservationProvider.OpenRouter
            ? _generator?.OpenRouterVisionModel is { Length: > 0 } m
                ? m
                : string.Empty
            : ResolveOllamaVisionModel();

    private static string VisionProviderDisplay(VisionObservationProvider provider) => provider switch
    {
        VisionObservationProvider.OpenRouter => "OpenRouter",
        _ => "local Ollama"
    };

    private static string BuildIdeaFromObservation(string observation, string userNotes)
    {
        var trimmedNotes = userNotes.Trim();
        return string.IsNullOrWhiteSpace(trimmedNotes)
            ? "Use this observed reference-image description as the idea source:\n\n" + observation.Trim()
            : "Use this observed reference-image description as the idea source:\n\n"
              + observation.Trim()
              + "\n\nUser notes to honor without inventing unrelated changes:\n"
              + trimmedNotes;
    }
}
