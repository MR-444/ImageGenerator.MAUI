using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Coordinates user-facing application preferences and other UI settings that are distinct from
/// model-generation parameters.
/// 
/// This coordinator exposes bindable state for settings screens, keeps model picker collections
/// available to the view, loads saved preferences from the application state store, refreshes catalog
/// data when required, and forwards user-visible status messages to the host page or shell.
/// 
/// It keeps presentation concerns such as output locations, catalog choices, and other persisted
/// preferences separate from the parameters passed to image generation.
/// </summary>
public sealed partial class AppSettingsCoordinator(
    IUiStateStore uiStateStore,
    IFolderPicker folderPicker,
    IOllamaModelCatalog? ollamaModelCatalog,
    IOpenRouterModelCatalog? openRouterModelCatalog,
    IAnthropicModelCatalog? anthropicModelCatalog,
    IAnthropicTokenStore? anthropicTokenStore,
    Action<string, StatusKind> setStatus,
    ILogger logger)
    : ObservableObject
{
    private readonly IUiStateStore
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));

    private readonly IFolderPicker
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly Action<string, StatusKind> _setStatus =
        setStatus ?? throw new ArgumentNullException(nameof(setStatus));

    // Both optional by design: MauiProgram binds whatever IOllamaModelCatalog /
    // IOpenRouterModelCatalog Resolve returned, so they are null on any platform where the
    // implementation threw at registration. Each reader no-ops when its catalog is absent.

    // The host hands over its own logger rather than an ILogger<AppSettingsCoordinator>: this
    // class is composed by GeneratorViewModel, not resolved, so there is no generic logger to
    // inject. Category therefore reads as the generator — acceptable, and it keeps the host's
    // 21-parameter constructor from growing.

    private void SetStatus(string message, StatusKind kind) => _setStatus(message, kind);

    // Inline, per-card result of the two server-backed pickers. The host's StatusMessage bar is
    // rendered only by MainPage.xaml, and the sibling pages bind their OWN view model's status, so
    // until now every one of the 7 Refresh/Folder buttons in the app reported its failure to a label
    // that was not on the page pressed: the user saw nothing and the log got a WARN.
    // Empty means "nothing to report" — the same null-hides-the-label contract as
    // GeneratorViewModel.CivitaiConnectionStatus, which is the in-app pattern this copies.
    [ObservableProperty]
    private string? _ollamaServerStatus;

    [ObservableProperty]
    private string? _openRouterVisionStatus;

    // One funnel per card, so what the card shows and what the status bar shows can never drift
    // apart. announce=false is the silent session-start probe (EnsureOllamaModelsLoadedAsync) and
    // stays silent by design: a down server must not nag on every page navigation.
    private void ReportOllama(string message, StatusKind kind, bool announce = true)
    {
        if (!announce) return;
        OllamaServerStatus = message;
        SetStatus(message, kind);
    }

    private void ReportOpenRouter(string message, StatusKind kind)
    {
        OpenRouterVisionStatus = message;
        SetStatus(message, kind);
    }

    // One funnel per card, as above. The text roles' OpenRouter list and the observer's vision list
    // refresh independently, so a failure on one must not overwrite the answer to a card the user did
    // not press.
    private void ReportOpenRouterText(string message, StatusKind kind)
    {
        OpenRouterTextStatus = message;
        SetStatus(message, kind);
    }

    private void ReportAnthropic(string message, StatusKind kind)
    {
        AnthropicStatus = message;
        SetStatus(message, kind);
    }

    // Non-secret server setting (first of its kind) — Preferences-backed, not a token store.
    // The ComfyUI generation service re-reads the store per request, so edits apply instantly.
    [ObservableProperty] private string _comfyUiBaseUrl = ModelConstants.ComfyUi.DefaultBaseUrl;

    partial void OnComfyUiBaseUrlChanged(string value) =>
        _uiStateStore.PersistComfyUiBaseUrl(value);

    // Runtime-only ComfyUI optimization. Default off because the KJNodes patch node and the
    // sageattention host library are optional; the generation service reads this persisted
    // value immediately before each workflow submission.
    [ObservableProperty]
    private bool _useSageAttention;

    partial void OnUseSageAttentionChanged(bool value) =>
        _uiStateStore.PersistUseSageAttention(value);

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

    partial void OnOllamaBaseUrlChanged(string value)
    {
        // A new address invalidates the old one's verdict: leaving "that isn't an Ollama server"
        // sitting in red under a just-corrected URL reads as "still broken" right before the retry.
        // The Entry commits on focus-loss/Enter, so this is per edit, not per keystroke.
        OllamaServerStatus = null;
        _uiStateStore.PersistOllamaBaseUrl(value);
    }

    // No compile-time default model: which models exist is per-server, so the old hardcoded
    // "qwen2.5" was wrong on most hosts and failed as an opaque Ollama 404. Empty means
    // "never picked" and the pickers show their "Refresh, then pick a model…" title.
    //
    // The persist hooks are gated by a suppression window because a MAUI Picker whose
    // SelectedItem is two-way bound here pushes NULL back the moment its ItemsSource is
    // cleared — which every refresh does. Unsuppressed, that null persisted an empty string
    // and silently destroyed the saved pick on the next launch.
    [ObservableProperty]
    private string _ollamaModel = string.Empty;

    partial void OnOllamaModelChanged(string value)
    {
        if (_suppressOllamaModelPersist) return;
        _uiStateStore.PersistOllamaModel(value);
    }

    [ObservableProperty]
    private string _ollamaVisionModel = string.Empty;

    partial void OnOllamaVisionModelChanged(string value)
    {
        if (_suppressOllamaModelPersist) return;
        _uiStateStore.PersistOllamaVisionModel(value);
    }

    // One flag covers both pickers: they are always rebuilt together by the same refresh.
    private bool _suppressOllamaModelPersist;

    // ---- One Local-tier model per LLM role -------------------------------------------------
    // Three values, not three lists: all three pickers draw from OllamaModels. They are separate
    // because they are three decisions, and until documents/plan-model-picker-rows.md they were one
    // decision — every role read OllamaModel, so picking a cheap tag to verify a caption mutation
    // also changed the model the generate button renders with.
    //
    // They share _suppressOllamaModelPersist with the two pickers above rather than getting flags of
    // their own: they bind the SAME ItemsSource, so they take the same null back at the same moment
    // when a refresh clears it, and unsuppressed that null persists an empty string.
    [ObservableProperty]
    private string _promptWriterModel = string.Empty;

    partial void OnPromptWriterModelChanged(string value)
    {
        if (_suppressOllamaModelPersist) return;
        _uiStateStore.PersistPromptWriterModel(value);
    }

    [ObservableProperty]
    private string _mutationModel = string.Empty;

    partial void OnMutationModelChanged(string value)
    {
        if (_suppressOllamaModelPersist) return;
        _uiStateStore.PersistMutationModel(value);
    }

    [ObservableProperty]
    private string _enrichModel = string.Empty;

    partial void OnEnrichModelChanged(string value)
    {
        if (_suppressOllamaModelPersist) return;
        _uiStateStore.PersistEnrichModel(value);
    }

    [ObservableProperty]
    private string _openRouterVisionModel = string.Empty;

    partial void OnOpenRouterVisionModelChanged(string value)
    {
        if (_suppressOpenRouterVisionModelPersist) return;
        _uiStateStore.PersistOpenRouterVisionModel(value);
    }

    // Same Picker-clears-its-selection hazard as _suppressOllamaModelPersist above.
    private bool _suppressOpenRouterVisionModelPersist;

    [ObservableProperty]
    private bool _openRouterVisionFreeOnly = true;

    partial void OnOpenRouterVisionFreeOnlyChanged(bool value)
    {
        if (_suppressOpenRouterVisionModelPersist) return;
        _uiStateStore.PersistOpenRouterVisionFreeOnly(value);
        OpenRouterVisionModels.Clear();
        OpenRouterVisionModel = string.Empty;
    }

    public ObservableCollection<string> OpenRouterVisionModels { get; } = [];

    [RelayCommand]
    private async Task RefreshOpenRouterVisionModelsAsync()
    {
        if (openRouterModelCatalog is null) return;
        OpenRouterVisionStatus = "Checking OpenRouter…";
        try
        {
            var models = await openRouterModelCatalog.ListVisionModelsAsync(OpenRouterVisionFreeOnly);
            var current = OpenRouterVisionModel;

            _suppressOpenRouterVisionModelPersist = true;
            try
            {
                OpenRouterVisionModels.Clear();
                foreach (var model in models)
                    OpenRouterVisionModels.Add(model.Id);

                // Re-assert through empty so the picker re-selects even when clearing the
                // ItemsSource did not push a null back through the binding.
                var resolved = OpenRouterVisionModels.Contains(current) ? current : string.Empty;
                OpenRouterVisionModel = string.Empty;
                OpenRouterVisionModel = resolved;
            }
            finally { _suppressOpenRouterVisionModelPersist = false; }

            // Deliberate write, outside the window: persists a restored pick and a genuine drop
            // alike. SafeSet skips identical rewrites, so the common case costs nothing.
            _uiStateStore.PersistOpenRouterVisionModel(OpenRouterVisionModel);

            ReportOpenRouter(models.Count > 0
                ? $"Found {models.Count} OpenRouter vision model(s){(OpenRouterVisionFreeOnly ? " with free pricing." : ".")}"
                : "No OpenRouter vision models matched the current filter.",
                models.Count > 0 ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list OpenRouter vision models");
            ReportOpenRouter($"Couldn't refresh OpenRouter models: {ex.Message}", StatusKind.Error);
        }
    }

    // ── Model lists for the text roles ────────────────────────────────────────────────
    //
    // One list per provider per KIND of caller, shared by every role that talks to that provider. A
    // shared list is not the cross-talk this plan exists to kill — a shared *choice* is, and the choice
    // belongs to the role. Which models a server happens to have is one fact, the same on every page.

    /// <summary>
    /// OpenRouter models that answer in text, for the roles that only ever send text.
    /// Deliberately not filtered by free pricing the way the observer's list is: the free-only switch
    /// lives on the observer's card, and honouring it here would hide paid models behind a control that
    /// is not on the page. A long list is an honest cost; a quietly short one is not.
    /// </summary>
    public ObservableCollection<string> OpenRouterTextModels { get; } = [];

    [ObservableProperty] private string? _openRouterTextStatus;

    /// <summary>Anthropic models this key can see, read from <c>GET /v1/models</c>.</summary>
    /// <remarks>
    /// Ids, not display strings: the value a role persists is the id, so the "deprecated" marker rides
    /// the refresh line below rather than a suffix that would be stored as part of a model id. Retired
    /// models never reach here at all — see <c>AnthropicModelCatalog</c>.
    /// </remarks>
    public ObservableCollection<string> AnthropicTextModels { get; } = [];

    [ObservableProperty] private string? _anthropicStatus;

    /// <summary>Ask OpenRouter which models answer in text. No selection is reconciled here: these lists
    /// serve every text role at once, and which model a role wants is that role's state, not this list's.</summary>
    [RelayCommand]
    private async Task RefreshOpenRouterTextModelsAsync()
    {
        if (openRouterModelCatalog is null)
        {
            ReportOpenRouterText("OpenRouter model list is unavailable in this build.", StatusKind.Warning);
            return;
        }

        OpenRouterTextStatus = "Checking OpenRouter…";
        try
        {
            var models = await openRouterModelCatalog.ListChatModelsAsync(freeOnly: false);
            OpenRouterTextModels.Clear();
            foreach (var model in models)
                OpenRouterTextModels.Add(model.Id);

            ReportOpenRouterText(models.Count > 0
                ? $"Found {models.Count} OpenRouter model(s) that answer in text."
                : "No OpenRouter model answered in text.",
                models.Count > 0 ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list OpenRouter text models");
            ReportOpenRouterText($"Couldn't refresh OpenRouter models: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Ask Anthropic which models this key can use, paging past the endpoint's 20-row default.</summary>
    [RelayCommand]
    private async Task RefreshAnthropicModelsAsync()
    {
        if (anthropicModelCatalog is null || anthropicTokenStore is null)
        {
            ReportAnthropic("Anthropic model list is unavailable in this build.", StatusKind.Warning);
            return;
        }

        AnthropicStatus = "Checking Anthropic…";
        try
        {
            // An empty key is not checked here on purpose: the catalog refuses with the same sentence the
            // services use for a missing key, and this card's job is to put that sentence on screen.
            var apiKey = await anthropicTokenStore.LoadAsync();
            var models = await anthropicModelCatalog.ListTextModelsAsync(apiKey ?? string.Empty);
            var deprecated = models.Count(m => m.IsDeprecated);

            AnthropicTextModels.Clear();
            foreach (var model in models)
                AnthropicTextModels.Add(model.Id);

            ReportAnthropic(models.Count > 0
                ? $"Found {models.Count} Anthropic model(s)"
                  + (deprecated > 0 ? $", {deprecated} of them deprecated." : ".")
                : "Anthropic returned no models for this key.",
                models.Count > 0 ? StatusKind.Success : StatusKind.Warning);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't list Anthropic models");
            ReportAnthropic($"Couldn't refresh Anthropic models: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>Installed Ollama models for the Settings picker; filled by <see cref="RefreshOllamaModelsCommand"/>.</summary>
    public ObservableCollection<string> OllamaModels { get; } = [];

    /// <summary>Installed Ollama models that advertise both vision and completion capabilities.</summary>
    public ObservableCollection<string> OllamaVisionModels { get; } = [];

    /// <summary>Pull the model list from the configured Ollama server into the picker.</summary>
    [RelayCommand]
    private Task RefreshOllamaModelsAsync() => LoadOllamaModelsAsync(announce: true);

    // Set once per app session by EnsureOllamaModelsLoadedAsync, whether or not the fetch worked:
    // a down server must not be re-probed (and re-stalled on) by every page navigation.
    private bool _ollamaLoadAttempted;

    /// <summary>
    /// Fetch the model list once per app session, on first appearance of a page that shows an Ollama
    /// picker, so the user doesn't have to press Refresh to see their models. Silent: a server that is
    /// down leaves the cached list in place and says nothing — the Refresh button stays the diagnostic
    /// path, and the "pick a model first" build guard points at it.
    /// </summary>
    public Task EnsureOllamaModelsLoadedAsync()
    {
        if (_ollamaLoadAttempted) return Task.CompletedTask;
        _ollamaLoadAttempted = true;
        return LoadOllamaModelsAsync(announce: false);
    }

    // Null-tolerant by design — see the call site in LoadSavedUiState.
    private static void SeedPicker(ObservableCollection<string> picker, IReadOnlyList<string>? cached)
    {
        if (cached is null) return;
        foreach (var m in cached) picker.Add(m);
    }

    private async Task LoadOllamaModelsAsync(bool announce)
    {
        if (ollamaModelCatalog is null) return;
        if (announce) OllamaServerStatus = "Checking the Ollama server…";
        try
        {
            var modelInfos = await ollamaModelCatalog.ListModelInfosAsync(OllamaBaseUrl);
            var models = modelInfos.Select(m => m.Name).ToArray();
            var visionModels = modelInfos
                .Where(m => m is { SupportsCompletion: true, SupportsVision: true })
                .Select(m => m.Name)
                .ToArray();

            // An empty list is ambiguous — a server with nothing pulled looks exactly like a
            // misconfigured URL or a proxy answering for one. Refuse to reconcile against it:
            // show the warning, but keep the cached options and the user's pick untouched.
            if (models.Length == 0)
            {
                ReportOllama("No models installed on the Ollama server.", StatusKind.Warning, announce);
                return;
            }

            var current = OllamaModel;
            var currentVision = OllamaVisionModel;
            // The role pickers point at the list being rebuilt, so a tag the server dropped has to
            // go from each of them too — a role naming a model this server no longer has fails at
            // call time, as a 404 nobody would trace back to this card.
            var (currentWriter, currentMutation, currentEnrich) =
                (PromptWriterModel, MutationModel, EnrichModel);

            _suppressOllamaModelPersist = true;
            try
            {
                OllamaModels.Clear();
                foreach (var m in models) OllamaModels.Add(m);
                OllamaVisionModels.Clear();
                foreach (var m in visionModels) OllamaVisionModels.Add(m);

                // A model the server no longer lists is dropped rather than kept as a ghost entry
                // that fails later as a 404. Re-assert through empty so the picker re-selects even
                // when clearing the ItemsSource did not push a null back through the binding.
                OllamaModel = string.Empty;
                OllamaModel = OllamaModels.Contains(current) ? current : string.Empty;
                OllamaVisionModel = string.Empty;
                OllamaVisionModel = OllamaVisionModels.Contains(currentVision) ? currentVision : string.Empty;
                PromptWriterModel = string.Empty;
                PromptWriterModel = OllamaModels.Contains(currentWriter) ? currentWriter : string.Empty;
                MutationModel = string.Empty;
                MutationModel = OllamaModels.Contains(currentMutation) ? currentMutation : string.Empty;
                EnrichModel = string.Empty;
                EnrichModel = OllamaModels.Contains(currentEnrich) ? currentEnrich : string.Empty;
            }
            finally { _suppressOllamaModelPersist = false; }

            // Deliberate writes, outside the window: persist a restored pick and a genuine drop alike.
            _uiStateStore.PersistOllamaModel(OllamaModel);
            _uiStateStore.PersistOllamaVisionModel(OllamaVisionModel);
            _uiStateStore.PersistPromptWriterModel(PromptWriterModel);
            _uiStateStore.PersistMutationModel(MutationModel);
            _uiStateStore.PersistEnrichModel(EnrichModel);
            _uiStateStore.PersistOllamaModels(OllamaBaseUrl, models, visionModels);

            // Report exactly what the reconciliation above cleared, by comparing each pick against
            // its own list — a model that is still installed but stopped advertising vision is
            // dropped from the vision picker only, and must still be named here.
            var dropped = new[]
                {
                    OllamaModel.Length == 0 ? current : null,
                    OllamaVisionModel.Length == 0 ? currentVision : null,
                    PromptWriterModel.Length == 0 ? currentWriter : null,
                    MutationModel.Length == 0 ? currentMutation : null,
                    EnrichModel.Length == 0 ? currentEnrich : null,
                }
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            ReportOllama(
                dropped.Length > 0
                    ? $"{string.Join(", ", dropped)} no longer installed on the Ollama server — pick another model."
                    : $"Found {models.Length} Ollama model(s), {visionModels.Length} with vision.",
                dropped.Length > 0 ? StatusKind.Warning : StatusKind.Success,
                announce);
        }
        catch (OllamaEndpointException ex)
        {
            // The host answered; something other than Ollama is listening. Telling the user it could
            // not be reached sends them after a cable, so this case keeps its own sentence and its
            // own log line — and must stay ahead of the transport catch below.
            _logger.LogWarning(ex, "Configured Ollama URL is not an Ollama endpoint {Url}", OllamaBaseUrl);
            ReportOllama(NotOllamaSentence(ex), StatusKind.Error, announce);
        }
        catch (Exception ex)
        {
            // Nothing has been cleared at this point — ListModelInfosAsync throws before the
            // rebuild below it — so a down server leaves the cached list and the pick intact.
            _logger.LogWarning(ex, "Couldn't list Ollama models from {Url}", OllamaBaseUrl);
            ReportOllama($"Couldn't reach Ollama at {OllamaBaseUrl}: {ex.Message}", StatusKind.Error, announce);
        }
    }

    /// <summary>
    /// "HTTP 404" is a fact, not advice. Name the route that failed, say what a working value looks
    /// like, and name the one mistake that reproduces this exactly: a <c>/v1</c> address is the
    /// OpenAI-compatible API, which answers on every path it knows and 404s the ones it does not.
    /// </summary>
    private string NotOllamaSentence(OllamaEndpointException ex)
    {
        var route = ex.Endpoint.Contains("/api/", StringComparison.Ordinal) ? "/api/tags" : ex.Endpoint;
        var head = ex.Problem is null
            ? $"{OllamaBaseUrl} answered HTTP {ex.StatusCode} on {route}"
            : $"{OllamaBaseUrl} answered on {route} but {ex.Problem}";
        var openAiRoot = OllamaBaseUrl.Contains("/v1", StringComparison.OrdinalIgnoreCase)
            ? " A /v1 address is the OpenAI-compatible API; the model list needs Ollama’s own API at the server root."
            : string.Empty;
        return $"{head} — that is not an Ollama server, so restarting or installing models there will not help. "
             + $"Ollama usually listens on {ModelConstants.Ollama.DefaultBaseUrl}.{openAiRoot}";
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
        var trimmed = value.Trim();
        _uiStateStore.PersistOutputFolder(trimmed);
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

    // LoadSavedUiState is a latch, and MainPage.OnAppearing fires on every navigation back to it —
    // so this restore is latched here too rather than relying on its caller.
    private bool _savedSettingsLoaded;

    /// <summary>
    /// Restores the settings-owned Preferences. Calls no generator helper, so it stays a verbatim
    /// copy of what used to sit in the middle of LoadSavedUiState.
    /// </summary>
    public void LoadSavedSettings()
    {
        if (_savedSettingsLoaded) return;
        _savedSettingsLoaded = true;

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
        var savedOllamaVisionModel = _uiStateStore.LoadOllamaVisionModel();
        // Each role's own pick, seeded by the store from the generation model the one time it has
        // never been set on its own (IUiStateStore.LoadPromptWriterModel).
        var savedWriterModel = _uiStateStore.LoadPromptWriterModel();
        var savedMutationModel = _uiStateStore.LoadMutationModel();
        var savedEnrichModel = _uiStateStore.LoadEnrichModel();
        // Seed the pickers from the last-known server list BEFORE assigning the selected
        // model so any attached Picker already sees the item in ItemsSource. The saved pick is
        // appended when the cache predates it; startup has verified nothing against the server yet,
        // so nothing is dropped here — the first successful refresh is what reconciles.
        // Seed defensively: this runs on the startup path, where an unhandled exception is a native
        // WinUI fail-fast rather than a catchable error, so a store handing back null degrades to
        // "no cache" instead of taking the app down.
        _suppressOllamaModelPersist = true;
        try
        {
            SeedPicker(OllamaModels, _uiStateStore.LoadOllamaModels(OllamaBaseUrl));
            SeedPicker(OllamaVisionModels, _uiStateStore.LoadOllamaVisionModels(OllamaBaseUrl));
            if (!string.IsNullOrWhiteSpace(savedOllamaModel))
            {
                if (!OllamaModels.Contains(savedOllamaModel))
                    OllamaModels.Add(savedOllamaModel);
                OllamaModel = savedOllamaModel;
            }
            if (!string.IsNullOrWhiteSpace(savedOllamaVisionModel))
            {
                if (!OllamaVisionModels.Contains(savedOllamaVisionModel))
                    OllamaVisionModels.Add(savedOllamaVisionModel);
                OllamaVisionModel = savedOllamaVisionModel;
            }

            // Same rule for the three role picks: show a saved tag even when the cache predates it,
            // and make sure it is IN the list first, because a Picker silently declines an item its
            // ItemsSource does not contain. The first successful refresh reconciles the lot.
            foreach (var saved in new[] { savedWriterModel, savedMutationModel, savedEnrichModel })
            {
                if (!string.IsNullOrWhiteSpace(saved) && !OllamaModels.Contains(saved))
                    OllamaModels.Add(saved);
            }

            PromptWriterModel = savedWriterModel ?? string.Empty;
            MutationModel = savedMutationModel ?? string.Empty;
            EnrichModel = savedEnrichModel ?? string.Empty;
        }
        finally { _suppressOllamaModelPersist = false; }

        _suppressOpenRouterVisionModelPersist = true;
        try
        {
            OpenRouterVisionFreeOnly = _uiStateStore.LoadOpenRouterVisionFreeOnly();
            var savedOpenRouterVisionModel = _uiStateStore.LoadOpenRouterVisionModel();
            if (savedOpenRouterVisionModel is { Length: > 0 } savedOpenRouterModel
                && (!OpenRouterVisionFreeOnly || savedOpenRouterModel.EndsWith(":free", StringComparison.OrdinalIgnoreCase)))
            {
                OpenRouterVisionModels.Add(savedOpenRouterModel);
                OpenRouterVisionModel = savedOpenRouterModel;
            }
        }
        finally { _suppressOpenRouterVisionModelPersist = false; }

        // Default-on; only flips the field when the user previously turned it off (OnChanged re-persist is
        // a harmless echo — nothing else writes this field).
        FreeVramAfterRendering = _uiStateStore.LoadFreeVramAfterRendering();

        // Default-off host capability. Like the server URL, the generation service re-reads
        // the persisted value per run, so this load merely echoes the switch into Settings.
        UseSageAttention = _uiStateStore.LoadUseSageAttention();

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
    }
}
