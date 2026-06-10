using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IJobRunner _jobRunner;
    private readonly IApiTokenStore _tokenStore;
    private readonly IPollinationsTokenStore _pollinationsTokenStore;
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly ILogger<GeneratorViewModel> _logger;

    [ObservableProperty]
    private ImageGenerationParameters _parameters;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    // Transient confirmation toast (e.g. "Image generated"). Distinct from StatusMessage,
    // which holds form-level errors / info that shouldn't auto-clear.
    [ObservableProperty]
    private string? _flashMessage;

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;

    public ProviderFilterCoordinator ProviderFilter { get; }
    public BatchCoordinator Batch { get; }

    [ObservableProperty]
    private List<string> _aspectRatioOptions = [];  // hydrated from registry in ctor

    [ObservableProperty]
    private List<string> _resolutionOptions = [];

    [ObservableProperty]
    private List<string> _gptQualityOptions = [];

    [ObservableProperty]
    private List<string> _gptBackgroundOptions = [];

    [ObservableProperty]
    private List<string> _gptModerationOptions = [];

    [ObservableProperty]
    private List<string> _gptInputFidelityOptions = [];

    [ObservableProperty]
    private List<string> _outputFormats = [nameof(ImageOutputFormat.Png).ToLowerInvariant(), nameof(ImageOutputFormat.Jpg).ToLowerInvariant(), nameof(ImageOutputFormat.Webp).ToLowerInvariant()];

    // Each provider's token Entry is rendered from this collection — adding a third / fourth
    // provider is a single extra item built in the ctor below, no XAML changes.
    public ObservableCollection<TokenProviderViewModel> TokenProviders { get; } = [];

    [ObservableProperty]
    private TokenProviderViewModel? _selectedTokenProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    private bool _isCustomAspectRatio;

    public InputImagesCoordinator InputImages { get; }

    // Derived from <Version> in the csproj via the SDK-generated AssemblyInformationalVersion
    // (which on Windows MAUI is the only version source not polluted by ApplicationVersion's
    // build counter, the way AppInfo.Current.VersionString is). Strip the "+gitSha" suffix.
    public string AppVersion =>
        typeof(GeneratorViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?.Split('+')[0]
        ?? "0.0.0";

    [ObservableProperty]
    private bool _isValid;

    // Live validity readout for structured-JSON mode, shown under the checkbox. Besides the
    // direct feedback while pasting, it makes any view->VM prompt desync immediately visible
    // (the label reflects what the VM actually holds, not what the Editor displays).
    [ObservableProperty]
    private string? _jsonPromptStateText;

    // Non-secret server setting (first of its kind) — Preferences-backed, not a token store.
    // The ComfyUI generation service re-reads the store per request, so edits apply instantly.
    [ObservableProperty]
    private string _comfyUiBaseUrl = ModelConstants.ComfyUi.DefaultBaseUrl;

    partial void OnComfyUiBaseUrlChanged(string value) =>
        _uiStateStore.PersistComfyUiBaseUrl(value ?? string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    [NotifyPropertyChangedFor(nameof(SupportsResolution))]
    [NotifyPropertyChangedFor(nameof(ShowSharedResolution))]
    [NotifyPropertyChangedFor(nameof(SupportsGptQuality))]
    [NotifyPropertyChangedFor(nameof(SupportsIdeogramOptions))]
    private ModelCapabilities _capabilities;  // hydrated from registry in ctor
    private bool _cachedCatalogLoaded;
    private bool _tokensLoaded;
    private bool _uiStateLoaded;

    // Suppresses InputImagesCoordinator.RecordExplicitAspectRatioPick during programmatic AR
    // writes (model fallback in RefreshCapabilities, image-add/remove auto-AR in the
    // coordinator) so only genuine user picks accumulate as the sticky preference.
    private bool _suppressPreferredArUpdate;

    // Persist suppression for Parameters.Model writes now lives on ProviderFilterCoordinator —
    // see ProviderFilter.SuppressModelPersist, set during ApplyCatalog / RestoreSelectedModel.

    // Suppresses persisting UseJsonPrompt during programmatic writes (the RefreshCapabilities
    // auto-clear on non-Ideogram models, the LoadSavedUiState restore) so a temporary model
    // switch can't erase the user's saved toggle preference.
    private bool _suppressJsonPromptPersist;

    // Same idea for Parameters.Resolution: RefreshCapabilities slams it to the new model's
    // first option whenever the current value isn't in the list — a capability fallback, not
    // a user pick, so it must not overwrite the persisted choice.
    private bool _suppressResolutionPersist;

    // Last Resolution value that was a member of the then-current ResolutionOptions. Used to
    // revert the WinUI ComboBox's DEFERRED null push: after an ItemsSource swap the platform
    // picker finishes rebuilding on a later dispatcher tick and pushes SelectedItem=null —
    // outside the suppression window, so flags can't catch it. (The model picker survives the
    // identical push because OnSelectedModelChanged ignores null; this is Resolution's
    // equivalent tolerance.)
    private string _lastValidResolution = string.Empty;

    partial void OnCapabilitiesChanged(ModelCapabilities value) => InputImages.OnCapabilitiesChanged();

    public bool SupportsCustomDimensions => Capabilities.CustomDimensions && IsCustomAspectRatio;
    public bool SupportsResolution => Capabilities.Resolutions is not null;
    public bool SupportsGptQuality => Capabilities.GptQualityOptions is not null;
    public bool SupportsIdeogramOptions => Capabilities.IdeogramOptions;
    // The Ideogram block renders its own resolution picker, so suppress the shared one there.
    public bool ShowSharedResolution => SupportsResolution && !Capabilities.IdeogramOptions;

    partial void OnParametersChanged(ImageGenerationParameters value)
    {
        UpdateCustomAspectRatio(value.AspectRatio);
        ValidateParameters();
        ProviderFilter.SyncSelectionFromParameters(value.Model);
    }

    private void RefreshCapabilities(string? modelValue)
    {
        var caps = _registry.CapabilitiesFor(modelValue ?? string.Empty).Capabilities;

        // Update derived lists first so when the Capabilities setter fires the binding
        // cascade (NotifyPropertyChangedFor + path-based bindings on Capabilities.X),
        // every consumer sees consistent state.
        AspectRatioOptions = caps.AspectRatios.ToList();
        // Sticky AR: prefer the user's last explicit pick if the new model supports it,
        // otherwise keep the current AR if still valid (covers initial-state edge cases),
        // otherwise fall back to the model's first AR.
        var current = Parameters.AspectRatio;
        var target =
            (InputImages.PreferredAspectRatio is { } pref && caps.AspectRatios.Contains(pref)) ? pref :
            caps.AspectRatios.Contains(current) ? current :
            caps.AspectRatios[0];
        if (!string.Equals(target, current, StringComparison.Ordinal))
        {
            SetAspectRatioProgrammatically(target);
        }

        // The options swap must sit INSIDE the persist-suppression window: replacing the
        // ItemsSource makes the two-way-bound Pickers push SelectedItem=null into
        // Parameters.Resolution synchronously whenever the old value isn't in the new list.
        // Outside the window that push looked user-driven and persisted null — deleting the
        // saved resolution before LoadSavedUiState could read it. (The model picker survives
        // the same push only because OnSelectedModelChanged ignores null.)
        _suppressResolutionPersist = true;
        try
        {
            // Capture BEFORE the swap: the Pickers' synchronous null push lands in
            // Parameters.Resolution during the assignment below, so reading it afterwards
            // would always see null and slam a still-valid choice to the first option.
            var previousResolution = Parameters.Resolution;
            ResolutionOptions = caps.Resolutions?.ToList() ?? [];
            if (ResolutionOptions.Count > 0)
            {
                // Sticky resolution, mirroring the AR logic above: keep the current value if
                // the new model offers it, else fall back to the persisted user choice, else
                // the model's first option.
                var savedResolution = _uiStateStore.LoadResolution();
                var targetResolution =
                    ResolutionOptions.Contains(previousResolution) ? previousResolution :
                    savedResolution is not null && ResolutionOptions.Contains(savedResolution) ? savedResolution :
                    ResolutionOptions[0];
                _logger.LogDebug(
                    "RefreshCapabilities({Model}): resolution prev=\"{Prev}\" saved=\"{Saved}\" -> \"{Target}\" ({Count} options)",
                    modelValue, previousResolution, savedResolution, targetResolution, ResolutionOptions.Count);
                Parameters.Resolution = targetResolution;
            }
        }
        finally { _suppressResolutionPersist = false; }

        // Models that hide the output-format picker (Ideogram) only emit PNG — pin the save format.
        if (!caps.OutputFormatSelectable) Parameters.OutputFormat = ImageOutputFormat.Png;
        // Clear the structured-JSON toggle when leaving an Ideogram model so a stale flag can't
        // gate validation (or alter Build) on a model that has no such field. Suppressed from
        // persistence: this is a capability consequence, not a user preference change.
        if (!caps.IdeogramOptions)
        {
            _suppressJsonPromptPersist = true;
            try { Parameters.UseJsonPrompt = false; }
            finally { _suppressJsonPromptPersist = false; }
        }

        GptQualityOptions = caps.GptQualityOptions?.ToList() ?? [];
        GptBackgroundOptions = caps.GptBackgroundOptions?.ToList() ?? [];
        GptModerationOptions = caps.GptModerationOptions?.ToList() ?? [];
        GptInputFidelityOptions = caps.GptInputFidelityOptions?.ToList() ?? [];

        // Truncate attached images to the new model's cap so users don't silently lose excess
        // images at generation time. The coordinator's CollectionChanged handler raises
        // CanAddImage etc. on the coordinator surface.
        InputImages.TruncateToMaxInputs(caps.MaxImageInputs);

        Capabilities = caps;

        // Capabilities now reflect the new model, so the JSON-prompt validation gate is accurate.
        ValidateParameters();
    }

    private void SetAspectRatioProgrammatically(string aspectRatio)
    {
        _suppressPreferredArUpdate = true;
        try { Parameters.AspectRatio = aspectRatio; }
        finally { _suppressPreferredArUpdate = false; }
    }

    private void MirrorImagePromptsToParameters()
    {
        Parameters.ImagePrompts.Clear();
        foreach (var item in InputImages.SelectedImages) Parameters.ImagePrompts.Add(item.Base64);
    }

    private void UpdateCustomAspectRatio(string aspectRatio)
    {
        IsCustomAspectRatio = aspectRatio == "custom";

        if (!IsCustomAspectRatio) return;

        Parameters.Width = Math.Clamp((int)Parameters.Width, ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax);
        Parameters.Height = Math.Clamp((int)Parameters.Height, ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax);
    }

    private void ValidateParameters()
    {
        // Pollinations works anonymously, so its token is optional. Replicate requires its own
        // Bearer token. Prompt is required for both.
        var tokenOk = ModelConstants.Pollinations.IsId(Parameters.Model)
            ? true
            : !string.IsNullOrWhiteSpace(Parameters.ApiToken);
        // In Ideogram structured-JSON mode the prompt box must contain valid JSON.
        var jsonModeActive = Capabilities.IdeogramOptions && Parameters.UseJsonPrompt;
        var jsonOk = !jsonModeActive || IsValidJson(Parameters.Prompt);
        JsonPromptStateText = jsonModeActive
            ? jsonOk ? "Structured JSON: valid ✓" : "Structured JSON: not valid JSON"
            : null;
        IsValid = tokenOk && !string.IsNullOrWhiteSpace(Parameters.Prompt) && jsonOk;
    }

    private static bool IsValidJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { using var _ = JsonDocument.Parse(text); return true; }
        catch (JsonException) { return false; }
    }

    public bool IsPollinationsSelected => ModelConstants.Pollinations.IsId(Parameters.Model);

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    public GeneratorViewModel(
        IJobRunner jobRunner,
        IApiTokenStore tokenStore,
        IPollinationsTokenStore pollinationsTokenStore,
        IUiStateStore uiStateStore,
        IModelCatalogCoordinator catalogCoordinator,
        IModelDescriptorRegistry registry,
        IPromptBatchParser promptBatchParser,
        ILogger<GeneratorViewModel> logger)
    {
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _pollinationsTokenStore = pollinationsTokenStore ?? throw new ArgumentNullException(nameof(pollinationsTokenStore));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _catalogCoordinator = catalogCoordinator ?? throw new ArgumentNullException(nameof(catalogCoordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (promptBatchParser is null) throw new ArgumentNullException(nameof(promptBatchParser));

        Jobs.CollectionChanged += (_, _) => DispatchToUi(() => OnPropertyChanged(nameof(HasJobs)));

        // Hydrate capabilities + AR options from the registry. The model catalog itself lives
        // on ProviderFilterCoordinator (constructed below).
        _capabilities = _registry.CapabilitiesFor(ModelConstants.Flux.Pro11).Capabilities;
        _aspectRatioOptions = _capabilities.AspectRatios.ToList();

        _parameters = new ImageGenerationParameters
        {
            ApiToken = "",
            Model = ModelConstants.OpenAI.GptImage15OnReplicate,
            AspectRatio = "16:9",
            Width = 1920,
            Height = 1080,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = ValidationConstants.OutputQualityMax,
            SafetyTolerance = ValidationConstants.SafetyMax,
            PromptUpsampling = false,
            Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue),
            RandomizeSeed = true
        };

        // Seed the preferred AR with the constructor default so first-launch model swaps
        // already prefer "16:9" instead of slamming to caps.AspectRatios[0].
        InputImages = new InputImagesCoordinator(
            capsAccessor: () => Capabilities,
            setAspectRatioProgrammatically: SetAspectRatioProgrammatically,
            mirrorImagePromptsToParameters: MirrorImagePromptsToParameters,
            setStatus: SetStatus,
            initialPreferredAspectRatio: _parameters.AspectRatio);

        ProviderFilter = new ProviderFilterCoordinator(
            initialSeeds: _registry.Seeds,
            currentModelAccessor: () => Parameters.Model,
            setParametersModel: model => Parameters.Model = model,
            refreshCapabilities: RefreshCapabilities);

        Batch = new BatchCoordinator(
            promptBatchParser: promptBatchParser,
            parametersAccessor: () => Parameters,
            enqueueJob: job => DispatchToUi(() => Jobs.Insert(0, job)),
            runJob: RunJobAsync,
            setStatus: SetStatus,
            addAsInputAsync: InputImages.AddAsInputAsync);

        _parameters.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ImageGenerationParameters.AspectRatio):
                    UpdateCustomAspectRatio(_parameters.AspectRatio);
                    if (!_suppressPreferredArUpdate)
                        InputImages.RecordExplicitAspectRatioPick(_parameters.AspectRatio);
                    break;
                case nameof(ImageGenerationParameters.ApiToken):
                    // Persistence lives on TokenProviderViewModel now — only revalidate here.
                    ValidateParameters();
                    break;
                case nameof(ImageGenerationParameters.PollinationsApiToken):
                    ValidateParameters();
                    break;
                case nameof(ImageGenerationParameters.Prompt):
                    ValidateParameters();
                    _uiStateStore.PersistPrompt(_parameters.Prompt);
                    break;
                case nameof(ImageGenerationParameters.UseJsonPrompt):
                    ValidateParameters();
                    if (!_suppressJsonPromptPersist) _uiStateStore.PersistUseJsonPrompt(_parameters.UseJsonPrompt);
                    break;
                case nameof(ImageGenerationParameters.Resolution):
                    // Revert the deferred null push (see _lastValidResolution). Unsuppressed
                    // null/empty while the model still offers options is never a user pick —
                    // the Pickers only offer list members.
                    if (!_suppressResolutionPersist
                        && string.IsNullOrEmpty(_parameters.Resolution)
                        && ResolutionOptions.Contains(_lastValidResolution))
                    {
                        _logger.LogDebug(
                            "Resolution null push reverted -> \"{Value}\"", _lastValidResolution);
                        _suppressResolutionPersist = true;
                        try { _parameters.Resolution = _lastValidResolution; }
                        finally { _suppressResolutionPersist = false; }
                        break;
                    }

                    if (!string.IsNullOrEmpty(_parameters.Resolution)
                        && ResolutionOptions.Contains(_parameters.Resolution))
                    {
                        _lastValidResolution = _parameters.Resolution;
                    }

                    // Membership guard: only a value the current model actually offers can be
                    // a user pick. Binding artifacts (the Pickers pushing null on ItemsSource
                    // attach/swap) must never overwrite the saved choice.
                    var resolutionPersistable = !_suppressResolutionPersist
                        && !string.IsNullOrEmpty(_parameters.Resolution)
                        && ResolutionOptions.Contains(_parameters.Resolution);
                    _logger.LogDebug(
                        "Resolution changed -> \"{Value}\" (suppressed={Suppressed}, inOptions={InOptions}, persisting={Persisting})",
                        _parameters.Resolution,
                        _suppressResolutionPersist,
                        ResolutionOptions.Contains(_parameters.Resolution ?? string.Empty),
                        resolutionPersistable);
                    if (resolutionPersistable)
                    {
                        _uiStateStore.PersistResolution(_parameters.Resolution!);
                    }
                    break;
                case nameof(ImageGenerationParameters.Model):
                    ProviderFilter.SyncSelectionFromParameters(_parameters.Model);
                    if (!ProviderFilter.SuppressModelPersist) _uiStateStore.PersistModel(_parameters.Model);
                    // Switching to or from a Pollinations model changes the token-required rule
                    // and toggles the Pollinations-only UI sections.
                    ValidateParameters();
                    OnPropertyChanged(nameof(IsPollinationsSelected));
                    break;
            }
        };

        ValidateParameters();

        // Build the tabbed token list. Order = display order in the Picker. Each entry's
        // syncToParameters callback writes to the right parameters field so service code
        // (ReplicateImageGenerationService.parameters.ApiToken,
        // PollinationsImageGenerationService.parameters.PollinationsApiToken) keeps reading
        // its own slot.
        TokenProviders.Add(new TokenProviderViewModel(
            key: "replicate",
            displayName: "Replicate",
            placeholder: "Paste your Replicate API token…",
            helperText: "Required for Replicate-hosted models (Flux, GPT, Nano Banana). Stored in OS secure storage.",
            store: _tokenStore,
            syncToParameters: v => _parameters.ApiToken = v));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "pollinations",
            displayName: "Pollinations",
            placeholder: "Paste your Pollinations token (optional)…",
            helperText: "Optional. Anonymous mode is rate-limited (1 req / 15 s). Register at auth.pollinations.ai for higher limits.",
            store: _pollinationsTokenStore,
            syncToParameters: v => _parameters.PollinationsApiToken = v));
        _selectedTokenProvider = TokenProviders[0];
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task GenerateImageAsync()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Parameters.ApiToken)) missing.Add("API Token");
        if (string.IsNullOrWhiteSpace(Parameters.Prompt)) missing.Add("Prompt");
        if (missing.Count > 0)
        {
            SetStatus($"Missing required field(s): {string.Join(", ", missing)}.", StatusKind.Error);
            return;
        }

        if (Capabilities.IdeogramOptions && Parameters.UseJsonPrompt && !IsValidJson(Parameters.Prompt))
        {
            SetStatus("Structured JSON prompt is not valid JSON.", StatusKind.Error);
            return;
        }

        if (Parameters.RandomizeSeed)
            Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);

        var snapshot = Parameters.Clone();
        var job = new GenerationJob(snapshot, AddAsInputAsync);
        Jobs.Insert(0, job);

        // Click-time feedback: generation can take minutes, so confirm the click before
        // the job-row spinner is the only visible signal.
        _ = FlashAsync("Generation started");

        await RunJobAsync(job);
    }

    private async Task RunJobAsync(GenerationJob job)
    {
        try
        {
            var outcome = await _jobRunner.RunAsync(job.Parameters, job.Cts.Token);

            // HttpClient continuations can land on a ThreadPool thread; MAUI drops
            // PropertyChanged fired off the UI thread, so marshal the update back.
            DispatchToUi(() =>
            {
                job.ResultPath = outcome.SavedPath;
                job.StatusMessage = outcome.Message;
                job.StatusKind = outcome.Kind switch
                {
                    JobOutcomeKind.Saved => StatusKind.Success,
                    // The runner wraps "no image data" + canceled-style message into Failed; the
                    // VM-side check on the message text preserves the canceled-vs-error distinction
                    // for messages that originated server-side (e.g. user clicked Cancel mid-poll).
                    _ when outcome.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                        => StatusKind.Canceled,
                    _ => StatusKind.Error
                };
            });
        }
        catch (OperationCanceledException)
        {
            DispatchToUi(() =>
            {
                job.StatusMessage = "Canceled.";
                job.StatusKind = StatusKind.Canceled;
            });
        }
        catch (Exception ex)
        {
            var msg = $"Error: {ex.Message}";
            DispatchToUi(() =>
            {
                job.StatusMessage = msg;
                job.StatusKind = StatusKind.Error;
            });
        }
        finally
        {
            DispatchToUi(() => job.IsRunning = false);
            job.Cts.Dispose();
        }
    }

    private async Task FlashAsync(string message, int durationMs = 2500)
    {
        FlashMessage = message;
        await Task.Delay(durationMs);
        if (FlashMessage == message) FlashMessage = null;
    }

    [RelayCommand]
    private async Task OpenOutputFolderAsync()
    {
        try
        {
            // Directory.CreateDirectory + Process.Start are fast on a warm filesystem but can
            // stall the UI on cold/network/OneDrive paths. Push to the thread pool.
            await Task.Run(() =>
            {
                Directory.CreateDirectory(OutputPaths.GeneratedImagesDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{OutputPaths.GeneratedImagesDirectory}\"",
                    UseShellExecute = true
                });
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open Explorer: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task OpenGalleryAsync()
    {
        // Shell.Current is null in the unit-test harness; the catch keeps tests green if a
        // future test ever invokes this command, while production paths surface the error
        // through the existing status surface.
        try
        {
            await Shell.Current.GoToAsync("gallery");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open Gallery: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task OpenIdeogramEditorAsync()
    {
        // Hand the current prompt box content to the editor so an applied structured prompt
        // round-trips (re-open → same boxes). Same Shell.Current guard as OpenGalleryAsync.
        try
        {
            var json = Uri.EscapeDataString(Parameters.Prompt ?? string.Empty);
            var resolution = Uri.EscapeDataString(Parameters.Resolution ?? string.Empty);
            await Shell.Current.GoToAsync($"ideogram-editor?json={json}&resolution={resolution}");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open the structure editor: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        // Pollinations refresh is anonymous and doesn't need the Replicate token, so don't
        // gate the whole refresh on Replicate auth. ModelCatalogService.FetchAsync already
        // returns an empty list when the token is blank, so the coordinator yields
        // Pollinations-only results in that case.
        SetStatus("Fetching model catalogs…", StatusKind.Info);
        var merged = await _catalogCoordinator.RefreshAsync(Parameters.ApiToken);

        if (merged is null)
        {
            SetStatus("No models returned. Check your API tokens or network.", StatusKind.Error);
            return;
        }

        ProviderFilter.ApplyCatalog(merged);
        SetStatus($"Loaded {merged.Count} models.", StatusKind.Success);
    }

    /// <summary>
    /// Hydrate every provider token from secure storage. One pass per provider; each
    /// TokenProviderViewModel knows its own store and pushes through to Parameters internally.
    /// </summary>
    public async Task LoadAllTokensAsync()
    {
        if (_tokensLoaded) return;
        _tokensLoaded = true;

        foreach (var provider in TokenProviders)
        {
            await provider.LoadAsync();
        }
    }

    // Restore last-used prompt + model from Preferences. Call after the catalog has hydrated
    // so FilteredModels contains every model the user might have last selected. We set
    // SelectedModel (not Parameters.Model) so the Picker's two-way binding sees a reference
    // already in FilteredModels — this avoids a class of MAUI binding races where setting
    // Parameters.Model first would let the Picker reset SelectedItem to the first row before
    // the SelectedModel re-sync runs. OnSelectedModelChanged then propagates to Parameters.Model
    // and triggers RefreshCapabilities + the persist hook.
    public void LoadSavedUiState()
    {
        if (_uiStateLoaded) return;
        _uiStateLoaded = true;

        var savedPrompt = _uiStateStore.LoadPrompt();
        if (!string.IsNullOrEmpty(savedPrompt))
        {
            Parameters.Prompt = savedPrompt;
        }

        // First write equals the loaded value, so the OnChanged persist is a harmless echo —
        // no suppression window needed (unlike resolution, nothing else writes this field).
        var savedComfyUrl = _uiStateStore.LoadComfyUiBaseUrl();
        if (!string.IsNullOrEmpty(savedComfyUrl))
        {
            ComfyUiBaseUrl = savedComfyUrl;
        }

        var savedModel = _uiStateStore.LoadModel();
        if (!string.IsNullOrEmpty(savedModel))
        {
            ProviderFilter.RestoreSelectedModel(savedModel);
        }

        // Restore the resolution after the model: RestoreSelectedModel ran RefreshCapabilities,
        // which both populated ResolutionOptions for the saved model and slammed the value to
        // the first option. Only adopt a saved value the current model actually offers.
        // Suppressed from persistence — a restore isn't a user pick.
        var savedResolution = _uiStateStore.LoadResolution();
        var adoptResolution = !string.IsNullOrEmpty(savedResolution) && ResolutionOptions.Contains(savedResolution);
        _logger.LogDebug(
            "LoadSavedUiState: resolution saved=\"{Saved}\" current=\"{Current}\" adopting={Adopting}",
            savedResolution, Parameters.Resolution, adoptResolution);
        if (adoptResolution)
        {
            _suppressResolutionPersist = true;
            try { Parameters.Resolution = savedResolution!; }
            finally { _suppressResolutionPersist = false; }
        }

        // Restore the structured-JSON toggle LAST: the model restore above has settled
        // Capabilities, and the toggle only means anything on an Ideogram model — restoring
        // it blindly would re-check the box on a model whose Build has no json_prompt field.
        // Suppress persistence: a restore isn't a user preference change.
        if (Capabilities.IdeogramOptions && _uiStateStore.LoadUseJsonPrompt())
        {
            _suppressJsonPromptPersist = true;
            try { Parameters.UseJsonPrompt = true; }
            finally { _suppressJsonPromptPersist = false; }
        }
    }

    public async Task LoadCachedCatalogAsync(CancellationToken ct = default)
    {
        // OnAppearing can fire more than once in a MAUI page lifecycle; a second hydrate
        // would stomp a freshly-refreshed in-memory catalog with the older on-disk copy.
        if (_cachedCatalogLoaded) return;
        _cachedCatalogLoaded = true;

        try
        {
            var merged = await _catalogCoordinator.LoadCachedAsync(ct);
            if (merged is not null)
            {
                ProviderFilter.ApplyCatalog(merged);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model catalog hydrate failed");
        }
    }

    [RelayCommand]
    private void ForgetSelectedToken()
    {
        if (SelectedTokenProvider is null) return;
        var name = SelectedTokenProvider.DisplayName;
        SelectedTokenProvider.Forget();
        SetStatus($"{name} token cleared from secure storage.", StatusKind.Info);
    }

    // GenerationJob's "Use as input" delegate keeps targeting the VM-level method name; route
    // it into the coordinator.
    internal Task AddAsInputAsync(string filePath) => InputImages.AddAsInputAsync(filePath);
}
