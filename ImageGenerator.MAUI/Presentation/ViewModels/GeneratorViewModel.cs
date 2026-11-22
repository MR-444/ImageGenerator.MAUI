using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
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
    private readonly IJobRunner _jobRunner;
    private readonly ICivitaiPostingService _civitaiPostingService;
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IGalleryService _galleryService;
    private readonly IComfyUiVramService? _comfyVram;
    private readonly IGpuGate? _gpuGate;
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

    // True while any holder of the shared GPU gate (a ComfyUI render, local Ollama work) runs.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WorkspaceSummary))]
    private bool _isGpuBusy;

    private IDisposable? _batchGpuLease;
    private string? _batchComfyModel;

    // Images in the output folder; null until LoadTotalImagesGeneratedAsync has counted them.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalImagesGeneratedText))]
    [NotifyPropertyChangedFor(nameof(WorkspaceSummary))]
    private int? _totalImagesGenerated;

    public string TotalImagesGeneratedText =>
        TotalImagesGenerated is { } n ? $"{n} image{(n == 1 ? "" : "s")}" : "…";

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;

    public bool HasFinishedJobs => Jobs.Any(j => j.IsFinished);

    // "2 running · 1 queued · 5 done". A queued batch job is not running and still at StatusKind.Info.
    public string JobStatusSummary
    {
        get
        {
            var running = Jobs.Count(j => j.IsRunning);
            var queued = Jobs.Count(j => j is { IsRunning: false, StatusKind: StatusKind.Info });
            var done = Jobs.Count(j => j.IsFinished);
            var parts = new List<string>(3);
            if (running > 0) parts.Add($"{running} running");
            if (queued > 0) parts.Add($"{queued} queued");
            if (done > 0) parts.Add($"{done} done");
            return parts.Count > 0 ? string.Join(" · ", parts) : "No jobs yet";
        }
    }

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
        $"{JobStatusSummary} · {SelectedModelSummary} · {TotalImagesGeneratedText}"
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

    // One token Entry per item; adding a provider needs no XAML change.
    public ObservableCollection<TokenProviderViewModel> TokenProviders { get; } = [];

    [ObservableProperty]
    private TokenProviderViewModel? _selectedTokenProvider;

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

    // The newest saved job: the one featured card. Canceled and failed jobs never become it.
    [ObservableProperty]
    private GenerationJob? _latestCompletedJob;

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
    private bool _tokensLoaded;
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
        foreach (var provider in TokenProviders) provider.PushToParameters();
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
        var tokenOk = TokenlessModel(Parameters.Model)
            || !string.IsNullOrWhiteSpace(Parameters.ApiToken);
        var jsonModeActive = Capabilities.JsonPromptEditor && Parameters.UseJsonPrompt;
        var jsonError = jsonModeActive ? JsonErrorDetail(Parameters.Prompt) : null;
        var jsonOk = !jsonModeActive || jsonError is null;
        JsonPromptStateText = jsonModeActive
            ? jsonError is null ? "Structured JSON: valid ✓" : $"Structured JSON: not valid — {jsonError}"
            : null;
        // MainPage's Ctrl+Enter handler reads this flag too.
        IsValid = ModelIsSupported && tokenOk && !string.IsNullOrWhiteSpace(Parameters.Prompt) && jsonOk;
    }

    // Pollinations works anonymously and ComfyUI is the user's own server; Replicate needs a token.
    private static bool TokenlessModel(string? model) =>
        ModelConstants.Pollinations.IsId(model) || ModelConstants.ComfyUi.IsId(model);

    // Null for valid JSON, else a short reason with line and position, so the user can find the
    // break in a long single-line blob.
    private static string? JsonErrorDetail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "prompt is empty";
        try { using var _ = JsonDocument.Parse(text); return null; }
        catch (JsonException ex)
        {
            // Drop the " LineNumber: … | BytePositionInLine: …" tail; the suffix below says it better.
            var msg = ex.Message;
            var cut = msg.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut > 0) msg = msg[..cut].TrimEnd();
            return ex is { LineNumber: { } line, BytePositionInLine: { } pos }
                ? $"{msg} (line {line + 1}, pos {pos})"
                : msg;
        }
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

    // Per-job PropertyChanged is attached here so a job turning finished re-evaluates the gates.
    private void OnJobsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => DispatchToUi(() =>
        {
            if (e.OldItems is not null)
                foreach (GenerationJob job in e.OldItems) job.PropertyChanged -= OnJobPropertyChanged;
            if (e.NewItems is not null)
                foreach (GenerationJob job in e.NewItems) job.PropertyChanged += OnJobPropertyChanged;

            OnPropertyChanged(nameof(HasJobs));
            RaiseFinishedJobsChanged();
        });

    private void OnJobPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenerationJob.IsFinished))
            DispatchToUi(RaiseFinishedJobsChanged);
    }

    private void RaiseFinishedJobsChanged()
    {
        OnPropertyChanged(nameof(HasFinishedJobs));
        OnPropertyChanged(nameof(JobStatusSummary));
        OnPropertyChanged(nameof(WorkspaceSummary));
        ClearFinishedJobsCommand.NotifyCanExecuteChanged();
    }

    // Drops finished cards; running and queued jobs stay. The featured card is always finished.
    [RelayCommand(CanExecute = nameof(HasFinishedJobs))]
    private void ClearFinishedJobs()
    {
        var finished = Jobs.Where(j => j.IsFinished).ToList();
        foreach (var job in finished) Jobs.Remove(job);

        if (LatestCompletedJob is not null && !Jobs.Contains(LatestCompletedJob))
            LatestCompletedJob = null;

        _ = FlashAsync($"Cleared {finished.Count} finished job{(finished.Count == 1 ? "" : "s")}.");
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
        _jobRunner = jobRunner;
        _civitaiPostingService = civitaiPostingService;
        _uiStateStore = uiStateStore;
        _catalogCoordinator = catalogCoordinator;
        _registry = registry;
        _galleryService = galleryService;
        _comfyVram = comfyVram;
        _gpuGate = gpuGate;
        _logger = logger;

        // Coordinators are composed here rather than resolved from DI, so the tests exercise
        // the same constructor the app runs.
        Settings = new AppSettingsCoordinator(
            uiStateStore, folderPicker, ollamaModelCatalog, openRouterModelCatalog,
            anthropicModelCatalog, anthropicTokenStore, SetStatus, logger);

        Jobs.CollectionChanged += OnJobsCollectionChanged;

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
            enqueueJob: job => DispatchToUi(() => Jobs.Insert(0, job)),
            runJob: RunBatchJobAsync,
            setStatus: SetStatus,
            addAsInputAsync: InputImages.AddAsInputAsync,
            mutateFromImageAsync: path => MutateFromImageAsync(path));

        // Free the ComfyUI GPU once per batch (a per-job free reloads the checkpoint every time),
        // before releasing the batch's GPU lease.
        Batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchCoordinator.IsBatchRunning) && !Batch.IsBatchRunning)
                _ = CompleteBatchGpuAsync();
        };

        AttachParameters(_parameters);

        ValidateParameters();

        // Display order of the token Picker. Only Replicate and Pollinations have a parameters
        // slot; the other services read their store per request. Callbacks write through the
        // Parameters property, never the field, so a replaced instance gets the value.
        TokenProviders.Add(new TokenProviderViewModel(
            key: "replicate",
            displayName: "Replicate",
            placeholder: "Paste your Replicate API token…",
            helperText: "Required for Replicate-hosted models (Flux, GPT, Nano Banana). Stored in OS secure storage.",
            store: tokenStore,
            syncToParameters: v => Parameters.ApiToken = v));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "pollinations",
            displayName: "Pollinations",
            placeholder: "Paste your Pollinations token (optional)…",
            helperText: "Optional. Anonymous mode is rate-limited (1 req / 15 s). Register at auth.pollinations.ai for higher limits.",
            store: pollinationsTokenStore,
            syncToParameters: v => Parameters.PollinationsApiToken = v));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "comfyui",
            displayName: "ComfyUI",
            placeholder: "Authorization header value (e.g. Bearer abc123)…",
            helperText: "Optional. Sent verbatim as the Authorization header on every ComfyUI request "
                        + "(HTTP + progress WebSocket) — for reverse-proxied servers. Leave empty on a LAN.",
            store: comfyUiAuthStore,
            syncToParameters: static _ => { }));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "civitai",
            displayName: "CivitAI",
            placeholder: "Paste your CivitAI API key…",
            helperText: "Optional. Needed only for \"Post to CivitAI\". Create a Full API key at "
                        + "civitai.com/user/account (a free account works). Stored in OS secure storage.",
            store: civitaiTokenStore,
            syncToParameters: static _ => { }));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "anthropic",
            displayName: "Anthropic",
            placeholder: "Paste your Anthropic API key…",
            helperText: "Required when \"Describe an idea…\" or AI tools use a Claude tier. Create a key at "
                        + "console.anthropic.com. Stored in OS secure storage.",
            store: anthropicTokenStore,
            syncToParameters: static _ => { }));
        if (openRouterTokenStore is not null)
        {
            TokenProviders.Add(new TokenProviderViewModel(
                key: "openrouter",
                displayName: "OpenRouter",
                placeholder: "Paste your OpenRouter API key…",
                helperText: "Required when image observation uses OpenRouter. The model id is configured below.",
                store: openRouterTokenStore,
                syncToParameters: static _ => { }));
        }
        _selectedTokenProvider = TokenProviders[0];
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
        var missing = new List<string>();
        if (!TokenlessModel(Parameters.Model) && string.IsNullOrWhiteSpace(Parameters.ApiToken)) missing.Add("API Token");
        if (string.IsNullOrWhiteSpace(Parameters.Prompt)) missing.Add("Prompt");
        if (missing.Count > 0)
        {
            SetStatus($"Missing required field(s): {string.Join(", ", missing)}.", StatusKind.Error);
            return;
        }

        if (Capabilities.JsonPromptEditor && Parameters.UseJsonPrompt
            && JsonErrorDetail(Parameters.Prompt) is { } jsonError)
        {
            SetStatus($"Structured JSON prompt is not valid JSON: {jsonError}", StatusKind.Error);
            return;
        }

        if (Parameters.RandomizeSeed)
            Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);

        var snapshot = Parameters.Clone();
        var job = new GenerationJob(snapshot, AddAsInputAsync, path => MutateFromImageAsync(path));
        Jobs.Insert(0, job);

        // JobCount makes an unintended extra run visible in the log (e.g. a focused button re-fired by Space).
        _logger.LogInformation(
            "Generate activated Model={Model} Seed={Seed} JobCount={Count}",
            snapshot.Model, snapshot.Seed, Jobs.Count);

        _ = FlashAsync("Generation started");

        await RunJobAsync(job);
    }

    private Task RunBatchJobAsync(GenerationJob job) => RunJobAsync(job, isBatchJob: true);

    private async Task RunJobAsync(GenerationJob job, bool isBatchJob = false)
    {
        // A ComfyUI render and local Ollama work share one GPU's VRAM. Hold the gate from before
        // submit until after the VRAM free. Other providers and split-host setups skip it.
        var isComfyJob = ModelConstants.ComfyUi.IsId(job.Parameters.Model);
        var gpuGated = _gpuGate is not null
            && isComfyJob
            && GpuColocation.SameHost(Settings.ComfyUiBaseUrl, Settings.OllamaBaseUrl);

        IDisposable? singleJobLease = null;
        if (gpuGated)
        {
            try
            {
                if (isBatchJob)
                {
                    if (_batchGpuLease is null)
                    {
                        _batchGpuLease = await _gpuGate!.AcquireAsync(job.Cts.Token);
                        DispatchToUi(() => IsGpuBusy = true);
                    }
                    _batchComfyModel = job.Parameters.Model;
                }
                else
                {
                    singleJobLease = await _gpuGate!.AcquireAsync(job.Cts.Token);
                    DispatchToUi(() => IsGpuBusy = true);
                }
            }
            catch (OperationCanceledException)
            {
                // Canceled while queued behind another GPU workload; nothing rendered.
                MarkCanceled(job);
                DispatchToUi(() =>
                {
                    job.IsRunning = false;
                    job.HasProgress = false;
                });
                job.Cts.Dispose();
                return;
            }
        }
        else if (isBatchJob && isComfyJob)
        {
            // Remember the batch's model so CompleteBatchGpuAsync frees VRAM even if the
            // picker changes mid-batch.
            _batchComfyModel = job.Parameters.Model;
        }

        try
        {
            _logger.LogInformation(
                "Job started Model={Model} Seed={Seed}", job.Parameters.Model, job.Parameters.Seed);

            // ComfyUI sampler progress. A report arriving after the outcome must not overwrite it.
            var progress = new Progress<JobProgress>(p => DispatchToUi(() =>
            {
                if (!job.IsRunning) return;
                job.StatusMessage = p.Message;
                if (p.Percent is { } pct)
                {
                    job.Progress = pct;
                    job.HasProgress = true;
                }
            }));

            var outcome = await _jobRunner.RunAsync(job.Parameters, job.Cts.Token, progress);

            _logger.LogInformation(
                "Job finished Kind={Kind} Path={Path}", outcome.Kind, outcome.SavedPath);

            // MAUI drops PropertyChanged raised off the UI thread.
            DispatchToUi(() =>
            {
                job.ResultPath = outcome.SavedPath;
                if (outcome.SavedPath is not null)
                {
                    // Exactly one card is featured: the newest saved one.
                    if (LatestCompletedJob is not null) LatestCompletedJob.IsFeatured = false;
                    job.IsFeatured = true;
                    LatestCompletedJob = job;

                    // Before the startup count finishes there is nothing to increment.
                    if (TotalImagesGenerated is { } n) TotalImagesGenerated = n + 1;
                }
                job.StatusMessage = outcome.Message;
                job.StatusKind = outcome.Kind switch
                {
                    JobOutcomeKind.Saved => StatusKind.Success,
                    // The runner reports a cancel during polling as Failed with a "canceled" message.
                    _ when outcome.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
                        => StatusKind.Canceled,
                    _ => StatusKind.Error
                };
            });

            // Fire-and-forget: a slow upload must not delay the next batch job, and its failure
            // never changes the job outcome.
            if (outcome is { Kind: JobOutcomeKind.Saved, SavedPath: not null }
                && job.Parameters.PostToCivitai)
            {
                _ = PostJobToCivitaiAsync(job, outcome.SavedPath);
            }
        }
        catch (OperationCanceledException)
        {
            MarkCanceled(job);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job finished Kind=Threw Model={Model}", job.Parameters.Model);
            var msg = $"Error: {ex.Message}";
            DispatchToUi(() =>
            {
                job.StatusMessage = msg;
                job.StatusKind = StatusKind.Error;
            });
        }
        finally
        {
            DispatchToUi(() =>
            {
                job.IsRunning = false;
                job.HasProgress = false;
            });
            job.Cts.Dispose();
        }

        // Batch jobs keep the lease until CompleteBatchGpuAsync runs at batch end.
        if (isBatchJob) return;

        // Free VRAM before releasing the gate, so queued GPU work gets a freed card.
        try
        {
            await MaybeFreeComfyUiVramAsync(job.Parameters.Model);
        }
        finally
        {
            if (gpuGated) DispatchToUi(() => IsGpuBusy = false);
            singleJobLease?.Dispose();
        }
    }

    private void MarkCanceled(GenerationJob job)
    {
        _logger.LogInformation("Job finished Kind=Canceled Path=(null)");
        DispatchToUi(() =>
        {
            job.StatusMessage = "Canceled.";
            job.StatusKind = StatusKind.Canceled;
        });
    }

    private async Task CompleteBatchGpuAsync()
    {
        var lease = _batchGpuLease;
        var model = _batchComfyModel;
        _batchGpuLease = null;
        _batchComfyModel = null;

        if (lease is null && model is null) return;

        try
        {
            if (model is not null)
                await MaybeFreeComfyUiVramAsync(model);
        }
        finally
        {
            if (lease is not null)
            {
                DispatchToUi(() => IsGpuBusy = false);
                lease.Dispose();
            }
        }
    }

    /// <summary>
    /// Frees ComfyUI's GPU memory if "Free GPU memory after rendering" is on. No-op for other
    /// models, during a batch, or without a VRAM service. The service swallows failures.
    /// </summary>
    internal async Task MaybeFreeComfyUiVramAsync(string model)
    {
        if (!Settings.FreeVramAfterRendering
            || _comfyVram is null
            || Batch.IsBatchRunning
            || !ModelConstants.ComfyUi.IsId(model))
        {
            return;
        }

        await _comfyVram.TryFreeAsync();
    }

    internal async Task PostJobToCivitaiAsync(GenerationJob job, string savedPath)
    {
        DispatchToUi(() => job.CivitaiStatusMessage = "Posting to CivitAI…");
        try
        {
            var meta = job.Parameters.CivitaiIncludeMeta
                ? CivitaiMetaBuilder.Build(job.Parameters)
                : null;
            var modelVersionId = CivitaiModelReference.ParseVersionId(job.Parameters.CivitaiModelRef);
            // Text that parses to nothing is a typo, not a "no model" choice; say so.
            var refNotRecognized =
                !string.IsNullOrWhiteSpace(job.Parameters.CivitaiModelRef) && modelVersionId is null;

            var result = await _civitaiPostingService.PostImageAsync(
                savedPath, CivitaiTitleBuilder.Build(job.Prompt), meta, modelVersionId);

            _logger.LogInformation(
                "CivitAI post finished Success={Success} PostId={PostId} ModelVersionId={ModelVersionId}",
                result.Success, result.PostId, modelVersionId);
            DispatchToUi(() =>
            {
                job.CivitaiPostUrl = result.PostUrl;
                job.CivitaiStatusMessage = result.Success
                    ? refNotRecognized
                        ? $"{result.Message} Model reference not recognized — posted without model link."
                        : result.Message
                    : $"CivitAI post failed: {result.Message}";
            });
        }
        catch (Exception ex)
        {
            // The service returns failures as results; anything that still throws must not
            // escape a fire-and-forget continuation. The job stays Saved.
            _logger.LogWarning(ex, "CivitAI posting threw Path={Path}", savedPath);
            DispatchToUi(() => job.CivitaiStatusMessage = $"CivitAI post failed: {ex.Message}");
        }
    }

    [ObservableProperty]
    private string? _civitaiConnectionStatus;

    [RelayCommand]
    private async Task TestCivitaiConnectionAsync()
    {
        CivitaiConnectionStatus = "Testing…";
        var result = await _civitaiPostingService.TestConnectionAsync();
        CivitaiConnectionStatus = result.Message;
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

    /// <summary>Loads every provider token from secure storage, once.</summary>
    public async Task LoadAllTokensAsync()
    {
        if (_tokensLoaded) return;
        _tokensLoaded = true;

        foreach (var provider in TokenProviders)
        {
            await provider.LoadAsync();
        }
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

    // Called fire-and-forget from OnAppearing, so a large output folder never delays startup.
    public async Task LoadTotalImagesGeneratedAsync(CancellationToken ct = default)
    {
        try
        {
            var count = 0;
            await foreach (var _ in _galleryService.EnumerateAsync(ct)) count++;
            DispatchToUi(() => TotalImagesGenerated = count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Total-images count failed");
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

    private Task AddAsInputAsync(string filePath) => InputImages.AddAsInputAsync(filePath);
}
