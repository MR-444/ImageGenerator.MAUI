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
/// data when required, and reports user-visible status messages through <see cref="StatusRaised"/>.
///
/// It keeps presentation concerns such as output locations, catalog choices, and other persisted
/// preferences separate from the parameters passed to image generation. A DI singleton, shared
/// with GeneratorViewModel, which exposes it as Settings.
/// </summary>
public sealed partial class AppSettingsCoordinator(
    IUiStateStore uiStateStore,
    IFolderPicker folderPicker,
    IOllamaModelCatalog? ollamaModelCatalog,
    IOpenRouterModelCatalog? openRouterModelCatalog,
    IAnthropicModelCatalog? anthropicModelCatalog,
    IAnthropicTokenStore? anthropicTokenStore,
    ILogger<AppSettingsCoordinator> logger)
    : ObservableObject
{
    private readonly IUiStateStore
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));

    private readonly IFolderPicker
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));

    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // The catalogs are optional: each reader no-ops when its catalog is absent (tests pass null).

    /// <summary>A message for the owner's status line.</summary>
    public event Action<string, StatusKind>? StatusRaised;

    private void SetStatus(string message, StatusKind kind) => StatusRaised?.Invoke(message, kind);

    // Inline, per-card result of the two server-backed pickers. The host's StatusMessage bar is
    // rendered only by MainPage.xaml, and the sibling pages bind their OWN view model's status, so
    // a failure is shown on the card whose button was pressed.
    // Empty means "nothing to report" — the same null-hides-the-label contract as
    // TokenCoordinator.CivitaiConnectionStatus, which is the in-app pattern this copies.
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

    // No role's model pick is held here. Each role's ModelChoiceState reads and writes its pick
    // through the store; a second copy here (the Settings page's three pickers, then the text roles'
    // values) went stale the moment a row changed its pick, and the next refresh persisted the stale
    // copy over it. The Ollama refresh reconciles against the store instead.
    private static readonly ModelRole[] LocalTextRoles = [ModelRole.PromptWrite, ModelRole.Mutate, ModelRole.Enrich];

    [ObservableProperty]
    private bool _openRouterFreeOnly = true;

    // One switch for every OpenRouter list. Changing it empties both lists, never a pick, so the next
    // row that needs one asks again under the new filter; a row keeps listing its own saved pick.
    partial void OnOpenRouterFreeOnlyChanged(bool value)
    {
        _uiStateStore.PersistOpenRouterFreeOnly(value);
        OpenRouterVisionModels.ReplaceAll([]);
        OpenRouterTextModels.ReplaceAll([]);
    }

    public BulkObservableCollection<string> OpenRouterVisionModels { get; } = [];

    [RelayCommand]
    private async Task RefreshOpenRouterVisionModelsAsync()
    {
        if (openRouterModelCatalog is null) return;
        OpenRouterVisionStatus = "Checking OpenRouter…";
        try
        {
            var models = await openRouterModelCatalog.ListVisionModelsAsync(OpenRouterFreeOnly);

            // No pick is reconciled: this list is filtered by price, so a model missing from it is not
            // a model that is gone, and the observer's pick is the row's state, not this list's.
            OpenRouterVisionModels.ReplaceAll(models.Select(m => m.Id));

            ReportOpenRouter(models.Count > 0
                ? $"Found {models.Count} OpenRouter vision model(s){(OpenRouterFreeOnly ? " with free pricing." : ".")}"
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
    //
    // No price is attached to anything here, and none can be. These are the providers' own lists, so a
    // figure per entry would have to be invented for models this app has never heard of, on metering it
    // does not implement (thinking tokens, cache reads, image inputs), in a currency the user may not be
    // billed in. What the rows say instead is free or billed and how many calls an operation makes -
    // see ModelChoiceState.Helper, where that sentence is written.

    /// <summary>
    /// OpenRouter models that answer in text, for the roles that only ever send text.
    /// Filtered by <see cref="OpenRouterFreeOnly"/>, the one free-only switch for every OpenRouter list:
    /// it lives on Settings, so a page without a copy of it still says so when the filter hides
    /// everything (see the refresh's status line).
    /// </summary>
    public BulkObservableCollection<string> OpenRouterTextModels { get; } = [];

    [ObservableProperty] private string? _openRouterTextStatus;

    /// <summary>Anthropic models this key can see, read from <c>GET /v1/models</c>.</summary>
    /// <remarks>
    /// Ids, not display strings: the value a role persists is the id, so the "deprecated" marker rides
    /// the refresh line below rather than a suffix that would be stored as part of a model id. Retired
    /// models never reach here at all — see <c>AnthropicModelCatalog</c>.
    /// </remarks>
    public BulkObservableCollection<string> AnthropicTextModels { get; } = [];

    [ObservableProperty] private string? _anthropicStatus;

    /// <summary>Ask OpenRouter which models answer in text. No selection is reconciled here: these lists
    /// serve every text role at once, and which model a role wants is that role's state, not this list's.</summary>
    [RelayCommand]
    private async Task RefreshOpenRouterTextModelsAsync()
    {
        if (openRouterModelCatalog is null)
        {
            // Logged as well as labelled: "unavailable in this build" is a sentence about the app, not
            // about the user's network, and if it ever shows up on a customer's screen the log has to say
            // which dependency was missing rather than leaving the claim unsupported.
            _logger.LogWarning("OpenRouter text model list asked for with no {Catalog} registered.",
                nameof(IOpenRouterModelCatalog));
            ReportOpenRouterText("OpenRouter model list is unavailable in this build.", StatusKind.Warning);
            return;
        }

        OpenRouterTextStatus = "Checking OpenRouter…";
        try
        {
            var models = await openRouterModelCatalog.ListChatModelsAsync(OpenRouterFreeOnly);
            OpenRouterTextModels.ReplaceAll(models.Select(m => m.Id));

            ReportOpenRouterText(models.Count > 0
                ? $"Found {models.Count} OpenRouter model(s) that answer in text{(OpenRouterFreeOnly ? ", free only." : ".")}"
                : OpenRouterFreeOnly
                    ? "No free OpenRouter model answered in text. Turn off free-only in Settings to see paid ones."
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
            _logger.LogWarning("Anthropic model list asked for with no {Missing} registered.",
                anthropicModelCatalog is null ? nameof(IAnthropicModelCatalog) : nameof(IAnthropicTokenStore));
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

            AnthropicTextModels.ReplaceAll(models.Select(m => m.Id));

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

    /// <summary>
    /// The lists, status lines and refresh commands a text role needs, keyed by provider — the dictionary
    /// a <see cref="ModelChoiceState"/> is built from. Assembled here because this class already owns all
    /// three per provider, and pages assembling them by hand is how six copies of one picker ended up
    /// disagreeing about what Refresh did. Every text role reads the lists through a row.
    /// </summary>
    public IReadOnlyDictionary<ModelProvider, IModelSource> TextModelSources =>
        _textModelSources ??= new Dictionary<ModelProvider, IModelSource>
        {
            [ModelProvider.Local] = new ModelSource(
                OllamaModels, this, nameof(OllamaServerStatus), () => OllamaServerStatus,
                () => RefreshOllamaModelsCommand.ExecuteAsync(null)),
            [ModelProvider.Anthropic] = new ModelSource(
                AnthropicTextModels, this, nameof(AnthropicStatus), () => AnthropicStatus,
                () => RefreshAnthropicModelsCommand.ExecuteAsync(null)),
            [ModelProvider.OpenRouter] = new ModelSource(
                OpenRouterTextModels, this, nameof(OpenRouterTextStatus), () => OpenRouterTextStatus,
                () => RefreshOpenRouterTextModelsCommand.ExecuteAsync(null)),
        };

    /// <summary>
    /// As <see cref="TextModelSources"/>, for the role that sends images: two providers, because an
    /// Anthropic vision call is a decision nobody has taken yet, and listing it here would offer it in
    /// the UI whether anyone meant it or not.
    /// </summary>
    public IReadOnlyDictionary<ModelProvider, IModelSource> VisionModelSources =>
        _visionModelSources ??= new Dictionary<ModelProvider, IModelSource>
        {
            [ModelProvider.Local] = new ModelSource(
                OllamaVisionModels, this, nameof(OllamaServerStatus), () => OllamaServerStatus,
                () => RefreshOllamaModelsCommand.ExecuteAsync(null)),
            [ModelProvider.OpenRouter] = new ModelSource(
                OpenRouterVisionModels, this, nameof(OpenRouterVisionStatus), () => OpenRouterVisionStatus,
                () => RefreshOpenRouterVisionModelsCommand.ExecuteAsync(null)),
        };

    // Built on first read rather than in an initializer: a source watches this object for its status
    // property, and "this object" is not available to an initializer. Built once, because a second set
    // of sources would double-subscribe, and a page that re-read the dictionary would find its row
    // listening to two copies of the same list.
    private IReadOnlyDictionary<ModelProvider, IModelSource>? _textModelSources;
    private IReadOnlyDictionary<ModelProvider, IModelSource>? _visionModelSources;

    /// <summary>Installed Ollama models for the Settings picker; filled by <see cref="RefreshOllamaModelsCommand"/>.</summary>
    public BulkObservableCollection<string> OllamaModels { get; } = [];

    /// <summary>Installed Ollama models that advertise both vision and completion capabilities.</summary>
    public BulkObservableCollection<string> OllamaVisionModels { get; } = [];

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
    private static void SeedPicker(BulkObservableCollection<string> picker, IReadOnlyList<string>? cached)
    {
        if (cached is null) return;
        picker.ReplaceAll(cached);
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

            OllamaModels.ReplaceAll(models);
            OllamaVisionModels.ReplaceAll(visionModels);
            _uiStateStore.PersistOllamaModels(OllamaBaseUrl, models, visionModels);

            // A role naming a tag this server no longer has fails at call time, as a 404 nobody would
            // trace back to this card — so drop it. Read from the store, which every row writes through,
            // so only a genuinely gone tag is touched and a pick made this session is left alone. Each
            // role is checked against its own list: a model still installed but no longer advertising
            // vision can still write prose, but cannot observe an image.
            var roleLists = LocalTextRoles.Select(r => (Role: r, List: models))
                .Append((Role: ModelRole.Observe, List: visionModels));
            var cleared = new List<string>();
            foreach (var (role, list) in roleLists)
            {
                var saved = _uiStateStore.LoadLlmModel(role, ModelProvider.Local);
                if (string.IsNullOrWhiteSpace(saved) || list.Contains(saved, StringComparer.Ordinal)) continue;
                _uiStateStore.PersistLlmModel(role, ModelProvider.Local, string.Empty);
                cleared.Add(saved);
            }

            var dropped = cleared
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
    /// Restores the settings-owned Preferences. Calls no generator helper.
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

        // Seed the lists from the last-known server list; startup has verified nothing against the
        // server yet, so nothing is dropped here — the first successful refresh is what reconciles.
        // No pick is seeded: each ModelChoiceState lists its own saved pick even when the cache
        // predates it (ModelChoiceState.RebuildModels).
        // Seed defensively: this runs on the startup path, where an unhandled exception is a native
        // WinUI fail-fast rather than a catchable error, so a store handing back null degrades to
        // "no cache" instead of taking the app down.
        SeedPicker(OllamaModels, _uiStateStore.LoadOllamaModels(OllamaBaseUrl));
        SeedPicker(OllamaVisionModels, _uiStateStore.LoadOllamaVisionModels(OllamaBaseUrl));

        OpenRouterFreeOnly = _uiStateStore.LoadOpenRouterFreeOnly();

        // Default-on; only flips the field when the user turned it off (OnChanged re-persist is
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
