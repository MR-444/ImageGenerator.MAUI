using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
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
    private readonly GeneratorSettingsReader _settings;
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
        _settings = new GeneratorSettingsReader(generator?.Settings);
        _ollamaCatalog = ollamaCatalog;
        _gpuGate = gpuGate;
        _visionObserver = visionObserver;
        _referenceImageDownloader = referenceImageDownloader;
        _uiStateStore = uiStateStore;

        // The BuildJson checkbox: the user's stored choice wins; only a first-ever launch defaults
        // it from the active model (on for structured-JSON models, off otherwise). It must not be
        // re-defaulted per page visit — that silently undid the user's uncheck every time.
        BuildJson = _uiStateStore?.LoadIdeaBuildJson() ?? generator?.SupportsJsonPromptEditor ?? false;

        // The observer's provider and model, remembered per role; the model on screen is the model called.
        if (uiStateStore is not null && generator?.Settings is { } settings)
        {
            Vision = new ModelChoiceState(
                ModelRole.Observe, uiStateStore, settings.VisionModelSources, ModelProvider.Local,
                VisionHelperFor,
                baseUrlFor: p => p == ModelProvider.Local ? _settings.OllamaBaseUrl : string.Empty);
            Vision.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ModelChoiceState.SelectedProvider))
                    OnPropertyChanged(nameof(IsOpenRouterVisionProvider));
            };

            // The writer's decision, same shape. It opens on Local because that provider cannot bill anyone.
            Writer = new ModelChoiceState(
                ModelRole.PromptWrite, uiStateStore, settings.TextModelSources, ModelProvider.Local,
                WriterHelperFor,
                baseUrlFor: p => p == ModelProvider.Local ? _settings.OllamaBaseUrl : string.Empty);
            // Whether Build may run depends on this object having a model, and that changes while the page
            // sits open: a refresh filling the list, a switch of provider, a pick.
            Writer.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ModelChoiceState.SelectedModel)
                    or nameof(ModelChoiceState.SelectedProvider))
                    BuildCommand.NotifyCanExecuteChanged();
            };
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    private string _idea = string.Empty;

    public IReadOnlyList<IdeaSourceMode> SourceModeOptions { get; } = (IdeaSourceMode[])[IdeaSourceMode.Text, IdeaSourceMode.Image];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextSource))]
    [NotifyPropertyChangedFor(nameof(IsImageSource))]
    [NotifyPropertyChangedFor(nameof(ShowObservation))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReObserveCommand))]
    private IdeaSourceMode _sourceMode = IdeaSourceMode.Text;

    public bool IsTextSource => SourceMode == IdeaSourceMode.Text;
    public bool IsImageSource => SourceMode == IdeaSourceMode.Image;

    // Switching the source clears the idea box: in image mode the text rides along as "notes to
    // honor", so a text-mode idea left behind silently steers every image build back to the old
    // prompt (and a stale note would do the same to the next text build).
    partial void OnSourceModeChanged(IdeaSourceMode value) => Idea = string.Empty;

    partial void OnBuildJsonChanged(bool value) => _uiStateStore?.PersistIdeaBuildJson(value);

    /// <summary>
    /// Who observes the reference image, with which model, remembered per role.
    /// Null only when the page has no settings to read, which the app never does.
    /// </summary>
    public ModelChoiceState? Vision { get; }

    /// <summary>
    /// Who writes the prompt, with which model, remembered per role. Three providers, so this card answers
    /// the same question the card above it answers; a tier could not, having two names for one provider and
    /// no name at all for OpenRouter.
    /// </summary>
    public ModelChoiceState? Writer { get; }

    /// <summary>
    /// Gates the OpenRouter-only controls beside the row: the free-only switch and its explanation. The
    /// row itself needs no gate — it shows whichever provider is selected.
    /// </summary>
    public bool IsOpenRouterVisionProvider => Vision?.SelectedProvider == ModelProvider.OpenRouter;

    private static string VisionHelperFor(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenRouter =>
            "Set the OpenRouter API key in Settings. The image is sent to the selected remote provider.",
        _ => "Only installed Ollama models advertising vision + completion appear after refresh."
    };

    /// <summary>
    /// One sentence per provider, and no figures. What this replaces compared Sonnet and Opus by price
    /// ("faster and cheaper") from a constant nobody had read off an API, and said nothing about the key
    /// the call needs. What is known: who gets billed, where the key lives, and whether the idea leaves
    /// this machine.
    /// </summary>
    private static string? WriterHelperFor(ModelProvider provider) => provider switch
    {
        ModelProvider.Local =>
            "Free — runs on the Ollama server configured in Settings. Nothing you type leaves this machine.",
        ModelProvider.Anthropic =>
            "Set the Anthropic API key in Settings. The idea is sent to Anthropic and billed per token by the model you pick.",
        ModelProvider.OpenRouter =>
            "Set the OpenRouter API key in Settings. The idea is routed by OpenRouter; which models are offered comes from your key.",
        _ => null
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferenceImage))]
    [NotifyPropertyChangedFor(nameof(ShowObservation))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReObserveCommand))]
    private string _referenceImageBase64 = string.Empty;

    [ObservableProperty]
    private ImageSource? _referenceImagePreview;

    [ObservableProperty]
    private string _referenceImageFileName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasObservation))]
    private string _observedImageDescription = string.Empty;

    /// <summary>
    /// True once the user has hand-edited the observation. An edited caption is never replaced
    /// automatically — the edit IS the user's intent — so only "Observe again" discards it.
    /// </summary>
    [ObservableProperty]
    private bool _observationEdited;

    // Set while the VM writes the observation itself, so the Editor's two-way push-back isn't
    // mistaken for a user edit. Same window idiom as GeneratorViewModel's _suppress* flags.
    private bool _writingObservation;

    partial void OnObservedImageDescriptionChanged(string value)
    {
        if (!_writingObservation) ObservationEdited = true;
    }

    // Identifies the inputs that produced the current observation. The idea box is deliberately NOT
    // part of it: user notes never reach the vision model (BuildIdeaFromObservation merges them in
    // afterwards, for the prose pass), so re-observing because the notes changed would buy nothing
    // and cost a second vision call. The image is hashed rather than compared by reference —
    // ImagePayloadNormalizer hands back the SAME array for a payload that was already valid, so
    // identity is not a usable change signal.
    private string? _observationFingerprint;

    private string CurrentObservationFingerprint() =>
        $"{Vision?.SelectedProvider}|{Vision?.SelectedModel}|" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ReferenceImageBase64)));

    // Writes the observation without tripping the hand-edit detector, and re-keys it in one step.
    private void SetObservation(string value, string? fingerprint)
    {
        _writingObservation = true;
        try { ObservedImageDescription = value; }
        finally { _writingObservation = false; }
        _observationFingerprint = fingerprint;
        ObservationEdited = false;
    }

    public bool HasReferenceImage => !string.IsNullOrWhiteSpace(ReferenceImageBase64);

    public bool HasObservation => !string.IsNullOrWhiteSpace(ObservedImageDescription);

    // Keyed off the IMAGE, not off having an observation: the card carries the "Observe again"
    // button and an editable box, both of which must stay reachable after the text is cleared —
    // and the user can write their own observation before ever running a vision pass.
    public bool ShowObservation => IsImageSource && HasReferenceImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CopyProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseProseCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseJsonCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReObserveCommand))]
    private bool _isBusy;

    /// <summary>The checkbox: also run pass 2 (prose → Ideogram V4 JSON). Defaults from the model.</summary>
    [ObservableProperty]
    private bool _buildJson;

    /// <summary>Expose the host generator so the page can reuse shared settings surfaces.</summary>
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

    /// <summary>A build needs something to build from and a model to send it to. What it does not need is a
    /// second confirmation that the user meant to spend nothing: the row opens on the provider that cannot
    /// bill anyone, so refusing to run until something is clicked teaches the user to click whatever makes
    /// the message go away.</summary>
    private bool CanBuild() =>
        !IsBusy
        && Writer is { Target.HasModel: true }
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
        if (await SetReferenceImageAsync(result.FileName, memoryStream.ToArray(), result.FullPath))
        {
            SetStatus($"Reference image ready: {ReferenceImageFileName}", StatusKind.Info);
        }
    }

    public async Task SetReferenceImageFromPathAsync(string filePath)
    {
        if (IsBusy) return;

        if (!File.Exists(filePath))
        {
            SetStatus("File not found.", StatusKind.Error);
            return;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(filePath);
            if (await SetReferenceImageAsync(Path.GetFileName(filePath), imageBytes, filePath))
            {
                SetStatus($"Reference image ready: {ReferenceImageFileName}", StatusKind.Info);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error using reference image: {ex.Message}", StatusKind.Error);
        }
    }

    public async Task<bool> SetReferenceImageFromBytesAsync(string fileName, byte[] bytes, string? sourcePath = null)
    {
        if (IsBusy) return false;

        if (bytes.Length == 0)
        {
            SetStatus("The dropped image was empty.", StatusKind.Error);
            return false;
        }

        if (!await SetReferenceImageAsync(fileName, bytes, sourcePath)) return false;
        SetStatus($"Reference image ready: {ReferenceImageFileName}", StatusKind.Info);
        return true;
    }

    public async Task<bool> SetReferenceImageFromUrlAsync(string url)
    {
        if (IsBusy) return false;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
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

            if (!await SetReferenceImageAsync(result.FileName ?? "browser-reference", result.Bytes, uri.ToString()))
            {
                return false;
            }
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
        if (IsBusy) return;

        ReferenceImageBase64 = string.Empty;
        ReferenceImagePreview = null;
        ReferenceImageFileName = string.Empty;
        // Only the observation goes: it describes an image that is no longer here. Any prose built
        // earlier stays on the page so it can still be copied or applied.
        SetObservation(string.Empty, fingerprint: null);
        SetStatus("Reference image cleared.", StatusKind.Info);
    }

    private async Task<bool> SetReferenceImageAsync(string fileName, byte[] bytes, string? sourcePath)
    {
        // Normalize off the UI thread BEFORE accepting: Ollama only decodes JPEG/PNG, and a browser
        // WebP is often saved with a ".png"/".jfif" name. Decodable formats are transcoded to PNG;
        // the rest are rejected here with a message the user can act on. The vision budget also caps
        // the long edge at 1024px, because a tiled vision model bills the image in tokens.
        var (normalized, _, error) = await ImagePayloadNormalizer.PrepareAsync(bytes, ImagePayloadLimits.Vision);
        if (normalized is null)
        {
            SetStatus(error!, StatusKind.Error);
            return false;
        }

        ReferenceImageBase64 = Convert.ToBase64String(normalized);
        ReferenceImageFileName = string.IsNullOrWhiteSpace(fileName)
            ? sourcePath is { Length: > 0 } path ? Path.GetFileName(path) : "reference image"
            : fileName;
        ResetAllResults();
        ReferenceImagePreview = PreviewFor(normalized);
        return true;
    }

    private ImageSource? PreviewFor(byte[] bytes)
    {
        try
        {
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IdeaToPrompt: reference image preview failed");
            return null;
        }
    }

    // A new (or cleared) reference image invalidates everything, the observation included.
    private void ResetAllResults()
    {
        ResetPromptResults();
        SetObservation(string.Empty, fingerprint: null);
    }

    // A rebuild clears only what that build regenerates. The observation deliberately survives: it
    // is re-validated against _observationFingerprint instead of being blanked, so a second build
    // on the same image reuses the caption rather than paying for it twice.
    private void ResetPromptResults()
    {
        Prose = string.Empty;
        HasJson = false;
        _builtJson = null;
    }

    private bool CanReObserve() => !IsBusy && IsImageSource && HasReferenceImage;

    /// <summary>
    /// Discards the current observation so the next build captions the image again — the escape hatch
    /// for a caption the user wants regenerated, or wants back after editing it. Marking it stale
    /// rather than observing inline keeps the GPU gate and model unload in the one place that owns
    /// them (<see cref="BuildAsync"/>).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReObserve))]
    private void ReObserve()
    {
        SetObservation(string.Empty, fingerprint: null);
        SetStatus("The image will be observed again on the next build.", StatusKind.Info);
    }

    [RelayCommand(CanExecute = nameof(CanBuild))]
    private async Task BuildAsync()
    {
        if (Writer is not { } writer)
        {
            SetStatus("Model settings are unavailable — leave this screen and open it again.", StatusKind.Error);
            return;
        }

        // Resolved once, before anything is asked of it: the row can move while a build runs, and a call
        // already in flight must not change who it is billed to or which model gets freed afterwards. The
        // service words the "nothing picked" sentence per provider, so this page does not guess at one.
        var writerTarget = writer.Target;
        if (!writerTarget.HasModel)
        {
            SetStatus(writerTarget.MissingModelError("in the prompt writer row below"), StatusKind.Error);
            return;
        }

        // Fresh token source per build; the previous one (if any) is disposed in this method's finally.
        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        IsBusy = true;
        // A fresh build drops the previous run's prose and JSON so stale results never linger
        // (including when this build errors or is cancelled before producing new output). The
        // observation is NOT dropped — see the reuse check below.
        ResetPromptResults();

        var usesLocalVision = IsImageSource && Vision?.SelectedProvider == ModelProvider.Local;
        var gpuGated = (writerTarget.Provider == ModelProvider.Local || usesLocalVision)
            && _gpuGate is not null
            && _settings.SharesGpuWithComfyUi;
        // Resolved once, before anything is asked of it: the picker may move while a build runs, and a
        // call already in flight must not change who it is billed to.
        var visionTarget = Vision?.Target;
        IDisposable? gpuLease = null;
        try
        {
            if (gpuGated)
            {
                SetStatus("Waiting for the current render to finish…", StatusKind.Info);
                gpuLease = await _gpuGate!.AcquireAsync(token);
            }

            var ideaForPrompt = Idea.Trim();
            if (IsImageSource)
            {
                if (_visionObserver is null)
                {
                    SetStatus("Image observation is not available in this build.", StatusKind.Error);
                    return;
                }

                if (Vision is null || visionTarget is not { } target)
                {
                    SetStatus("Model settings are unavailable — leave this screen and open it again.", StatusKind.Error);
                    return;
                }

                if (!target.HasModel)
                {
                    SetStatus(target.MissingModelError("in the vision model row above"), StatusKind.Error);
                    return;
                }

                // Observe only when there is nothing usable to reuse. The caption depends on the
                // image and the vision model, never on the idea box, so a second build with changed
                // notes must not pay for a second vision call — the notes are applied below, by
                // BuildIdeaFromObservation. A hand-edited caption is always kept; "Observe again"
                // is how the user discards it.
                var fingerprint = CurrentObservationFingerprint();
                if (!HasObservation || (_observationFingerprint != fingerprint && !ObservationEdited))
                {
                    SetStatus($"Asking {target.ProviderLabel} to observe the reference image…", StatusKind.Info);
                    var observationResult = await _visionObserver.ObserveAsync(
                        new VisionObservationRequest(
                            target,
                            ReferenceImageBase64,
                            ReferenceImageFileName),
                        token);

                    if (!observationResult.Success || observationResult.Observation is null)
                    {
                        SetStatus(observationResult.Error ?? "Couldn't observe the reference image.", StatusKind.Error);
                        return;
                    }

                    SetObservation(observationResult.Observation, fingerprint);
                }
                else
                {
                    SetStatus(
                        ObservationEdited
                            ? "Using your edited image observation…"
                            : "Reusing the existing image observation…",
                        StatusKind.Info);
                }

                ideaForPrompt = BuildIdeaFromObservation(ObservedImageDescription, Idea);
            }

            SetStatus($"Asking {writerTarget.ModelId} on {writerTarget.ProviderLabel} to write a prompt…", StatusKind.Info);

            // Pass 1 (always): idea → prose.
            var proseResult = await _builder.BuildProseAsync(ideaForPrompt, writerTarget, token);
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
            var jsonResult = await _builder.BuildJsonAsync(Prose, writerTarget, token);
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
            try
            {
                if (_ollamaCatalog is not null)
                {
                    // Pass CancellationToken.None so a user cancel still unloads local models from VRAM —
                    // and free the model that was called, not the shared Ollama key's model.
                    if (writerTarget.Provider == ModelProvider.Local)
                        await _ollamaCatalog.UnloadAsync(writerTarget.BaseUrl, writerTarget.ModelId, CancellationToken.None);
                    if (usesLocalVision)
                        await _ollamaCatalog.UnloadAsync(visionTarget!.Value.BaseUrl, visionTarget!.Value.ModelId, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IdeaToPrompt: Ollama model unload failed");
            }
            finally
            {
                gpuLease?.Dispose();

                IsBusy = false;
                _cts = null;
                cts.Dispose();
            }
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
    /// identical to <c>IdeogramStructureEditorViewModel.ApplyToGenerator</c>.</summary>
    private void ApplyToGenerator(V4JsonPrompt prompt)
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