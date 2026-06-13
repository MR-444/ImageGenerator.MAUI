using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.Services;
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
    private readonly ICivitaiPostingService _civitaiPostingService;
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IComfyUiCheckpointService _checkpointService;
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

    // The newest successfully saved job — drives the featured (large uncropped) queue card
    // and is the handle for un-featuring the previous newest. Only Saved outcomes update it
    // (see RunJobAsync); canceled/failed jobs never become the featured card.
    [ObservableProperty]
    private GenerationJob? _latestCompletedJob;

    // Non-secret server setting (first of its kind) — Preferences-backed, not a token store.
    // The ComfyUI generation service re-reads the store per request, so edits apply instantly.
    [ObservableProperty]
    private string _comfyUiBaseUrl = ModelConstants.ComfyUi.DefaultBaseUrl;

    partial void OnComfyUiBaseUrlChanged(string value) =>
        _uiStateStore.PersistComfyUiBaseUrl(value ?? string.Empty);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    [NotifyPropertyChangedFor(nameof(SupportsResolution))]
    [NotifyPropertyChangedFor(nameof(SupportsGptQuality))]
    [NotifyPropertyChangedFor(nameof(SupportsIdeogramOptions))]
    [NotifyPropertyChangedFor(nameof(SupportsJsonPromptEditor))]
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

    // ComfyUI checkpoint picker. Options[0] is always the workflow's own baked-in checkpoint
    // (= no patch); server checkpoints follow after the async fetch lands. Hidden whenever the
    // selected workflow has no literal CheckpointLoaderSimple ckpt_name.
    [ObservableProperty]
    private List<string> _checkpointOptions = [];

    [ObservableProperty]
    private string? _selectedCheckpoint;

    [ObservableProperty]
    private bool _supportsCheckpoint;

    // "Checkpoint" (CheckpointLoaderSimple) or "Diffusion model" (UNETLoader) — set per the
    // selected workflow's loader kind.
    [ObservableProperty]
    private string _checkpointLabel = "Checkpoint";

    private string? _workflowDefaultCheckpoint;
    private bool _suppressCheckpointPersist;
    // Monotonic token: a slower fetch for a previously selected model must not clobber the
    // options of the model selected after it.
    private int _checkpointRefreshVersion;

    partial void OnSelectedCheckpointChanged(string? value)
    {
        // The WinUI ComboBox pushes SelectedItem=null on ItemsSource swaps; ignore it like
        // OnSelectedModelChanged does — the real selection lands on a later tick.
        if (value is null) return;

        Parameters.ComfyUiCheckpoint =
            value == _workflowDefaultCheckpoint ? string.Empty : value;

        // Membership guard, same as resolution: only a value the picker actually offers can
        // be a user pick.
        if (!_suppressCheckpointPersist && CheckpointOptions.Contains(value))
        {
            _uiStateStore.PersistComfyUiCheckpoint(
                value, ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
        }
    }

    // ComfyUI quality-preset picker (the workflow's single CustomCombo node). Options come
    // from the file itself — option1..option4 — with the baked-in choice first (= no patch);
    // no server fetch. Hidden whenever the workflow has no unambiguous CustomCombo.
    [ObservableProperty]
    private List<string> _qualityPresetOptions = [];

    [ObservableProperty]
    private string? _selectedQualityPreset;

    [ObservableProperty]
    private bool _supportsQualityPreset;

    private string? _workflowDefaultPreset;
    private bool _suppressPresetPersist;
    // Monotonic token, same role as _checkpointRefreshVersion.
    private int _presetRefreshVersion;

    partial void OnSelectedQualityPresetChanged(string? value)
    {
        // Same WinUI null-push tolerance as OnSelectedCheckpointChanged.
        if (value is null) return;

        Parameters.ComfyUiPreset =
            value == _workflowDefaultPreset ? string.Empty : value;

        if (!_suppressPresetPersist && QualityPresetOptions.Contains(value))
        {
            _uiStateStore.PersistComfyUiPreset(
                value, ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
        }
    }

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
    // Structured-JSON checkbox + "Edit structure…" button: Ideogram V4 and ComfyUI workflow
    // models (whose graphs consume the caption JSON) both set this.
    public bool SupportsJsonPromptEditor => Capabilities.JsonPromptEditor;

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
        // ItemsSource makes the two-way-bound Picker push SelectedItem=null into
        // Parameters.Resolution synchronously whenever the old value isn't in the new list.
        // Outside the window that push looked user-driven and persisted null — deleting the
        // saved resolution before LoadSavedUiState could read it. (The model picker survives
        // the same push only because OnSelectedModelChanged ignores null.) Exactly ONE Picker
        // may bind Parameters.Resolution: two bound at once (2026-06-11, hidden Ideogram +
        // shared) livelocked the UI thread in a synchronous null/value ping-pong here.
        _suppressResolutionPersist = true;
        try
        {
            // Capture BEFORE the swap: the Picker's synchronous null push lands in
            // Parameters.Resolution during the assignment below, so reading it afterwards
            // would always see null and slam a still-valid choice to the first option.
            var previousResolution = Parameters.Resolution;
            ResolutionOptions = caps.Resolutions?.ToList() ?? [];
            if (ResolutionOptions.Count > 0)
            {
                // Sticky resolution, mirroring the AR logic above: keep the current value if
                // the new model offers it, else fall back to the persisted user choice (of the
                // NEW model's option family), else the model's first option.
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

        // Models that hide the output-format picker (Ideogram) only emit PNG — pin the save format.
        if (!caps.OutputFormatSelectable) Parameters.OutputFormat = ImageOutputFormat.Png;
        // Clear the structured-JSON toggle when leaving a JSON-capable model (Ideogram V4 /
        // ComfyUI workflows) so a stale flag can't gate validation (or alter Build) on a model
        // that has no such field. Suppressed from persistence: this is a capability
        // consequence, not a user preference change.
        if (!caps.JsonPromptEditor)
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

        // Fire-and-forget: the checkpoint picker hydrates asynchronously (file probe + server
        // fetch) while the synchronous capability swap above stays instant. The method owns
        // its try/catch and guards against stale completions via _checkpointRefreshVersion.
        _ = RefreshCheckpointOptionsAsync(modelValue);
        // Same shape for the quality-preset picker — file probe only, no server fetch.
        _ = RefreshQualityPresetOptionsAsync(modelValue);
    }

    internal async Task RefreshCheckpointOptionsAsync(string? modelValue)
    {
        var version = ++_checkpointRefreshVersion;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue))
            {
                HideCheckpointPicker();
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var slot = await _checkpointService.GetWorkflowModelSlotAsync(workflowName);
            if (version != _checkpointRefreshVersion) return;
            if (slot is null)
            {
                // Not an error: link-driven loaders, or a multi-UNET pairing that must never
                // be half-swapped — but say so, or a hidden picker looks like a bug.
                _logger.LogInformation(
                    "Checkpoint picker hidden: workflow {Workflow} has no swappable model loader "
                    + "(needs CheckpointLoaderSimple or exactly one UNETLoader with a literal name)",
                    workflowName);
                HideCheckpointPicker();
                return;
            }
            var baked = slot.BakedName;

            // Show the default-only picker immediately — the server fetch below may take
            // seconds (offline host) and the workflow default is always a valid choice.
            DispatchToUi(() =>
            {
                _suppressCheckpointPersist = true;
                try
                {
                    _workflowDefaultCheckpoint = baked;
                    CheckpointOptions = [baked];
                    SelectedCheckpoint = baked;
                    CheckpointLabel = slot.Kind == ComfyUiLoaderKind.Unet ? "Diffusion model" : "Checkpoint";
                    SupportsCheckpoint = true;
                }
                finally { _suppressCheckpointPersist = false; }
            });

            var server = await _checkpointService.GetModelNamesAsync(slot.Kind);
            if (version != _checkpointRefreshVersion) return;
            if (server is null || server.Count == 0) return;

            DispatchToUi(() =>
            {
                _suppressCheckpointPersist = true;
                try
                {
                    CheckpointOptions =
                        [baked, .. server.Where(n => !string.Equals(n, baked, StringComparison.Ordinal))];
                    // Restore a previous explicit pick for THIS workflow — but only when the
                    // server still offers it; never invent a selection.
                    var saved = _uiStateStore.LoadComfyUiCheckpoint(workflowName);
                    SelectedCheckpoint =
                        saved is not null && CheckpointOptions.Contains(saved) ? saved : baked;
                }
                finally { _suppressCheckpointPersist = false; }
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Checkpoint options refresh failed Model={Model}", modelValue);
        }
    }

    private void HideCheckpointPicker() => DispatchToUi(() =>
    {
        _suppressCheckpointPersist = true;
        try
        {
            SupportsCheckpoint = false;
            CheckpointOptions = [];
            _workflowDefaultCheckpoint = null;
            SelectedCheckpoint = null;
            CheckpointLabel = "Checkpoint";
            // A stale checkpoint must not linger in Clone() snapshots of other models.
            Parameters.ComfyUiCheckpoint = string.Empty;
        }
        finally { _suppressCheckpointPersist = false; }
    });

    internal async Task RefreshQualityPresetOptionsAsync(string? modelValue)
    {
        var version = ++_presetRefreshVersion;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue))
            {
                HideQualityPresetPicker();
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var slot = await _checkpointService.GetWorkflowQualityPresetSlotAsync(workflowName);
            if (version != _presetRefreshVersion) return;
            if (slot is null)
            {
                // Not an error — most workflows simply have no (single) CustomCombo. Say so,
                // or a hidden picker looks like a bug.
                _logger.LogInformation(
                    "Preset picker hidden: workflow {Workflow} does not have exactly one "
                    + "CustomCombo node with a literal choice",
                    workflowName);
                HideQualityPresetPicker();
                return;
            }
            var baked = slot.BakedChoice;

            // Single UI update — the options live in the file, so unlike the checkpoint
            // picker there is no async second phase to wait for.
            DispatchToUi(() =>
            {
                _suppressPresetPersist = true;
                try
                {
                    _workflowDefaultPreset = baked;
                    QualityPresetOptions =
                        [baked, .. slot.Options.Where(o => !string.Equals(o, baked, StringComparison.Ordinal))];
                    // Restore a previous explicit pick for THIS workflow — but only when the
                    // workflow still offers it; never invent a selection.
                    var saved = _uiStateStore.LoadComfyUiPreset(workflowName);
                    SelectedQualityPreset =
                        saved is not null && QualityPresetOptions.Contains(saved) ? saved : baked;
                    SupportsQualityPreset = true;
                }
                finally { _suppressPresetPersist = false; }
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Quality-preset options refresh failed Model={Model}", modelValue);
        }
    }

    private void HideQualityPresetPicker() => DispatchToUi(() =>
    {
        _suppressPresetPersist = true;
        try
        {
            SupportsQualityPreset = false;
            QualityPresetOptions = [];
            _workflowDefaultPreset = null;
            SelectedQualityPreset = null;
            // A stale preset must not linger in Clone() snapshots of other models.
            Parameters.ComfyUiPreset = string.Empty;
        }
        finally { _suppressPresetPersist = false; }
    });

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
        // Pollinations works anonymously and ComfyUI is the user's own server, so their tokens
        // are optional/nonexistent. Replicate requires its own Bearer token. Prompt is
        // required everywhere.
        var tokenOk = TokenlessModel(Parameters.Model)
            || !string.IsNullOrWhiteSpace(Parameters.ApiToken);
        // In structured-JSON mode (Ideogram V4 / ComfyUI) the prompt box must contain valid JSON.
        var jsonModeActive = Capabilities.JsonPromptEditor && Parameters.UseJsonPrompt;
        var jsonError = jsonModeActive ? JsonErrorDetail(Parameters.Prompt) : null;
        var jsonOk = !jsonModeActive || jsonError is null;
        JsonPromptStateText = jsonModeActive
            ? jsonError is null ? "Structured JSON: valid ✓" : $"Structured JSON: not valid — {jsonError}"
            : null;
        IsValid = tokenOk && !string.IsNullOrWhiteSpace(Parameters.Prompt) && jsonOk;
    }

    private static bool TokenlessModel(string? model) =>
        ModelConstants.Pollinations.IsId(model) || ModelConstants.ComfyUi.IsId(model);

    // null = valid JSON. Otherwise a short human-readable reason, with line/position when
    // available, so the user can find the break in a 1700-char compact blob (the 2026-06-11
    // missing-root-brace hunt: "not valid JSON" alone cost a long checkbox-toggle session).
    private static string? JsonErrorDetail(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "prompt is empty";
        try { using var _ = JsonDocument.Parse(text); return null; }
        catch (JsonException ex)
        {
            // ex.Message ends with " LineNumber: 0 | BytePositionInLine: 1792." — redundant
            // with the friendlier suffix below, so cut it.
            var msg = ex.Message;
            var cut = msg.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut > 0) msg = msg[..cut].TrimEnd();
            return ex.LineNumber is { } line && ex.BytePositionInLine is { } pos
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

    public GeneratorViewModel(
        IJobRunner jobRunner,
        IApiTokenStore tokenStore,
        IPollinationsTokenStore pollinationsTokenStore,
        IComfyUiAuthStore comfyUiAuthStore,
        ICivitaiTokenStore civitaiTokenStore,
        ICivitaiPostingService civitaiPostingService,
        IUiStateStore uiStateStore,
        IModelCatalogCoordinator catalogCoordinator,
        IModelDescriptorRegistry registry,
        IPromptBatchParser promptBatchParser,
        IComfyUiCheckpointService checkpointService,
        ILogger<GeneratorViewModel> logger)
    {
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _pollinationsTokenStore = pollinationsTokenStore ?? throw new ArgumentNullException(nameof(pollinationsTokenStore));
        if (comfyUiAuthStore is null) throw new ArgumentNullException(nameof(comfyUiAuthStore));
        if (civitaiTokenStore is null) throw new ArgumentNullException(nameof(civitaiTokenStore));
        _civitaiPostingService = civitaiPostingService ?? throw new ArgumentNullException(nameof(civitaiPostingService));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _catalogCoordinator = catalogCoordinator ?? throw new ArgumentNullException(nameof(catalogCoordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _checkpointService = checkpointService ?? throw new ArgumentNullException(nameof(checkpointService));
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
                    // the Picker only offers list members.
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
                        _uiStateStore.PersistResolution(_parameters.Resolution!, _parameters.Model);
                    }
                    break;
                case nameof(ImageGenerationParameters.CivitaiModelRef):
                    // Persist unconditionally: the launch restore writes the value just loaded,
                    // which the store's skip-identical rule turns into a no-op.
                    _uiStateStore.PersistCivitaiModelRef(_parameters.CivitaiModelRef ?? string.Empty);
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
        // No parameters slot: the ComfyUI services read the store directly per run (the
        // checkpoint fetch has no ImageGenerationParameters at all), and ComfyUI must stay
        // tokenless for validation — see TokenlessModel.
        TokenProviders.Add(new TokenProviderViewModel(
            key: "comfyui",
            displayName: "ComfyUI",
            placeholder: "Authorization header value (e.g. Bearer abc123)…",
            helperText: "Optional. Sent verbatim as the Authorization header on every ComfyUI request "
                        + "(HTTP + progress WebSocket) — for reverse-proxied servers. Leave empty on a LAN.",
            store: comfyUiAuthStore,
            syncToParameters: static _ => { }));
        // No parameters slot either: CivitaiPostingService reads the store per request (posting
        // happens after the job's parameter snapshot, and Test connection has no parameters).
        TokenProviders.Add(new TokenProviderViewModel(
            key: "civitai",
            displayName: "CivitAI",
            placeholder: "Paste your CivitAI API key…",
            helperText: "Optional. Needed only for \"Post to CivitAI\". Create a Full API key at "
                        + "civitai.com/user/account (a free account works). Stored in OS secure storage.",
            store: civitaiTokenStore,
            syncToParameters: static _ => { }));
        _selectedTokenProvider = TokenProviders[0];
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task GenerateImageAsync()
    {
        var missing = new List<string>();
        // Mirror ValidateParameters' token rule: Pollinations/ComfyUI generate without one.
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
        var job = new GenerationJob(snapshot, AddAsInputAsync);
        Jobs.Insert(0, job);

        // Activation audit: a generate run used to be invisible in app.log until the service's
        // first line, which made the 2026-06-10 "phantom second generation" unattributable.
        // JobCount exposes unnoticed extra cards (e.g. a focused-button Space/Enter re-fire).
        _logger.LogInformation(
            "Generate activated Model={Model} Seed={Seed} JobCount={Count}",
            snapshot.Model, snapshot.Seed, Jobs.Count);

        // Click-time feedback: generation can take minutes, so confirm the click before
        // the job-row spinner is the only visible signal.
        _ = FlashAsync("Generation started");

        await RunJobAsync(job);
    }

    private async Task RunJobAsync(GenerationJob job)
    {
        try
        {
            // Start/finish lines bracket BOTH single and batch runs (shared chokepoint).
            _logger.LogInformation(
                "Job started Model={Model} Seed={Seed}", job.Parameters.Model, job.Parameters.Seed);

            // Live sampler progress (ComfyUI ws). The IsRunning guard drops any report that
            // arrives after the outcome landed — a late ws frame must not overwrite the final
            // status text.
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

            // HttpClient continuations can land on a ThreadPool thread; MAUI drops
            // PropertyChanged fired off the UI thread, so marshal the update back.
            DispatchToUi(() =>
            {
                job.ResultPath = outcome.SavedPath;
                if (outcome.SavedPath is not null)
                {
                    // Move the "featured" crown from the previous newest to this one so exactly
                    // one card shows the large uncropped preview.
                    if (LatestCompletedJob is not null) LatestCompletedJob.IsFeatured = false;
                    job.IsFeatured = true;
                    LatestCompletedJob = job;
                }
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

            // Post-save side effect, deliberately fire-and-forget: a slow CivitAI upload must
            // not delay the next batch job, and its failure must never touch the job outcome
            // above. Internal method so tests can await it directly.
            if (outcome.Kind == JobOutcomeKind.Saved
                && outcome.SavedPath is not null
                && job.Parameters.PostToCivitai)
            {
                _ = PostJobToCivitaiAsync(job, outcome.SavedPath);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Job finished Kind=Canceled Path=(null)");
            DispatchToUi(() =>
            {
                job.StatusMessage = "Canceled.";
                job.StatusKind = StatusKind.Canceled;
            });
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
            // Non-empty text that parses to nothing is a typo, not a "no model" choice — say so
            // instead of silently posting to the profile.
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
            // Belt-and-braces: the service already returns failures as results; whatever still
            // throws (a bug, an unexpected cancellation) must not take the app down from a
            // fire-and-forget continuation — and the job stays Saved either way.
            _logger.LogWarning(ex, "CivitAI posting threw Path={Path}", savedPath);
            DispatchToUi(() => job.CivitaiStatusMessage = $"CivitAI post failed: {ex.Message}");
        }
    }

    // Post title from the prompt: collapse whitespace, cut at a word boundary near 60 chars.
    // Structured-JSON prompts (Ideogram / ComfyUI) would otherwise yield a raw
    // '{"high_level_description":…' blob as the title — extract the human description field
    // instead; when none is usable, return empty and the service omits the title entirely.
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
    private async Task OpenSettingsAsync()
    {
        // Same Shell.Current rationale as OpenGalleryAsync above.
        try
        {
            await Shell.Current.GoToAsync("settings");
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open Settings: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private async Task OpenIdeogramEditorAsync()
    {
        // Hand the current prompt box content to the editor so an applied structured prompt
        // round-trips (re-open → same boxes). Same Shell.Current guard as OpenGalleryAsync.
        try
        {
            await Shell.Current.GoToAsync(BuildEditorRoute());
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open the structure editor: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>
    /// The editor hand-off route. On a ComfyUI workflow the output shape lives in
    /// Parameters.AspectRatio (ResolutionSelector combo string), so that is what seeds the
    /// editor's picker; everywhere else it's the resolution. Internal: tests pin the shape
    /// without Shell, mirroring the editor's ApplyToGenerator seam.
    /// </summary>
    internal string BuildEditorRoute()
    {
        var json = Uri.EscapeDataString(Parameters.Prompt ?? string.Empty);
        var shape = ModelConstants.ComfyUi.IsId(Parameters.Model)
            ? Parameters.AspectRatio
            : Parameters.Resolution;
        return $"ideogram-editor?json={json}&resolution={Uri.EscapeDataString(shape ?? string.Empty)}";
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

        // Restore the structured-JSON toggle LAST: the model restore above has settled
        // Capabilities, and the toggle only means anything on a JSON-capable model (Ideogram /
        // ComfyUI) — restoring it blindly would re-check the box on a model whose Build has no
        // json_prompt field. Suppress persistence: a restore isn't a user preference change.
        if (Capabilities.JsonPromptEditor && _uiStateStore.LoadUseJsonPrompt())
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
