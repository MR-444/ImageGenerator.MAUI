using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject, IStatusOwner, IInputImageTarget
{
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IGalleryService _galleryService;
    private readonly ILogger<GeneratorViewModel> _logger;

    [ObservableProperty]
    private ImageGenerationParameters _parameters;

    private ImageGenerationParameters? _subscribedParameters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPersistentStatusMessageVisible))]
    private string? _statusMessage;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    // Transient toast; StatusMessage holds what must not auto-clear.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlashMessageVisible))]
    [NotifyPropertyChangedFor(nameof(IsPersistentStatusMessageVisible))]
    private string? _flashMessage;

    private int _flashVersion;

    public bool IsFlashMessageVisible => !string.IsNullOrEmpty(FlashMessage);

    public bool IsPersistentStatusMessageVisible =>
        !IsFlashMessageVisible && !string.IsNullOrEmpty(StatusMessage);

    // Set by the mutation VM so MainPage shows a banner while the LLM calls run, before the
    // batch's job cards exist.
    [ObservableProperty]
    private bool _isAiMutationRunning;

    [ObservableProperty]
    private string? _aiMutationStatus;

    private bool _isGpuBusy;

    // True while any holder of the shared GPU gate (a ComfyUI render, local Ollama work) runs.
    // Fed by the gate only, so no caller can leave it stale.
    public bool IsGpuBusy
    {
        get => _isGpuBusy;
        private set
        {
            if (SetProperty(ref _isGpuBusy, value)) OnPropertyChanged(nameof(WorkspaceSummary));
        }
    }

    // The job cards and their runs.
    public JobQueueCoordinator Queue { get; }

    public ProviderFilterCoordinator ProviderFilter { get; }
    public BatchCoordinator Batch { get; }

    // Preferences that are not generation parameters. Sibling pages bind Generator.Settings.*.
    public AppSettingsCoordinator Settings { get; }

    // ComfyUI workflow state; re-probed on every model swap.
    public ComfyUiWorkflowCoordinator ComfyUi { get; }

    // "Replicate · flux-dev".
    public string SelectedModelSummary =>
        ProviderFilter.SelectedModel is { } model
            ? $"{ProviderFilter.SelectedProvider} · {model.Display}"
            : ProviderFilter.SelectedProvider;

    /// <summary>
    /// Whether this build can drive the selected model: false only for a Replicate model with no
    /// field list. The service refuses such a model before any HTTP, so anything that starts work
    /// checks this instead of queueing a job that can only fail.
    /// </summary>
    public bool ModelIsSupported => _registry.IsSchemaKnown(Parameters.Model);

    /// <summary>Note under the model picker for an unsupported model; null (Label collapsed) otherwise.</summary>
    public string? ModelFieldsNote => ModelIsSupported
        ? null
        : $"{Parameters.Model} is not supported: this build has no field list for it, so generating is refused.";

    // IInputImageTarget: the gallery's "Use as input" refuses on the spot instead of navigating
    // to a page whose Input Image card is hidden.
    public bool SupportsInputImage => Capabilities.ImagePrompt;
    public bool CanAddInputImage => InputImages.CanAddImage;

    public string WorkspaceSummary =>
        $"{Queue.JobStatusSummary} · {SelectedModelSummary} · {Queue.TotalImagesGeneratedText}"
        + (IsGpuBusy ? " · GPU busy" : string.Empty);

    [ObservableProperty]
    private List<string> _aspectRatioOptions;  // hydrated from registry in ctor

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

    // gpt-image-2.6-sunburst only; the list is empty (picker hidden) on every other model.
    [ObservableProperty]
    private List<string> _gptComplexityOptions = [];

    [ObservableProperty]
    private List<string> _outputFormats = [nameof(ImageOutputFormat.Png).ToLowerInvariant(), nameof(ImageOutputFormat.Jpg).ToLowerInvariant(), nameof(ImageOutputFormat.Webp).ToLowerInvariant()];

    // The API keys on SettingsPage.
    public TokenCoordinator Tokens { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    private bool _isCustomAspectRatio;

    public InputImagesCoordinator InputImages { get; }

    [ObservableProperty]
    private bool _isValid;

    // Structured-JSON validity under the checkbox. It reports what the VM holds, so a view/VM
    // prompt desync shows up here too.
    [ObservableProperty]
    private string? _jsonPromptStateText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    [NotifyPropertyChangedFor(nameof(SupportsResolution))]
    [NotifyPropertyChangedFor(nameof(SupportsGptQuality))]
    [NotifyPropertyChangedFor(nameof(SupportsGptInputFidelity))]
    [NotifyPropertyChangedFor(nameof(SupportsGptComplexity))]
    [NotifyPropertyChangedFor(nameof(SupportsIdeogramOptions))]
    [NotifyPropertyChangedFor(nameof(SupportsJsonPromptEditor))]
    [NotifyPropertyChangedFor(nameof(SupportsInputImage))]
    private ModelCapabilities _capabilities;  // hydrated from registry in ctor
    private bool _cachedCatalogLoaded;
    private bool _uiStateLoaded;

    // Programmatic aspect-ratio writes (model fallback, auto-AR from an input image) must not
    // become the sticky preferred ratio; only user picks do.
    private bool _suppressPreferredArUpdate;

    // The persist-suppression flags below keep capability fallbacks, restores and Picker
    // ItemsSource-swap pushes from overwriting a saved user choice.
    private bool _suppressJsonPromptPersist;
    private bool _suppressResolutionPersist;

    // The AR Picker pushes a valid first option on an ItemsSource swap, which the membership
    // check cannot tell from a user pick.
    private bool _suppressAspectRatioPersist;

    // The last Resolution that was in ResolutionOptions. After an ItemsSource swap the WinUI
    // ComboBox pushes null on a later dispatcher tick, outside any suppression window; this
    // value is restored when that happens.
    private string _lastValidResolution = string.Empty;

    partial void OnCapabilitiesChanged(ModelCapabilities value) => InputImages.OnCapabilitiesChanged();

    public bool SupportsCustomDimensions => Capabilities.CustomDimensions && IsCustomAspectRatio;
    public bool SupportsResolution => Capabilities.Resolutions is not null;
    public bool SupportsGptQuality => Capabilities.GptQualityOptions is not null;
    // Each gpt-image model declares its own knobs (2.6-sunburst has no input_fidelity, 1.5/2 no
    // complexity), so each Picker has its own visibility flag.
    public bool SupportsGptInputFidelity => Capabilities.GptInputFidelityOptions is not null;
    public bool SupportsGptComplexity => Capabilities.GptComplexityOptions is not null;
    public bool SupportsIdeogramOptions => Capabilities.IdeogramOptions;
    // Ideogram V4 and ComfyUI workflows (whose graphs consume the caption JSON).
    public bool SupportsJsonPromptEditor => Capabilities.JsonPromptEditor;

    private void AttachParameters(ImageGenerationParameters parameters)
    {
        if (ReferenceEquals(_subscribedParameters, parameters)) return;
        if (_subscribedParameters is not null)
            _subscribedParameters.PropertyChanged -= OnParametersPropertyChanged;

        _subscribedParameters = parameters;
        parameters.PropertyChanged += OnParametersPropertyChanged;
    }

    partial void OnParametersChanged(ImageGenerationParameters value)
    {
        AttachParameters(value);
        // A replacement instance has empty token slots, and the token rows push only on change,
        // so re-seed from the rows before revalidating.
        Tokens.PushAll();
        UpdateCustomAspectRatio(value.AspectRatio);
        ValidateParameters();
        ProviderFilter.SyncSelectionFromParameters(value.Model);
    }

    private void RefreshCapabilities(string? modelValue)
    {
        var caps = _registry.CapabilitiesFor(modelValue ?? string.Empty).Capabilities;

        // The option lists change before Capabilities, so the binding cascade from the
        // Capabilities setter sees consistent state. Each ItemsSource swap sits inside its
        // persist-suppression window: the two-way-bound Picker pushes a value synchronously
        // during the swap, and that push is not a user pick.
        _suppressAspectRatioPersist = true;
        try
        {
            AspectRatioOptions = [.. caps.AspectRatios];
            // Sticky AR: the user's last pick if this model offers it, else the current value,
            // else the model's first ratio. With no ratios at all, keep the current value rather
            // than invent a size the model never offered.
            var current = Parameters.AspectRatio;
            var fallback = caps.AspectRatios.Count > 0 ? caps.AspectRatios[0] : current;
            var target =
                (InputImages.PreferredAspectRatio is { } pref && caps.AspectRatios.Contains(pref)) ? pref :
                caps.AspectRatios.Contains(current) ? current :
                fallback;
            if (!string.Equals(target, current, StringComparison.Ordinal))
            {
                SetAspectRatioProgrammatically(target);
            }
        }
        finally { _suppressAspectRatioPersist = false; }

        // Exactly ONE Picker may bind Parameters.Resolution: two bound at once livelock the UI
        // thread in a synchronous null/value ping-pong during this swap.
        _suppressResolutionPersist = true;
        try
        {
            // Read before the swap, which pushes null into Parameters.Resolution.
            var previousResolution = Parameters.Resolution;
            ResolutionOptions = caps.Resolutions?.ToList() ?? [];
            if (ResolutionOptions.Count > 0)
            {
                // Sticky resolution: the current value if offered, else the saved choice for
                // this model, else the first option.
                var savedResolution = _uiStateStore.LoadResolution(modelValue);
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

        // Models without an output-format picker (Ideogram, unknown Replicate models) pin PNG.
        if (!caps.OutputFormatSelectable) Parameters.OutputFormat = ImageOutputFormat.Png;
        // A model without a JSON prompt field must not keep the toggle on: it gates validation
        // and Build. Not persisted, since it is a capability consequence, not a user choice.
        if (!caps.JsonPromptEditor)
        {
            _suppressJsonPromptPersist = true;
            try { Parameters.UseJsonPrompt = false; }
            finally { _suppressJsonPromptPersist = false; }
        }

        // The GPT knob enums differ per model and host (2.6-sunburst adds quality xhigh/max and a
        // complexity field), and a value the new model does not offer comes back as a 422. Snap
        // it to the app's default where offered, else the first option. The first option is no
        // default: quality and moderation lists open with "auto", the priced end. Assign the
        // options first: an empty list makes the two-way-bound Picker write null into the knob.
        void Knob(IReadOnlyList<string>? offered, Action<List<string>> setOptions,
            Func<string?> current, string appDefault, Action<string> select)
        {
            var options = offered?.ToList() ?? [];
            setOptions(options);
            var value = current();
            if (options.Count == 0 || (value is not null && options.Contains(value))) return;
            select(options.Contains(appDefault, StringComparer.Ordinal) ? appDefault : options[0]);
        }

        Knob(caps.GptQualityOptions, o => GptQualityOptions = o, () => Parameters.GptQuality,
            ImageGenerationParameters.DefaultGptQuality, v => Parameters.GptQuality = v);
        Knob(caps.GptBackgroundOptions, o => GptBackgroundOptions = o, () => Parameters.GptBackground,
            ImageGenerationParameters.DefaultGptBackground, v => Parameters.GptBackground = v);
        Knob(caps.GptModerationOptions, o => GptModerationOptions = o, () => Parameters.GptModeration,
            ImageGenerationParameters.DefaultGptModeration, v => Parameters.GptModeration = v);
        Knob(caps.GptInputFidelityOptions, o => GptInputFidelityOptions = o, () => Parameters.GptInputFidelity,
            ImageGenerationParameters.DefaultGptInputFidelity, v => Parameters.GptInputFidelity = v);
        Knob(caps.GptComplexityOptions, o => GptComplexityOptions = o, () => Parameters.GptComplexity,
            ImageGenerationParameters.DefaultGptComplexity, v => Parameters.GptComplexity = v);

        // Drop attached images beyond the new model's cap now rather than at generation time.
        // ComfyUI waits for the workflow probe below: its cap depends on the workflow, and the
        // static 0 would drop an image the probe is about to allow.
        if (!ModelConstants.ComfyUi.IsId(modelValue))
        {
            InputImages.TruncateToMaxInputs(caps.MaxImageInputs);
        }

        Capabilities = caps;
        ValidateParameters();

        // Fire-and-forget; each probe catches its own errors and ignores stale completions.
        ComfyUi.RefreshForModel(modelValue);
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

        Parameters.Width = Math.Clamp(Parameters.Width, ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax);
        Parameters.Height = Math.Clamp(Parameters.Height, ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax);
    }

    private void ValidateParameters()
    {
        var jsonModeActive = GenerationReadiness.IsJsonModeActive(Parameters, Capabilities.JsonPromptEditor);
        var jsonError = jsonModeActive ? GenerationReadiness.JsonErrorDetail(Parameters.Prompt) : null;
        JsonPromptStateText = jsonModeActive
            ? jsonError is null ? "Structured JSON: valid ✓" : $"Structured JSON: not valid — {jsonError}"
            : null;
        // MainPage's Ctrl+Enter handler reads this flag too.
        IsValid = ModelIsSupported
                  && GenerationReadiness.MissingFields(Parameters).Count == 0
                  && jsonError is null;
    }

    public bool IsPollinationsSelected => ModelConstants.Pollinations.IsId(Parameters.Model);

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    /// <summary>Reports dropped AI-mutation variants on the action-bar status, so fewer job cards than
    /// asked for comes with a reason. No-op when every variant succeeded.</summary>
    public void ReportAiMutationOutcome(int rendered, int failed)
    {
        if (failed <= 0)
            return;

        var plural = failed == 1 ? "" : "s";
        SetStatus(
            $"AI mutation: {rendered} variant(s) queued, {failed} dropped — the local model likely timed out "
            + $"on {failed} call{plural}. See app.log; try a non-thinking model or fewer variants.",
            StatusKind.Warning);
    }

    public GeneratorViewModel(
        IJobRunner jobRunner,
        IApiTokenStore tokenStore,
        IPollinationsTokenStore pollinationsTokenStore,
        IComfyUiAuthStore comfyUiAuthStore,
        ICivitaiTokenStore civitaiTokenStore,
        IAnthropicTokenStore anthropicTokenStore,
        ICivitaiPostingService civitaiPostingService,
        IUiStateStore uiStateStore,
        IModelCatalogCoordinator catalogCoordinator,
        IModelDescriptorRegistry registry,
        IPromptBatchParser promptBatchParser,
        IComfyUiCheckpointService checkpointService,
        IGalleryService galleryService,
        IFolderPicker folderPicker,
        ILogger<GeneratorViewModel> logger,
        IOllamaModelCatalog? ollamaModelCatalog = null,
        IComfyUiVramService? comfyVram = null,
        IGpuGate? gpuGate = null,
        IOpenRouterTokenStore? openRouterTokenStore = null,
        IOpenRouterModelCatalog? openRouterModelCatalog = null,
        IComfyUiLoraService? comfyUiLoraService = null,
        IAnthropicModelCatalog? anthropicModelCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(jobRunner);
        ArgumentNullException.ThrowIfNull(tokenStore);
        ArgumentNullException.ThrowIfNull(pollinationsTokenStore);
        ArgumentNullException.ThrowIfNull(comfyUiAuthStore);
        ArgumentNullException.ThrowIfNull(civitaiTokenStore);
        ArgumentNullException.ThrowIfNull(anthropicTokenStore);
        ArgumentNullException.ThrowIfNull(civitaiPostingService);
        ArgumentNullException.ThrowIfNull(uiStateStore);
        ArgumentNullException.ThrowIfNull(catalogCoordinator);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(promptBatchParser);
        ArgumentNullException.ThrowIfNull(galleryService);
        ArgumentNullException.ThrowIfNull(logger);
        _uiStateStore = uiStateStore;
        _catalogCoordinator = catalogCoordinator;
        _registry = registry;
        _galleryService = galleryService;
        _logger = logger;

        // Coordinators are composed here rather than resolved from DI, so the tests exercise
        // the same constructor the app runs.
        Settings = new AppSettingsCoordinator(
            uiStateStore, folderPicker, ollamaModelCatalog, openRouterModelCatalog,
            anthropicModelCatalog, anthropicTokenStore, SetStatus, logger);

        // Writes go through the Parameters property, never the field, so a replaced instance
        // receives them.
        Tokens = new TokenCoordinator(
            tokenStore, pollinationsTokenStore, comfyUiAuthStore, civitaiTokenStore,
            anthropicTokenStore, openRouterTokenStore, civitaiPostingService);
        Tokens.ReplicateTokenChanged += token => Parameters.ApiToken = token;
        Tokens.PollinationsTokenChanged += token => Parameters.PollinationsApiToken = token;
        Tokens.StatusRaised += SetStatus;

        Queue = new JobQueueCoordinator(
            jobRunner, civitaiPostingService, galleryService, comfyVram, gpuGate, Settings,
            isBatchRunning: () => Batch is { IsBatchRunning: true },
            flash: message => _ = FlashAsync(message),
            logger: logger);

        // Read the gate when the update runs, not when the event fired: a release and the next
        // acquire can be dispatched in either order.
        if (gpuGate is not null)
        {
            gpuGate.BusyChanged += (_, _) => DispatchToUi(() => IsGpuBusy = gpuGate.IsBusy);
            _isGpuBusy = gpuGate.IsBusy;
        }

        Queue.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(JobQueueCoordinator.JobStatusSummary)
                or nameof(JobQueueCoordinator.TotalImagesGeneratedText))
            {
                OnPropertyChanged(nameof(WorkspaceSummary));
            }
        };

        // Defaults live on ImageGenerationParameters only. The starting seed is random per launch.
        _parameters = new ImageGenerationParameters
        {
            Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue)
        };

        // Seeded from the starting model, so the coordinators built below see its capabilities.
        _capabilities = _registry.CapabilitiesFor(_parameters.Model).Capabilities;
        _aspectRatioOptions = [.. _capabilities.AspectRatios];

        // The starting ratio is the first preferred ratio, so early model swaps keep it.
        InputImages = new InputImagesCoordinator(
            capsAccessor: () => Capabilities,
            setAspectRatioProgrammatically: SetAspectRatioProgrammatically,
            mirrorImagePromptsToParameters: MirrorImagePromptsToParameters,
            setStatus: SetStatus,
            logger: logger,
            initialPreferredAspectRatio: _parameters.AspectRatio);

        // Built before ProviderFilter, whose selection callback reaches ComfyUi through
        // RefreshCapabilities. Coordinators read Parameters through delegates because the
        // instance is replaceable.
        ComfyUi = new ComfyUiWorkflowCoordinator(
            checkpointService: checkpointService,
            uiStateStore: uiStateStore,
            logger: logger,
            parametersAccessor: () => Parameters,
            capsAccessor: () => Capabilities,
            capsSetter: value => Capabilities = value,
            truncateInputImages: max => InputImages.TruncateToMaxInputs(max),
            comfyUiLoraService: comfyUiLoraService);

        ProviderFilter = new ProviderFilterCoordinator(
            initialSeeds: _registry.Seeds,
            currentModelAccessor: () => Parameters.Model,
            setParametersModel: model => Parameters.Model = model,
            refreshCapabilities: RefreshCapabilities);

        ProviderFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProviderFilterCoordinator.SelectedProvider)
                or nameof(ProviderFilterCoordinator.SelectedModel))
            {
                OnPropertyChanged(nameof(SelectedModelSummary));
                OnPropertyChanged(nameof(WorkspaceSummary));
                OnPropertyChanged(nameof(ModelFieldsNote));
            }
        };

        Batch = new BatchCoordinator(
            promptBatchParser: promptBatchParser,
            parametersAccessor: () => Parameters,
            enqueueJob: Queue.Enqueue,
            runJob: Queue.RunBatchJobAsync,
            setStatus: SetStatus,
            addAsInputAsync: InputImages.AddAsInputAsync,
            mutateFromImageAsync: path => MutateFromImageAsync(path));

        Batch.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName == nameof(BatchCoordinator.IsBatchRunning) && !Batch.IsBatchRunning)
                _ = Queue.CompleteBatchAsync();
        };

        AttachParameters(_parameters);

        ValidateParameters();
    }

    private void OnParametersPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ImageGenerationParameters.AspectRatio):
                UpdateCustomAspectRatio(Parameters.AspectRatio);
                if (!_suppressPreferredArUpdate)
                {
                    InputImages.RecordExplicitAspectRatioPick(Parameters.AspectRatio);
                    // "custom" is not persisted: its width and height aren't either.
                    if (!_suppressAspectRatioPersist
                        && !string.IsNullOrEmpty(Parameters.AspectRatio)
                        && Parameters.AspectRatio != "custom"
                        && AspectRatioOptions.Contains(Parameters.AspectRatio))
                    {
                        _uiStateStore.PersistAspectRatio(Parameters.AspectRatio, Parameters.Model);
                    }
                }
                break;
            case nameof(ImageGenerationParameters.ApiToken):
            case nameof(ImageGenerationParameters.PollinationsApiToken):
                ValidateParameters();
                break;
            case nameof(ImageGenerationParameters.Prompt):
                ValidateParameters();
                _uiStateStore.PersistPrompt(Parameters.Prompt);
                break;
            case nameof(ImageGenerationParameters.UseJsonPrompt):
                ValidateParameters();
                if (!_suppressJsonPromptPersist) _uiStateStore.PersistUseJsonPrompt(Parameters.UseJsonPrompt);
                break;
            case nameof(ImageGenerationParameters.Resolution):
                // The deferred null push (see _lastValidResolution); the Picker itself only
                // offers list members.
                if (!_suppressResolutionPersist
                    && string.IsNullOrEmpty(Parameters.Resolution)
                    && ResolutionOptions.Contains(_lastValidResolution))
                {
                    _logger.LogDebug(
                        "Resolution null push reverted -> \"{Value}\"", _lastValidResolution);
                    _suppressResolutionPersist = true;
                    try { Parameters.Resolution = _lastValidResolution; }
                    finally { _suppressResolutionPersist = false; }
                    break;
                }

                if (!string.IsNullOrEmpty(Parameters.Resolution)
                    && ResolutionOptions.Contains(Parameters.Resolution))
                {
                    _lastValidResolution = Parameters.Resolution;
                }

                // Only a value the current model offers can be a user pick.
                var resolutionPersistable = !_suppressResolutionPersist
                    && !string.IsNullOrEmpty(Parameters.Resolution)
                    && ResolutionOptions.Contains(Parameters.Resolution);
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug(
                        "Resolution changed -> \"{Value}\" (suppressed={Suppressed}, inOptions={InOptions}, persisting={Persisting})",
                        Parameters.Resolution,
                        _suppressResolutionPersist,
                        ResolutionOptions.Contains(Parameters.Resolution),
                        resolutionPersistable);
                if (resolutionPersistable)
                {
                    _uiStateStore.PersistResolution(Parameters.Resolution, Parameters.Model);
                }
                break;
            case nameof(ImageGenerationParameters.CivitaiModelRef):
                // The store skips identical writes, so the launch restore costs nothing.
                _uiStateStore.PersistCivitaiModelRef(Parameters.CivitaiModelRef);
                break;
            case nameof(ImageGenerationParameters.UpscaleAfterRender):
                ComfyUi.OnUpscaleAfterRenderChanged();
                break;
            case nameof(ImageGenerationParameters.Model):
                ProviderFilter.SyncSelectionFromParameters(Parameters.Model);
                if (!ProviderFilter.SuppressModelPersist) _uiStateStore.PersistModel(Parameters.Model);
                ValidateParameters();
                OnPropertyChanged(nameof(IsPollinationsSelected));
                // Raised here too: a model missing from the catalog never becomes the
                // ProviderFilter selection, so that hook would not fire.
                OnPropertyChanged(nameof(ModelFieldsNote));
                break;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task GenerateImageAsync()
    {
        var missing = GenerationReadiness.MissingFields(Parameters);
        if (missing.Count > 0)
        {
            SetStatus($"Missing required field(s): {string.Join(", ", missing)}.", StatusKind.Error);
            return;
        }

        if (GenerationReadiness.IsJsonModeActive(Parameters, Capabilities.JsonPromptEditor)
            && GenerationReadiness.JsonErrorDetail(Parameters.Prompt) is { } jsonError)
        {
            SetStatus($"Structured JSON prompt is not valid JSON: {jsonError}", StatusKind.Error);
            return;
        }

        if (Parameters.RandomizeSeed)
            Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);

        var snapshot = Parameters.Clone();
        var job = new GenerationJob(snapshot, AddAsInputAsync, path => MutateFromImageAsync(path));
        Queue.Enqueue(job);

        // JobCount makes an unintended extra run visible in the log (e.g. a focused button re-fired by Space).
        _logger.LogInformation(
            "Generate activated Model={Model} Seed={Seed} JobCount={Count}",
            snapshot.Model, snapshot.Seed, Queue.Jobs.Count);

        _ = FlashAsync("Generation started");

        await Queue.RunAsync(job);
    }

    private async Task FlashAsync(string message, int durationMs = 2500)
    {
        var version = ++_flashVersion;
        DispatchToUi(() => FlashMessage = message);
        await Task.Delay(durationMs);
        DispatchToUi(() =>
        {
            if (_flashVersion == version) FlashMessage = null;
        });
    }

    /// <summary>
    /// Navigates and reports a failure on the status line. Shell.Current is null in unit tests.
    /// <paramref name="target"/> is the noun in "Couldn't open …".
    /// </summary>
    private async Task NavigateAsync(string route, string target)
    {
        try
        {
            await Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open {target}: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>
    /// Hand-off to the mutation page, read once on its appearance. A typed model, because a
    /// string round-trip would strip explicit <c>Element.SlotTag</c> values.
    /// </summary>
    public V4JsonPrompt? PendingMutationBase { get; set; }

    /// <summary>Hand-off from the Gallery's breed action to the mutation page, read once.</summary>
    public IReadOnlyList<V4JsonPrompt>? PendingBreedSet { get; set; }

    /// <summary>
    /// Seeds the mutation page from the prompt box. A non-structured prompt leaves
    /// <see cref="PendingMutationBase"/> null and the mutation page reports it.
    /// </summary>
    [RelayCommand]
    private async Task OpenMutationEngineAsync()
    {
        try
        {
            PendingMutationBase = V4JsonPromptSerializer.Deserialize(Parameters.Prompt);
        }
        catch (V4JsonPromptParseException)
        {
            PendingMutationBase = null;
        }

        await NavigateAsync("mutation-engine", "the mutation engine");
    }

    /// <summary>
    /// "Mutate from this" on a saved image: adopts only its caption as the mutation base and
    /// keeps the current model and settings, so there is no surprise model or resolution switch.
    /// A non-structured caption leaves <see cref="PendingMutationBase"/> null.
    /// </summary>
    public async Task MutateFromImageAsync(string? filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var meta = await _galleryService.ReadMetadataAsync(filePath, ct);
            if (meta is null || !meta.TryGetValue("Prompt", out var prompt) || string.IsNullOrWhiteSpace(prompt))
            {
                SetStatus("No caption found in this image.", StatusKind.Warning);
                return;
            }

            Parameters.Prompt = prompt;
            try { PendingMutationBase = V4JsonPromptSerializer.Deserialize(prompt); }
            catch (V4JsonPromptParseException) { PendingMutationBase = null; }

            await NavigateAsync("mutation-engine", "the mutation engine");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mutate from image failed for {Path}", filePath);
            SetStatus("Couldn't load this image's caption. See app.log for details.", StatusKind.Error);
        }
    }

    [RelayCommand]
    private Task OpenIdeaToPromptAsync() => NavigateAsync("idea-to-prompt", "the prompt builder");

    [RelayCommand]
    private Task OpenIdeogramEditorAsync() => NavigateAsync(BuildEditorRoute(), "the structure editor");

    /// <summary>
    /// The editor route, carrying the prompt and the output shape. On ComfyUI the shape lives in
    /// Parameters.AspectRatio (a ResolutionSelector combo string), elsewhere in Resolution.
    /// </summary>
    internal string BuildEditorRoute()
    {
        var json = Uri.EscapeDataString(Parameters.Prompt);
        var shape = ModelConstants.ComfyUi.IsId(Parameters.Model)
            ? Parameters.AspectRatio
            : Parameters.Resolution;
        return $"ideogram-editor?json={json}&resolution={Uri.EscapeDataString(shape)}";
    }

    [RelayCommand]
    private async Task RefreshModelsAsync()
    {
        // Not gated on the Replicate token: without one, only the anonymous catalogs return models.
        SetStatus("Fetching model catalogs…", StatusKind.Info);
        var merged = await _catalogCoordinator.RefreshAsync(Parameters.ApiToken);

        if (merged is null)
        {
            SetStatus("No models returned. Check your API tokens or network.", StatusKind.Error);
            return;
        }

        ProviderFilter.ApplyCatalog(merged);
        // The catalog can change which ComfyUI workflows exist.
        await ComfyUi.RefreshLorasForCurrentModelAsync(Parameters.Model);
        // A listed model is not necessarily one this build can drive; say how many are.
        var unsupported = merged.Count(m => !_registry.IsSchemaKnown(m.Value));
        SetStatus(unsupported > 0
            ? $"Loaded {merged.Count} models ({merged.Count - unsupported} this build can drive; the other {unsupported} are marked and will not run)."
            : $"Loaded {merged.Count} models.",
            StatusKind.Success);
    }

    // Restores the last session's choices. Call after the catalog has loaded. The model is
    // restored through the ProviderFilter selection, not Parameters.Model, so the Picker sees an
    // item already in its list instead of resetting to the first row.
    public void LoadSavedUiState()
    {
        if (_uiStateLoaded) return;
        _uiStateLoaded = true;

        var savedModelRef = _uiStateStore.LoadCivitaiModelRef();
        if (!string.IsNullOrEmpty(savedModelRef))
        {
            Parameters.CivitaiModelRef = savedModelRef;
        }

        var savedPrompt = _uiStateStore.LoadPrompt();
        if (!string.IsNullOrEmpty(savedPrompt))
        {
            Parameters.Prompt = savedPrompt;
        }

        Settings.LoadSavedSettings();

        var savedModel = _uiStateStore.LoadModel();
        if (!string.IsNullOrEmpty(savedModel))
        {
            ProviderFilter.RestoreSelectedModel(savedModel);
        }

        // After the model, which settled the option lists. Restores are not persisted.
        var savedResolution = _uiStateStore.LoadResolution(Parameters.Model);
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

        // Recorded explicitly so the restored ratio is also the session's preferred one.
        var savedAspectRatio = _uiStateStore.LoadAspectRatio(Parameters.Model);
        if (!string.IsNullOrEmpty(savedAspectRatio)
            && savedAspectRatio != "custom"
            && AspectRatioOptions.Contains(savedAspectRatio))
        {
            SetAspectRatioProgrammatically(savedAspectRatio);
            InputImages.RecordExplicitAspectRatioPick(savedAspectRatio);
        }

        // Only on a model with a JSON prompt field.
        if (Capabilities.JsonPromptEditor && _uiStateStore.LoadUseJsonPrompt())
        {
            _suppressJsonPromptPersist = true;
            try { Parameters.UseJsonPrompt = true; }
            finally { _suppressJsonPromptPersist = false; }
        }
    }

    /// <summary>
    /// Loads a saved image's embedded recipe into the generator. Same order as
    /// <see cref="LoadSavedUiState"/>: model first, so the option lists settle. Common lines are
    /// applied here, per-model extras by the model's <c>IMetadataDescriber.Apply</c>. An unknown
    /// model still loads prompt and seed.
    /// </summary>
    public async Task RemixFromImageAsync(string? filePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        try
        {
            var meta = await _galleryService.ReadMetadataAsync(filePath, ct);
            if (meta is null || meta.Count == 0)
            {
                SetStatus("No recipe found in this image.", StatusKind.Warning);
                return;
            }

            meta.ApplyString("Prompt", v => Parameters.Prompt = v);
            if (meta.TryGetValue("Seed", out var seedText)
                && long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            {
                Parameters.Seed = seed;
                // Otherwise Generate re-rolls the seed.
                Parameters.RandomizeSeed = false;
            }

            meta.TryGetValue("ModelName", out var modelId);
            var modelKnown = !string.IsNullOrEmpty(modelId)
                && ProviderFilter.AllModels.Any(m => m.Value == modelId);
            if (!modelKnown)
            {
                _logger.LogInformation("Remix: model \"{Model}\" not in catalog — loaded prompt+seed only", modelId);
                SetStatus(
                    string.IsNullOrEmpty(modelId)
                        ? "This image has no model recorded — loaded prompt and seed only."
                        : $"Model '{modelId}' isn't available — loaded prompt and seed only.",
                    StatusKind.Warning);
                return;
            }

            ProviderFilter.RestoreSelectedModel(modelId!);

            if (meta.TryGetValue("AspectRatio", out var ar) && Capabilities.AspectRatios.Contains(ar))
            {
                SetAspectRatioProgrammatically(ar);
                // Custom sizes are recorded in the Dimensions line.
                if (ar == "custom" && Capabilities.CustomDimensions
                    && meta.TryGetValue("Dimensions", out var dims))
                {
                    var parts = dims.Split('x', StringSplitOptions.TrimEntries);
                    if (parts.Length == 2
                        && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    {
                        Parameters.Width = Math.Clamp(w, ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax);
                        Parameters.Height = Math.Clamp(h, ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax);
                    }
                }
            }

            if (Capabilities.OutputFormatSelectable
                && meta.TryGetValue("Format", out var fmt)
                && Enum.TryParse<ImageOutputFormat>(fmt, ignoreCase: true, out var outputFormat))
            {
                Parameters.OutputFormat = outputFormat;
            }

            if (meta.TryGetValue("Quality", out var qualityText)
                && int.TryParse(qualityText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quality))
            {
                Parameters.OutputQuality = quality;
            }

            // Per-model extras. A restore must not overwrite the persisted picks.
            _suppressResolutionPersist = true;
            _suppressJsonPromptPersist = true;
            try { _registry.MetadataFor(modelId!)?.Apply(Parameters, meta); }
            finally
            {
                _suppressResolutionPersist = false;
                _suppressJsonPromptPersist = false;
            }

            SetStatus("Recipe loaded from image.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Remix from image failed for {Path}", filePath);
            SetStatus("Couldn't load this image's recipe. See app.log for details.", StatusKind.Error);
        }
    }

    public async Task LoadCachedCatalogAsync(CancellationToken ct = default)
    {
        // OnAppearing fires more than once; a second load would replace a refreshed catalog
        // with the older copy on disk.
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

    private Task AddAsInputAsync(string filePath) => InputImages.AddAsInputAsync(filePath);
}
