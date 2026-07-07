using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
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

public partial class GeneratorViewModel : ObservableObject, IStatusOwner
{
    private readonly IJobRunner _jobRunner;
    private readonly IApiTokenStore _tokenStore;
    private readonly IPollinationsTokenStore _pollinationsTokenStore;
    private readonly ICivitaiPostingService _civitaiPostingService;
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IComfyUiCheckpointService _checkpointService;
    private readonly IGalleryService _galleryService;
    private readonly IFolderPicker _folderPicker;
    private readonly IOllamaModelCatalog? _ollamaModelCatalog;
    private readonly IComfyUiVramService? _comfyVram;
    private readonly IGpuGate? _gpuGate;
    private readonly IOpenRouterTokenStore? _openRouterTokenStore;
    private readonly IOpenRouterModelCatalog? _openRouterModelCatalog;
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

    // True while the Mutation engine's AI fan-out is in flight. The mutation VM (a singleton holding
    // this one) flips it so MainPage can show an in-progress banner if the user leaves that page —
    // the LLM calls happen before the batch's own job cards appear.
    [ObservableProperty]
    private bool _isAiMutationRunning;

    [ObservableProperty]
    private string? _aiMutationStatus;

    // True while either this VM's own ComfyUI render or the mutation engine's local-Ollama tier
    // holds the shared single-GPU gate (see GpuGate). Pushed directly from both acquire/release
    // sites — mirrors the IsAiMutationRunning cross-VM push above, no new gate-side plumbing.
    [ObservableProperty]
    private bool _isGpuBusy;

    // All-time count of images in the output folder, shown in the header. Null until the
    // background enumeration in LoadTotalImagesGeneratedAsync completes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalImagesGeneratedText))]
    private int? _totalImagesGenerated;

    public string TotalImagesGeneratedText =>
        TotalImagesGenerated is { } n ? $"{n} image{(n == 1 ? "" : "s")}" : "…";

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;

    // Gates the "Clear finished jobs" command. Recomputed from the live collection whenever a
    // job is added/removed or transitions to a terminal state (see the CollectionChanged +
    // per-job PropertyChanged wiring in the ctor).
    public bool HasFinishedJobs => Jobs.Any(j => j.IsFinished);

    // Compact header summary, e.g. "2 running · 1 queued · 5 done". A "queued" batch job is
    // !IsRunning with StatusKind still at the default Info (see GenerationJob.IsFinished comment) —
    // recomputed from the same hooks that already track HasFinishedJobs.
    public string JobStatusSummary
    {
        get
        {
            var running = Jobs.Count(j => j.IsRunning);
            var queued = Jobs.Count(j => !j.IsRunning && j.StatusKind == StatusKind.Info);
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

    // Compact header summary, e.g. "Replicate · flux-dev".
    public string SelectedModelSummary =>
        ProviderFilter.SelectedModel is { } model
            ? $"{ProviderFilter.SelectedProvider} · {model.Display}"
            : ProviderFilter.SelectedProvider;

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

    // When on, POST /free to ComfyUI once rendering is idle so local Ollama prompt/AI work (and the OS) get
    // the VRAM back. Default on; turn off to keep the checkpoint resident for faster single-image iteration.
    [ObservableProperty]
    private bool _freeVramAfterRendering = true;

    partial void OnFreeVramAfterRenderingChanged(bool value) =>
        _uiStateStore.PersistFreeVramAfterRendering(value);

    // Color theme selection for the Settings "Appearance" picker. Bound as an int so the picker's
    // SelectedIndex maps straight onto the AppTheme enum (0=Unspecified/System, 1=Light, 2=Dark) with
    // no value converter. Persists the pick and applies it live to Application.UserAppTheme; the whole
    // UI is already AppThemeBinding-driven so it repaints immediately. Loaded at VM init below.
    [ObservableProperty]
    private int _appThemeIndex; // 0=System, 1=Light, 2=Dark

    partial void OnAppThemeIndexChanged(int value)
    {
        var theme = (AppTheme)value;
        _uiStateStore.PersistAppTheme(theme);
        ApplyAppTheme(theme);
    }

    // Applying UserAppTheme touches the visual tree, so marshal to the UI thread — the change hook can
    // fire off it. No-op when there's no live Application (unit tests construct the VM headless).
    private static void ApplyAppTheme(AppTheme theme)
    {
        if (Application.Current is { } app)
            app.Dispatcher.Dispatch(() => app.UserAppTheme = theme);
    }

    // The local Ollama server + model for the AI caption mutator's free "Local" tier. Preferences-backed
    // like the ComfyUI URL; the mutation service re-reads the store per request, so edits apply instantly.
    [ObservableProperty]
    private string _ollamaBaseUrl = ModelConstants.Ollama.DefaultBaseUrl;

    partial void OnOllamaBaseUrlChanged(string value) =>
        _uiStateStore.PersistOllamaBaseUrl(value ?? string.Empty);

    [ObservableProperty]
    private string _ollamaModel = ModelConstants.Ollama.DefaultModel;

    partial void OnOllamaModelChanged(string value) =>
        _uiStateStore.PersistOllamaModel(value ?? string.Empty);

    [ObservableProperty]
    private string _ollamaVisionModel = ModelConstants.Ollama.DefaultModel;

    partial void OnOllamaVisionModelChanged(string value) =>
        _uiStateStore.PersistOllamaVisionModel(value ?? string.Empty);

    [ObservableProperty]
    private string _openRouterVisionModel = string.Empty;

    partial void OnOpenRouterVisionModelChanged(string value) =>
        _uiStateStore.PersistOpenRouterVisionModel(value ?? string.Empty);

    [ObservableProperty]
    private bool _openRouterVisionFreeOnly = true;

    partial void OnOpenRouterVisionFreeOnlyChanged(bool value)
    {
        _uiStateStore.PersistOpenRouterVisionFreeOnly(value);
        OpenRouterVisionModels.Clear();
        OpenRouterVisionModel = string.Empty;
    }

    public ObservableCollection<string> OpenRouterVisionModels { get; } = [];

    [RelayCommand]
    private async Task RefreshOpenRouterVisionModelsAsync()
    {
        if (_openRouterModelCatalog is null) return;
        try
        {
            var models = await _openRouterModelCatalog.ListVisionModelsAsync(OpenRouterVisionFreeOnly);
            var current = OpenRouterVisionModel;

            OpenRouterVisionModels.Clear();
            foreach (var model in models)
                OpenRouterVisionModels.Add(model.Id);

            if (!string.IsNullOrWhiteSpace(current) && !OpenRouterVisionModels.Contains(current))
                OpenRouterVisionModel = string.Empty;

            SetStatus(models.Count > 0
                ? $"Found {models.Count} OpenRouter vision model(s){(OpenRouterVisionFreeOnly ? " with free pricing." : ".")}"
                : "No OpenRouter vision models matched the current filter.",
                models.Count > 0 ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list OpenRouter vision models");
            SetStatus($"Couldn't refresh OpenRouter models: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Installed Ollama models for the Settings picker; filled by <see cref="RefreshOllamaModelsCommand"/>.</summary>
    public ObservableCollection<string> OllamaModels { get; } = [];

    /// <summary>Installed Ollama models that advertise both vision and completion capabilities.</summary>
    public ObservableCollection<string> OllamaVisionModels { get; } = [];

    /// <summary>Pull the model list from the configured Ollama server into the picker.</summary>
    [RelayCommand]
    private async Task RefreshOllamaModelsAsync()
    {
        if (_ollamaModelCatalog is null) return;
        try
        {
            var modelInfos = await _ollamaModelCatalog.ListModelInfosAsync(OllamaBaseUrl);
            var models = modelInfos.Select(m => m.Name).ToArray();
            var visionModels = modelInfos
                .Where(m => m.SupportsCompletion && m.SupportsVision)
                .Select(m => m.Name)
                .ToArray();

            var current = OllamaModel;
            var currentVision = OllamaVisionModel;
            OllamaModels.Clear();
            foreach (var m in models) OllamaModels.Add(m);
            OllamaVisionModels.Clear();
            foreach (var m in visionModels) OllamaVisionModels.Add(m);
            // Preserve the saved selection even if the server doesn't list it (e.g. typo / not pulled yet).
            if (!string.IsNullOrWhiteSpace(current) && !OllamaModels.Contains(current))
                OllamaModels.Add(current);
            if (!string.IsNullOrWhiteSpace(currentVision) && !OllamaVisionModels.Contains(currentVision))
                OllamaVisionModels.Add(currentVision);

            SetStatus(models.Length > 0
                ? $"Found {models.Length} Ollama model(s), {visionModels.Length} with vision."
                : "No models installed on the Ollama server.", models.Length > 0 ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list Ollama models from {Url}", OllamaBaseUrl);
            SetStatus($"Couldn't reach Ollama at {OllamaBaseUrl}: {ex.Message}", StatusKind.Error);
        }
    }

    // The configurable ROOT data folder (images, json-prompts, comfy-workflows, mutation-library
    // and prompt-builder all live under it). Preferences-backed like the ComfyUI URL; the change
    // hook applies the override to OutputPaths so saves, the gallery and "Open output folder"
    // follow it live. Defaults to the fixed Pictures\Emberforge location for display until the
    // user picks another.
    [ObservableProperty]
    private string _outputFolder = OutputPaths.DefaultRootDirectory;

    partial void OnOutputFolderChanged(string value)
    {
        var trimmed = value?.Trim();
        _uiStateStore.PersistOutputFolder(trimmed ?? string.Empty);
        OutputPaths.SetRootOverride(trimmed);
    }

    [RelayCommand]
    private async Task BrowseOutputFolderAsync()
    {
        try
        {
            var picked = await _folderPicker.PickFolderAsync(OutputFolder);
            // Null => the user cancelled; leave the current value untouched.
            if (!string.IsNullOrWhiteSpace(picked))
                OutputFolder = picked;
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open the folder picker: {ex.Message}", StatusKind.Error);
        }
    }

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

    // Mirrors _suppressResolutionPersist for the AspectRatio Picker. The AR options swap in
    // RefreshCapabilities makes the bound Picker push a VALID first/default option (not null like
    // resolution), so the membership guard alone can't tell it from a user pick — this flag gates
    // persistence across the swap so a binding artifact never overwrites the saved choice.
    private bool _suppressAspectRatioPersist;

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

        // Display-only: record the resolved model name (default OR picked) so the job card
        // can show which ComfyUI model produced the image, not just the workflow filename.
        Parameters.ComfyUiModelDisplay = value;

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

        // Display-only: record the resolved preset (default OR picked) so the metadata and job
        // card report which preset rendered the image. Unlike ComfyUiPreset (empty = no patch),
        // this is set even when the pick equals the baked default — mirrors ComfyUiModelDisplay.
        Parameters.ComfyUiPresetDisplay = value;

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
        // The options swap must sit INSIDE the persist-suppression window, exactly like the
        // resolution swap below: replacing the ItemsSource makes the two-way-bound AR Picker push
        // a VALID first/default option into Parameters.AspectRatio synchronously when the old value
        // isn't in the new list. Outside the window that push (where _suppressPreferredArUpdate is
        // false) looked user-driven and clobbered the saved AR before LoadSavedUiState could
        // restore it. Unlike resolution the push is a valid string, so the membership guard alone
        // isn't enough — _suppressAspectRatioPersist gates it.
        _suppressAspectRatioPersist = true;
        try
        {
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
        }
        finally { _suppressAspectRatioPersist = false; }

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
            // (SelectedCheckpoint=null returns early in OnSelectedCheckpointChanged, so the
            // display field is cleared here too, not there.)
            Parameters.ComfyUiCheckpoint = string.Empty;
            Parameters.ComfyUiModelDisplay = string.Empty;
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
            Parameters.ComfyUiPresetDisplay = string.Empty;
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

    /// <summary>Surface an AI-mutation batch's partial outcome on the action-bar status. Without this a
    /// failed (e.g. timed-out) variant vanishes silently — the user just sees fewer job cards than they
    /// asked for. No-op when every variant succeeded. Called by the mutation VM as it hands off the batch.</summary>
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

    // Keep HasJobs/HasFinishedJobs and the Clear-finished command's CanExecute in sync as the
    // queue mutates. Per-job PropertyChanged is attached/detached here so a running→terminal
    // transition (which flips IsFinished) re-evaluates the gate without polling.
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
        ClearFinishedJobsCommand.NotifyCanExecuteChanged();
    }

    // Queue-eviction control: drop every finished (terminal) card, leaving running and queued
    // jobs in place. Clearing all finished jobs removes every featured candidate (a featured
    // card is always a Saved ⇒ terminal job), so LatestCompletedJob falls back to null.
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
        IOpenRouterModelCatalog? openRouterModelCatalog = null)
    {
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _pollinationsTokenStore = pollinationsTokenStore ?? throw new ArgumentNullException(nameof(pollinationsTokenStore));
        if (comfyUiAuthStore is null) throw new ArgumentNullException(nameof(comfyUiAuthStore));
        if (civitaiTokenStore is null) throw new ArgumentNullException(nameof(civitaiTokenStore));
        if (anthropicTokenStore is null) throw new ArgumentNullException(nameof(anthropicTokenStore));
        _civitaiPostingService = civitaiPostingService ?? throw new ArgumentNullException(nameof(civitaiPostingService));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _catalogCoordinator = catalogCoordinator ?? throw new ArgumentNullException(nameof(catalogCoordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _checkpointService = checkpointService ?? throw new ArgumentNullException(nameof(checkpointService));
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _ollamaModelCatalog = ollamaModelCatalog;
        _comfyVram = comfyVram;
        _gpuGate = gpuGate;
        _openRouterTokenStore = openRouterTokenStore;
        _openRouterModelCatalog = openRouterModelCatalog;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        if (promptBatchParser is null) throw new ArgumentNullException(nameof(promptBatchParser));

        Jobs.CollectionChanged += OnJobsCollectionChanged;

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

        // Keep the header's SelectedModelSummary in sync as the provider/model pickers change.
        ProviderFilter.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ProviderFilterCoordinator.SelectedProvider)
                or nameof(ProviderFilterCoordinator.SelectedModel))
                OnPropertyChanged(nameof(SelectedModelSummary));
        };

        Batch = new BatchCoordinator(
            promptBatchParser: promptBatchParser,
            parametersAccessor: () => Parameters,
            enqueueJob: job => DispatchToUi(() => Jobs.Insert(0, job)),
            runJob: RunJobAsync,
            setStatus: SetStatus,
            addAsInputAsync: InputImages.AddAsInputAsync,
            mutateFromImageAsync: path => MutateFromImageAsync(path));

        // Free the ComfyUI GPU once a whole batch finishes (per-job frees would reload the checkpoint
        // between every variant). The per-job path handles single Generates; here IsBatchRunning has just
        // gone false, so MaybeFree proceeds for the batch as a whole.
        Batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchCoordinator.IsBatchRunning) && !Batch.IsBatchRunning)
                _ = MaybeFreeComfyUiVramAsync(Parameters.Model);
        };

        _parameters.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ImageGenerationParameters.AspectRatio):
                    UpdateCustomAspectRatio(_parameters.AspectRatio);
                    if (!_suppressPreferredArUpdate)
                    {
                        InputImages.RecordExplicitAspectRatioPick(_parameters.AspectRatio);
                        // Persist genuine user picks so the choice survives a restart (mirrors
                        // resolution). Skip "custom" — its width/height aren't persisted, so
                        // restoring it would be wrong; and only a value the model actually offers
                        // (never a Picker null/swap artifact).
                        if (!_suppressAspectRatioPersist
                            && !string.IsNullOrEmpty(_parameters.AspectRatio)
                            && _parameters.AspectRatio != "custom"
                            && AspectRatioOptions.Contains(_parameters.AspectRatio))
                        {
                            _uiStateStore.PersistAspectRatio(_parameters.AspectRatio, _parameters.Model);
                        }
                    }
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
                    if (_logger.IsEnabled(LogLevel.Debug))
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
        // No parameters slot: Claude prompt-builder / AI-tool tiers read the store per request.
        TokenProviders.Add(new TokenProviderViewModel(
            key: "anthropic",
            displayName: "Anthropic",
            placeholder: "Paste your Anthropic API key…",
            helperText: "Required when \"Describe an idea…\" or AI tools use a Claude tier. Create a key at "
                        + "console.anthropic.com. Stored in OS secure storage.",
            store: anthropicTokenStore,
            syncToParameters: static _ => { }));
        if (_openRouterTokenStore is not null)
        {
            TokenProviders.Add(new TokenProviderViewModel(
                key: "openrouter",
                displayName: "OpenRouter",
                placeholder: "Paste your OpenRouter API key…",
                helperText: "Required when image observation uses OpenRouter. The model id is configured below.",
                store: _openRouterTokenStore,
                syncToParameters: static _ => { }));
        }
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
        var job = new GenerationJob(snapshot, AddAsInputAsync, path => MutateFromImageAsync(path));
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
        // Serialize GPU work: a ComfyUI render and local Ollama prompt/AI work share fireEngine's
        // VRAM, so co-loading both thrashes/OOMs. Hold the gate from before submit until the post-render
        // VRAM free, so a mutation (or another render) waits rather than collides. Non-ComfyUI providers
        // and split-host setups (ComfyUI ≠ Ollama box) skip the gate entirely.
        var gpuGated = _gpuGate is not null
            && ModelConstants.ComfyUi.IsId(job.Parameters.Model)
            && GpuColocation.SameHost(ComfyUiBaseUrl, OllamaBaseUrl);
        IDisposable? gpuLease = null;
        if (gpuGated)
        {
            try
            {
                gpuLease = await _gpuGate!.AcquireAsync(job.Cts.Token);
                DispatchToUi(() => IsGpuBusy = true);
            }
            catch (OperationCanceledException)
            {
                // Cancelled while queued behind another GPU workload — nothing rendered.
                _logger.LogInformation("Job finished Kind=Canceled Path=(null)");
                DispatchToUi(() =>
                {
                    job.StatusMessage = "Canceled.";
                    job.StatusKind = StatusKind.Canceled;
                    job.IsRunning = false;
                    job.HasProgress = false;
                });
                job.Cts.Dispose();
                return;
            }
        }

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

                    // Guarded on "already loaded" so a job finishing before the startup folder
                    // enumeration completes can't fabricate a count from null.
                    if (TotalImagesGenerated is { } n) TotalImagesGenerated = n + 1;
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

        // Rendering for this job is done — free the ComfyUI GPU when idle. A batch ITEM sees
        // IsBatchRunning true and skips (no mid-batch checkpoint reload); the batch-completion hook
        // frees once at the end. A single Generate frees here. The GPU gate is released only after the
        // free, so a queued mutation gets a freed card (the inner try/catch never rethrows, so control
        // always reaches here; the finally guards against a future change or a throwing free).
        try
        {
            await MaybeFreeComfyUiVramAsync(job.Parameters.Model);
        }
        finally
        {
            gpuLease?.Dispose();
            if (gpuGated) DispatchToUi(() => IsGpuBusy = false);
        }
    }

    /// <summary>
    /// Free the ComfyUI server's GPU memory when rendering is idle, if the user's "Free GPU memory after
    /// rendering" toggle is on. No-op for non-ComfyUI models, while a batch is still running, or when the
    /// VRAM service isn't wired (tests). Best-effort — the service swallows failures.
    /// </summary>
    internal async Task MaybeFreeComfyUiVramAsync(string model)
    {
        if (!FreeVramAfterRendering
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
    private Task OpenGalleryAsync() => NavigateAsync("gallery", "Gallery");

    [RelayCommand]
    private Task OpenSettingsAsync() => NavigateAsync("settings", "Settings");

    /// <summary>
    /// Shared navigate-and-report body for the shell routes below. Shell.Current is null in the
    /// unit-test harness; the catch keeps tests green if a command is ever invoked there, while
    /// production surfaces the failure through the existing status area. <paramref name="target"/>
    /// is the noun in the "Couldn't open …" message.
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
    /// Transient hand-off slot for the mutation engine page: the structure editor stashes the
    /// typed base model here (so explicit <c>Element.SlotTag</c> values survive — a string
    /// round-trip would strip them), and the mutation VM consumes it on appearance. Plain
    /// property, not observable: nothing binds to it; it's read once then cleared.
    /// </summary>
    public V4JsonPrompt? PendingMutationBase { get; set; }

    /// <summary>
    /// Transient hand-off for the breed flow: the Gallery stashes the selected winners' captions
    /// here, and the mutation VM consumes them on appearance (it flips to AI mode and runs
    /// <c>BreedAsync</c>). Plain property like <see cref="PendingMutationBase"/> — read once then
    /// cleared; nothing binds to it.
    /// </summary>
    public IReadOnlyList<V4JsonPrompt>? PendingBreedSet { get; set; }

    /// <summary>
    /// "Mutate current prompt" entry from MainPage: seed the mutation page from whatever is in the
    /// prompt box (slot tags only inferred, since a string carries none). A non-structured prompt
    /// leaves <see cref="PendingMutationBase"/> null — the mutation VM then reports the problem.
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
            // Navigate anyway: the page surfaces "the prompt box isn't a structured prompt".
            PendingMutationBase = null;
        }

        await NavigateAsync("mutation-engine", "the mutation engine");
    }

    /// <summary>
    /// "Mutate from this" on a saved image (gallery item or finished job): read the image's
    /// embedded caption and seed the mutation page with it as a new base — KEEPING the user's
    /// current generator settings (model/resolution/quality). No recipe restore: the only sensible
    /// source is an Ideogram structured-JSON image and the user is already on a draft Ideogram
    /// preset, so adopting just the caption avoids a surprise model/resolution switch; the mutation
    /// page pins one render seed across the batch so the only visible difference is the mutation.
    /// Slot tags can't survive a metadata string, so they're inferred by SlotTagger — same as the
    /// "Mutate current prompt" path. A non-structured prompt leaves <see cref="PendingMutationBase"/>
    /// null; the mutation page then reports the problem.
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

            // Adopt the image's caption as the box content (the mutation page's null-base fallback
            // re-parses it) and seed the typed base when it parses.
            Parameters.Prompt = prompt;
            try { PendingMutationBase = V4JsonPromptSerializer.Deserialize(prompt); }
            catch (V4JsonPromptParseException) { PendingMutationBase = null; }

            await Shell.Current.GoToAsync("mutation-engine");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mutate from image failed for {Path}", filePath);
            SetStatus("Couldn't load this image's caption. See app.log for details.", StatusKind.Error);
        }
    }

    /// <summary>
    /// "Describe an idea…" entry from MainPage: open the prompt-builder page. The page owns the idea
    /// box + the selected model call and writes the result back through the same Parameters handoff the
    /// structure editor uses, so there's nothing to stash here — just navigate.
    /// </summary>
    [RelayCommand]
    private Task OpenIdeaToPromptAsync() => NavigateAsync("idea-to-prompt", "the prompt builder");

    [RelayCommand]
    private async Task OpenIdeogramEditorAsync()
    {
        // Hand the current prompt box content to the editor so an applied structured prompt
        // round-trips (re-open → same boxes).
        await NavigateAsync(BuildEditorRoute(), "the structure editor");
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

        var savedOllamaUrl = _uiStateStore.LoadOllamaBaseUrl();
        if (!string.IsNullOrEmpty(savedOllamaUrl))
        {
            OllamaBaseUrl = savedOllamaUrl;
        }

        var savedOllamaModel = _uiStateStore.LoadOllamaModel();
        if (!string.IsNullOrEmpty(savedOllamaModel))
        {
            OllamaModel = savedOllamaModel;
        }

        var savedOllamaVisionModel = _uiStateStore.LoadOllamaVisionModel();
        if (!string.IsNullOrEmpty(savedOllamaVisionModel))
        {
            OllamaVisionModel = savedOllamaVisionModel;
        }
        // Seed the picker with the current model so it shows a value before the first live refresh.
        if (!string.IsNullOrWhiteSpace(OllamaModel) && !OllamaModels.Contains(OllamaModel))
            OllamaModels.Add(OllamaModel);
        if (!string.IsNullOrWhiteSpace(OllamaVisionModel) && !OllamaVisionModels.Contains(OllamaVisionModel))
            OllamaVisionModels.Add(OllamaVisionModel);

        OpenRouterVisionFreeOnly = _uiStateStore.LoadOpenRouterVisionFreeOnly();
        var savedOpenRouterVisionModel = _uiStateStore.LoadOpenRouterVisionModel();
        if (savedOpenRouterVisionModel is { Length: > 0 } savedOpenRouterModel
            && (!OpenRouterVisionFreeOnly || savedOpenRouterModel.EndsWith(":free", StringComparison.OrdinalIgnoreCase)))
        {
            OpenRouterVisionModel = savedOpenRouterModel;
            OpenRouterVisionModels.Add(savedOpenRouterModel);
        }

        // Default-on; only flips the field when the user previously turned it off (OnChanged re-persist is
        // a harmless echo — nothing else writes this field).
        FreeVramAfterRendering = _uiStateStore.LoadFreeVramAfterRendering();

        // Echo the saved theme into the picker (the theme itself is already applied at startup in
        // App.xaml.cs). OnChanged re-persists/re-applies harmlessly. Default 0 = System (follow OS).
        AppThemeIndex = (int)_uiStateStore.LoadAppTheme();

        // The override was already applied at the composition root; this just echoes the saved
        // value into the bound field so Settings shows it. OnChanged re-applies harmlessly.
        var savedOutputFolder = _uiStateStore.LoadOutputFolder();
        if (!string.IsNullOrEmpty(savedOutputFolder))
        {
            OutputFolder = savedOutputFolder;
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

        // Restore the last aspect-ratio pick the same way: the model restore above settled
        // AspectRatioOptions, so only adopt a value this model actually offers. SetAspectRatio-
        // Programmatically suppresses the preferred-AR/persist hooks; we then record it explicitly
        // so it also becomes the session's sticky preferred AR (surviving in-session model swaps).
        var savedAspectRatio = _uiStateStore.LoadAspectRatio(Parameters.Model);
        if (!string.IsNullOrEmpty(savedAspectRatio)
            && savedAspectRatio != "custom"
            && AspectRatioOptions.Contains(savedAspectRatio))
        {
            SetAspectRatioProgrammatically(savedAspectRatio);
            InputImages.RecordExplicitAspectRatioPick(savedAspectRatio);
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

    /// <summary>
    /// Remix from an image: read a saved image's embedded recipe and reconstitute it in the
    /// generator (select the model, populate prompt/seed/params). Mirrors LoadSavedUiState's
    /// ordering — model first so RefreshCapabilities settles the option lists, then the
    /// resolution/JSON-prompt writes happen last under the persist-suppression windows. The
    /// common lines (Prompt/ModelName/Seed/AspectRatio/Dimensions/Format/Quality) are applied
    /// here; per-model extras come from the model's IMetadataDescriber.Apply (the inverse of the
    /// Lines writer in ImageFileService). Best-effort: a missing model still loads prompt+seed.
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

            // Prompt + seed first so the best-effort path (unknown model) still loads them.
            meta.ApplyString("Prompt", v => Parameters.Prompt = v);
            if (meta.TryGetValue("Seed", out var seedText)
                && long.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            {
                Parameters.Seed = seed;
                // Without this the seed is re-rolled at generate time (GenerateImageAsync) —
                // defeating the whole point of reproducing the image.
                Parameters.RandomizeSeed = false;
            }

            // Model: only when it resolves in the hydrated catalog. RestoreSelectedModel runs
            // RefreshCapabilities synchronously, settling AspectRatio/Resolution/format options.
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

            // Aspect ratio: only if the (now-current) model offers it. SetAspectRatioProgrammatically
            // keeps it out of the sticky preferred-AR memory.
            if (meta.TryGetValue("AspectRatio", out var ar) && Capabilities.AspectRatios.Contains(ar))
            {
                SetAspectRatioProgrammatically(ar);
                // Custom dimensions live in the Dimensions line (the produced pixel size); only
                // meaningful for custom-dimension models, which is exactly when AR == "custom".
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

            // Output format: skip on models that pin PNG (Ideogram) — RefreshCapabilities already set it.
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

            // Per-model extras (Upsampling, Raw, Gpt*, Resolution, JsonPrompt, Safe, …). Resolution
            // and UseJsonPrompt writes must sit inside the suppression windows so this restore can't
            // overwrite the user's persisted preferences (same rule as LoadSavedUiState).
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

    // Decorative header counter — a plain count via the existing gallery enumeration, not a
    // shared cache with GalleryViewModel (which needs the full item list for its own display).
    // Called fire-and-forget from OnAppearing so a large output folder never delays real startup work.
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

    // GenerationJob's "Use as input" delegate keeps targeting the VM-level method name; route
    // it into the coordinator.
    internal Task AddAsInputAsync(string filePath) => InputImages.AddAsInputAsync(filePath);
}
