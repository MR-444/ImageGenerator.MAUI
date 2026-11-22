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

    // Transient confirmation toast (e.g. "Image generated"). Distinct from StatusMessage,
    // which holds form-level errors / info that shouldn't auto-clear.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFlashMessageVisible))]
    [NotifyPropertyChangedFor(nameof(IsPersistentStatusMessageVisible))]
    private string? _flashMessage;

    private int _flashVersion;

    public bool IsFlashMessageVisible => !string.IsNullOrEmpty(FlashMessage);

    public bool IsPersistentStatusMessageVisible =>
        !IsFlashMessageVisible && !string.IsNullOrEmpty(StatusMessage);

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
    [NotifyPropertyChangedFor(nameof(WorkspaceSummary))]
    private bool _isGpuBusy;

    private IDisposable? _batchGpuLease;
    private string? _batchComfyModel;

    // All-time count of images in the output folder, shown in the action bar. Null until the
    // background enumeration in LoadTotalImagesGeneratedAsync completes.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalImagesGeneratedText))]
    [NotifyPropertyChangedFor(nameof(WorkspaceSummary))]
    private int? _totalImagesGenerated;

    public string TotalImagesGeneratedText =>
        TotalImagesGenerated is { } n ? $"{n} image{(n == 1 ? "" : "s")}" : "…";

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;

    // Gates the "Clear finished jobs" command. Recomputed from the live collection whenever a
    // job is added/removed or transitions to a terminal state (see the CollectionChanged +
    // per-job PropertyChanged wiring in the ctor).
    public bool HasFinishedJobs => Jobs.Any(j => j.IsFinished);

    // Compact queue summary, e.g. "2 running · 1 queued · 5 done". A "queued" batch job is
    // !IsRunning with StatusKind still at the default Info (see GenerationJob.IsFinished comment) —
    // recomputed from the same hooks that already track HasFinishedJobs.
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

    // User preferences that are not generation parameters: ComfyUI/Ollama server URLs, the two
    // VRAM toggles, appearance, the Ollama/OpenRouter vision pickers and the output folder.
    // Bound from SettingsPage as Settings.* and from the sibling pages as Generator.Settings.*.
    public AppSettingsCoordinator Settings { get; }

    // ComfyUI workflow state (baked model, quality preset, Krea-2 LoRAs, input-image support,
    // upscale chain + factor). Bound from MainPage as ComfyUi.*; re-probed on every model swap.
    public ComfyUiWorkflowCoordinator ComfyUi { get; }

    // Compact model summary, e.g. "Replicate · flux-dev".
    public string SelectedModelSummary =>
        ProviderFilter.SelectedModel is { } model
            ? $"{ProviderFilter.SelectedProvider} · {model.Display}"
            : ProviderFilter.SelectedProvider;

    /// <summary>
    /// Whether this build can drive the selected model at all: false for a Replicate model with no
    /// field list, true for every described model and for the two uniform providers. Anything that
    /// starts work should consult this rather than re-deriving it, because the service refuses such a
    /// model before any HTTP — a caller that proceeds is not taking a risk, it is queueing a failure.
    /// </summary>
    public bool ModelIsSupported => _registry.IsSchemaKnown(Parameters.Model);

    /// <summary>
    /// Note under the model picker when the selected model has no field list in this build: the app
    /// will not call it, so no request goes out and nothing is billed. Null for every model this
    /// build can describe, which collapses the Label.
    ///
    /// Derived from IsSchemaKnown rather than from an "is this Replicate" test, because a model this
    /// build cannot describe can only be a Replicate owner/name path: Pollinations speaks one uniform
    /// API for every id it lists, and a ComfyUI entry is the user's own workflow.
    /// </summary>
    public string? ModelFieldsNote => ModelIsSupported
        ? null
        : $"{Parameters.Model} is not supported: this build has no field list for it, so generating is refused.";

    // IInputImageTarget: lets the gallery detail page's "Use as input" refuse on the spot when
    // the current model can't take a reference image (the Input Image card is hidden then, so a
    // silent navigation looks like a dead button). SelectedModelSummary above completes the
    // interface.
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

    // Each provider's token Entry is rendered from this collection — adding a third / fourth
    // provider is a single extra item built in the ctor below, no XAML changes.
    public ObservableCollection<TokenProviderViewModel> TokenProviders { get; } = [];

    [ObservableProperty]
    private TokenProviderViewModel? _selectedTokenProvider;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    private bool _isCustomAspectRatio;

    public InputImagesCoordinator InputImages { get; }

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

    // Last Resolution value that was a member of the then-current ResolutionOptions. Reverts the WinUI ComboBox's DEFERRED null push: after an ItemsSource swap the platform
    // picker finishes rebuilding on a later dispatcher tick and pushes SelectedItem=null —
    // outside the suppression window, so flags can't catch it. (The model picker survives the
    // identical push because OnSelectedModelChanged ignores null; this is Resolution's
    // equivalent tolerance.)
    private string _lastValidResolution = string.Empty;

    partial void OnCapabilitiesChanged(ModelCapabilities value) => InputImages.OnCapabilitiesChanged();

    public bool SupportsCustomDimensions => Capabilities.CustomDimensions && IsCustomAspectRatio;
    public bool SupportsResolution => Capabilities.Resolutions is not null;
    public bool SupportsGptQuality => Capabilities.GptQualityOptions is not null;
    // Per-knob visibility for the GPT options card. Each hosted gpt-image model declares its own
    // field set, so "the card is showing" is not enough: gpt-image-2.6-sunburst has no
    // input_fidelity, gpt-image-1.5/-2 have no complexity. Without these the grid renders a
    // labelled Picker bound to an empty list — a control that can only ever be wrong.
    public bool SupportsGptInputFidelity => Capabilities.GptInputFidelityOptions is not null;
    public bool SupportsGptComplexity => Capabilities.GptComplexityOptions is not null;
    public bool SupportsIdeogramOptions => Capabilities.IdeogramOptions;
    // Structured-JSON checkbox + "Edit structure…" button: Ideogram V4 and ComfyUI workflow
    // models (whose graphs consume the caption JSON) both set this.
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
        // A replacement instance arrives with empty token slots while every token row still shows —
        // and validates against — its stored value, so re-seed from the rows before revalidating.
        // Without this, swapping Parameters silently turns into "API Token missing" with a token
        // visibly in the box: the rows push on change only, so nothing else would ever fill them.
        foreach (var provider in TokenProviders) provider.PushToParameters();
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
        // a VALID first/default option into Parameters.AspectRatio synchronously when the current value
        // isn't in the new list. Outside the window that push would look user-driven and overwrite the
        // saved AR before LoadSavedUiState restores it. Unlike resolution the push is a valid
        // string, so the membership guard alone isn't enough — _suppressAspectRatioPersist gates it.
        _suppressAspectRatioPersist = true;
        try
        {
            AspectRatioOptions = [.. caps.AspectRatios];
            // Sticky AR: prefer the user's last explicit pick if the new model supports it,
            // otherwise keep the current AR if still valid (covers initial-state edge cases),
            // otherwise fall back to the model's first AR.
            var current = Parameters.AspectRatio;
            // A descriptor that declares no aspect ratios has no first option to fall back to, and
            // inventing one would send a size this model never offered. Keeping the current value is
            // the same choice the resolution block below makes with its Count guard.
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

        // The options swap must sit INSIDE the persist-suppression window: replacing the
        // ItemsSource makes the two-way-bound Picker push SelectedItem=null into
        // Parameters.Resolution synchronously whenever the current value isn't in the new list.
        // Outside the window that push would look user-driven and persist null, deleting the
        // saved resolution before LoadSavedUiState reads it. (The model picker survives
        // the same push only because OnSelectedModelChanged ignores null.) Exactly ONE Picker
        // may bind Parameters.Resolution: two bound at once livelock the UI thread in a
        // synchronous null/value ping-pong here.
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

        // Models that hide the output-format picker (Ideogram, unknown Replicate models) pin PNG.
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

        // Every knob below is an enum the HOST defines, and the enums differ per model —
        // gpt-image-2.6-sunburst adds quality "xhigh"/"max" and a whole extra field (complexity)
        // that gpt-image-1.5 does not declare. Carrying a selection across that boundary ships a
        // value the new model rejects, which comes back as a 422 naming a field the picker can no
        // longer even display. So: whenever a model offers a list and the current value is not in
        // it, fall back — to this app's own starting value where the new model offers one, and only
        // then to the model's first option.
        //
        // "First option" is not a default. It is the first value in the vendor's enum, and for
        // quality and moderation those lists open with "auto" — for quality, the priced end. That
        // is not hypothetical: SelectedItem is bound two-way to these knobs, so switching to a model
        // with no gpt knobs at all (empty ItemsSource) writes null into them, and coming back found
        // null rather than a choice — which read as "the app defaults to auto" however often the
        // default was moved.
        static void SnapToModelOption(List<string> options, string? current, string appDefault, Action<string> select)
        {
            if (options.Count == 0 || (current is not null && options.Contains(current))) return;
            select(options.Contains(appDefault, StringComparer.Ordinal) ? appDefault : options[0]);
        }

        GptQualityOptions = caps.GptQualityOptions?.ToList() ?? [];
        // The quality enum differs per host: Replicate offers auto/low/medium/high, Pollinations
        // low/medium/high/hd. Carrying a selection across that boundary would leave the picker
        // blank on a value the new model rejects, so snap it to something both can send.
        SnapToModelOption(GptQualityOptions, Parameters.GptQuality,
            ImageGenerationParameters.DefaultGptQuality, v => Parameters.GptQuality = v);
        GptBackgroundOptions = caps.GptBackgroundOptions?.ToList() ?? [];
        SnapToModelOption(GptBackgroundOptions, Parameters.GptBackground,
            ImageGenerationParameters.DefaultGptBackground, v => Parameters.GptBackground = v);
        GptModerationOptions = caps.GptModerationOptions?.ToList() ?? [];
        SnapToModelOption(GptModerationOptions, Parameters.GptModeration,
            ImageGenerationParameters.DefaultGptModeration, v => Parameters.GptModeration = v);
        GptInputFidelityOptions = caps.GptInputFidelityOptions?.ToList() ?? [];
        SnapToModelOption(GptInputFidelityOptions, Parameters.GptInputFidelity,
            ImageGenerationParameters.DefaultGptInputFidelity, v => Parameters.GptInputFidelity = v);
        GptComplexityOptions = caps.GptComplexityOptions?.ToList() ?? [];
        SnapToModelOption(GptComplexityOptions, Parameters.GptComplexity,
            ImageGenerationParameters.DefaultGptComplexity, v => Parameters.GptComplexity = v);

        // Truncate attached images to the new model's cap so users don't silently lose excess
        // images at generation time. The coordinator's CollectionChanged handler raises
        // CanAddImage etc. on the coordinator surface. ComfyUI is deferred: its real cap is
        // workflow-specific (LoadImage → 1) and known only after the async probe below —
        // truncating to the static 0 here would drop an image the probe is about to legitimize
        // (e.g. gallery "Use as input" followed by picking the upscale workflow).
        if (!ModelConstants.ComfyUi.IsId(modelValue))
        {
            InputImages.TruncateToMaxInputs(caps.MaxImageInputs);
        }

        Capabilities = caps;

        // Capabilities now reflect the new model, so the JSON-prompt validation gate is accurate.
        ValidateParameters();

        // Fire-and-forget: the workflow probes hydrate asynchronously (local file probes plus
        // one optional server round-trip) while the synchronous capability swap above stays
        // instant. Each probe owns its try/catch and guards stale completions with its own
        // version token — see ComfyUiWorkflowCoordinator.RefreshForModel.
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
        // A model this build has no field list for is refused by the service before any HTTP, so a
        // tap here could only ever produce a failed job. ModelFieldsNote says so in words under the
        // picker; this is the same sentence in the one place the user is about to act. MainPage's
        // Ctrl+Enter handler reads this same flag, so the button and the hotkey close together.
        IsValid = ModelIsSupported && tokenOk && !string.IsNullOrWhiteSpace(Parameters.Prompt) && jsonOk;
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
        OnPropertyChanged(nameof(WorkspaceSummary));
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
        IOpenRouterModelCatalog? openRouterModelCatalog = null,
        IComfyUiLoraService? comfyUiLoraService = null,
        IAnthropicModelCatalog? anthropicModelCatalog = null)
    {
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        var tokenStore1 = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        var pollinationsTokenStore1 = pollinationsTokenStore ?? throw new ArgumentNullException(nameof(pollinationsTokenStore));
        if (comfyUiAuthStore is null) throw new ArgumentNullException(nameof(comfyUiAuthStore));
        if (civitaiTokenStore is null) throw new ArgumentNullException(nameof(civitaiTokenStore));
        if (anthropicTokenStore is null) throw new ArgumentNullException(nameof(anthropicTokenStore));
        _civitaiPostingService = civitaiPostingService ?? throw new ArgumentNullException(nameof(civitaiPostingService));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _catalogCoordinator = catalogCoordinator ?? throw new ArgumentNullException(nameof(catalogCoordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _galleryService = galleryService ?? throw new ArgumentNullException(nameof(galleryService));
        _comfyVram = comfyVram;
        _gpuGate = gpuGate;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Composed here, not resolved: the host keeps its 21 parameters (a DI constructor is
        // resolved, never called, so a hand-written one cannot be unit-tested) and the coordinator
        // takes the three dependencies no generator member touches.
        Settings = new AppSettingsCoordinator(
            uiStateStore, folderPicker, ollamaModelCatalog, openRouterModelCatalog,
            anthropicModelCatalog, anthropicTokenStore, SetStatus, logger);
        if (promptBatchParser is null) throw new ArgumentNullException(nameof(promptBatchParser));

        Jobs.CollectionChanged += OnJobsCollectionChanged;

        // Defaults live on ImageGenerationParameters and nowhere else, pinned by
        // ImageGenerationParametersDefaultsTests. The only thing added here is the seed the session
        // starts on: it is random per launch rather than a default, and RandomizeSeed regenerates it
        // per run anyway.
        _parameters = new ImageGenerationParameters
        {
            Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue)
        };

        // Seeded from the model this instance starts on, so anything constructed before the parameter
        // hooks refresh capabilities sees this model's capabilities, not another's.
        _capabilities = _registry.CapabilitiesFor(_parameters.Model).Capabilities;
        _aspectRatioOptions = [.. _capabilities.AspectRatios];

        // Seed the preferred AR with the app's starting ratio so first-launch model swaps already
        // prefer the shape the app was built around instead of slamming to caps.AspectRatios[0].
        InputImages = new InputImagesCoordinator(
            capsAccessor: () => Capabilities,
            setAspectRatioProgrammatically: SetAspectRatioProgrammatically,
            mirrorImagePromptsToParameters: MirrorImagePromptsToParameters,
            setStatus: SetStatus,
            logger: logger,
            initialPreferredAspectRatio: _parameters.AspectRatio);

        // ComfyUI workflow probing/ownership. Constructed before ProviderFilter because
        // RefreshCapabilities (which drives ComfyUi.RefreshForModel) is reachable from the
        // provider's selection callback. All collaborators are read through delegates: the
        // parameters instance is replaceable through its property (a replacement also re-seeds the
        // token slots — see OnParametersChanged), so no owner may pin the instance by value.
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

        // Keep the action bar's model and workspace summaries in sync with both pickers.
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

        // Free the ComfyUI GPU once a whole batch finishes (per-job frees would reload the checkpoint
        // between every variant), while still holding the batch GPU lease so waiting Ollama work
        // sees a freed card.
        Batch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BatchCoordinator.IsBatchRunning) && !Batch.IsBatchRunning)
                _ = CompleteBatchGpuAsync();
        };

        AttachParameters(_parameters);

        ValidateParameters();

        // Build the tabbed token list. Order = display order in the Picker. Each entry's
        // syncToParameters callback writes to the right parameters slot so service code
        // (ReplicateImageGenerationService.parameters.ApiToken,
        // PollinationsImageGenerationService.parameters.PollinationsApiToken) keeps reading
        // its own slot. Written through the Parameters property, never the _parameters field: the
        // property is the single access funnel for the instance, and a callback that pinned the
        // instance (or wrote the field behind OnParametersChanged) would keep working while writing
        // somewhere nobody re-seeds or revalidates — see OnParametersChanged.
        TokenProviders.Add(new TokenProviderViewModel(
            key: "replicate",
            displayName: "Replicate",
            placeholder: "Paste your Replicate API token…",
            helperText: "Required for Replicate-hosted models (Flux, GPT, Nano Banana). Stored in OS secure storage.",
            store: tokenStore1,
            syncToParameters: v => Parameters.ApiToken = v));
        TokenProviders.Add(new TokenProviderViewModel(
            key: "pollinations",
            displayName: "Pollinations",
            placeholder: "Paste your Pollinations token (optional)…",
            helperText: "Optional. Anonymous mode is rate-limited (1 req / 15 s). Register at auth.pollinations.ai for higher limits.",
            store: pollinationsTokenStore1,
            syncToParameters: v => Parameters.PollinationsApiToken = v));
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
                    // Persist genuine user picks so the choice survives a restart (mirrors
                    // resolution). Skip "custom" — its width/height aren't persisted, so
                    // restoring it would be wrong; and only a value the model actually offers
                    // (never a Picker null/swap artifact).
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
                // Persistence lives on TokenProviderViewModel now — only revalidate here.
                ValidateParameters();
                break;
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
                // Revert the deferred null push (see _lastValidResolution). Unsuppressed
                // null/empty while the model still offers options is never a user pick —
                // the Picker only offers list members.
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

                // Membership guard: only a value the current model actually offers can be
                // a user pick. Binding artifacts (the Pickers pushing null on ItemsSource
                // attach/swap) must never overwrite the saved choice.
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
                // Persist unconditionally: the launch restore writes the value just loaded,
                // which the store's skip-identical rule turns into a no-op.
                _uiStateStore.PersistCivitaiModelRef(Parameters.CivitaiModelRef);
                break;
            case nameof(ImageGenerationParameters.UpscaleAfterRender):
                // Owned by the workflow coordinator, which holds the persist-suppression flag
                // this needs; see ComfyUiWorkflowCoordinator.OnUpscaleAfterRenderChanged.
                ComfyUi.OnUpscaleAfterRenderChanged();
                break;
            case nameof(ImageGenerationParameters.Model):
                ProviderFilter.SyncSelectionFromParameters(Parameters.Model);
                if (!ProviderFilter.SuppressModelPersist) _uiStateStore.PersistModel(Parameters.Model);
                // Switching to or from a Pollinations model changes the token-required rule
                // and toggles the Pollinations-only UI sections.
                ValidateParameters();
                OnPropertyChanged(nameof(IsPollinationsSelected));
                // The note is computed from Parameters.Model. The ProviderFilter hook that also
                // raises it only fires when the selection object actually changes, and a model the
                // catalog does not contain cannot become the selection — so without this line the
                // note keeps naming the model before the change, in the one message whose whole job
                // is naming the model.
                OnPropertyChanged(nameof(ModelFieldsNote));
                break;
        }
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

        // Activation audit: logs every generate run before the service's first line, so an extra run
        // can be attributed. JobCount exposes unnoticed extra cards (e.g. a focused-button Space/Enter re-fire).
        _logger.LogInformation(
            "Generate activated Model={Model} Seed={Seed} JobCount={Count}",
            snapshot.Model, snapshot.Seed, Jobs.Count);

        // Click-time feedback: generation can take minutes, so confirm the click before
        // the job-row spinner is the only visible signal.
        _ = FlashAsync("Generation started");

        await RunJobAsync(job);
    }

    private Task RunBatchJobAsync(GenerationJob job) => RunJobAsync(job, isBatchJob: true);

    private async Task RunJobAsync(GenerationJob job, bool isBatchJob = false)
    {
        // Serialize GPU work: a ComfyUI render and local Ollama prompt/AI work share fireEngine's
        // VRAM, so co-loading both thrashes/OOMs. Hold the gate from before submit until the post-render
        // VRAM free, so a mutation (or another render) waits rather than collides. Non-ComfyUI providers
        // and split-host setups (ComfyUI ≠ Ollama box) skip the gate entirely.
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
        else if (isBatchJob && isComfyJob)
        {
            // Split-host or test without _gpuGate: remember the batch's actual model so
            // CompleteBatchGpuAsync still frees ComfyUI VRAM even if the UI picker changes mid-batch.
            _batchComfyModel = job.Parameters.Model;
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
            if (outcome is { Kind: JobOutcomeKind.Saved, SavedPath: not null }
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

        // Batch jobs keep their lease and skip /free until CompleteBatchGpuAsync runs at batch end.
        if (isBatchJob) return;

        // Rendering for this single job is done — free the ComfyUI GPU when idle, then release
        // the gate so a queued mutation gets a freed card.
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
    /// Free the ComfyUI server's GPU memory when rendering is idle, if the user's "Free GPU memory after
    /// rendering" toggle is on. No-op for non-ComfyUI models, while a batch is still running, or when the
    /// VRAM service isn't wired (tests). Best-effort — the service swallows failures.
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
        var version = ++_flashVersion;
        DispatchToUi(() => FlashMessage = message);
        await Task.Delay(durationMs);
        DispatchToUi(() =>
        {
            if (_flashVersion == version) FlashMessage = null;
        });
    }

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

            await NavigateAsync("mutation-engine", "the mutation engine");
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
        var json = Uri.EscapeDataString(Parameters.Prompt);
        var shape = ModelConstants.ComfyUi.IsId(Parameters.Model)
            ? Parameters.AspectRatio
            : Parameters.Resolution;
        return $"ideogram-editor?json={json}&resolution={Uri.EscapeDataString(shape)}";
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
        // The catalog can change which ComfyUI workflows exist, so re-probe the LoRA catalog
        // (the one workflow probe worth awaiting here — it is the only one with a round-trip).
        await ComfyUi.RefreshLorasForCurrentModelAsync(Parameters.Model);
        // Say what the catalog actually produced for this build. Being allow-listed by owner is enough
        // for a model to appear here, and appearing has never meant the app knows its fields — without
        // this number, 90 entries read as 90 working models.
        var unsupported = merged.Count(m => !_registry.IsSchemaKnown(m.Value));
        SetStatus(unsupported > 0
            ? $"Loaded {merged.Count} models ({merged.Count - unsupported} this build can drive; the other {unsupported} are marked and will not run)."
            : $"Loaded {merged.Count} models.",
            StatusKind.Success);
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

        Settings.LoadSavedSettings();

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
    private Task AddAsInputAsync(string filePath) => InputImages.AddAsInputAsync(filePath);
}
