using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
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
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private const string AllProvidersLabel = "All providers";

    private readonly IJobRunner _jobRunner;
    private readonly IApiTokenStore _tokenStore;
    private readonly IPollinationsTokenStore _pollinationsTokenStore;
    private readonly IUiStateStore _uiStateStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;
    private readonly IPromptBatchParser _promptBatchParser;
    private readonly ILogger<GeneratorViewModel> _logger;

    // Non-null only while a batch is actively running. CancelBatch flips it; RunBatchAsync
    // disposes and clears it in a finally so a second batch always starts with a fresh CTS.
    private CancellationTokenSource? _batchCts;

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

    // True while RunBatchAsync is iterating. Drives the "Cancel batch" button visibility
    // and lets the View hide "Import prompts…" while a batch is in flight.
    [ObservableProperty]
    private bool _isBatchRunning;

    public ObservableCollection<GenerationJob> Jobs { get; } = [];
    public bool HasJobs => Jobs.Count > 0;

    [ObservableProperty]
    private List<ModelOption> _allModels = [];  // hydrated from registry in ctor

    [ObservableProperty]
    private List<string> _providers;

    [ObservableProperty]
    private string _selectedProvider = AllProvidersLabel;

    [ObservableProperty]
    private List<ModelOption> _filteredModels = [];

    [ObservableProperty]
    private ModelOption? _selectedModel;

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

    public sealed record InputImageItem(string Base64, ImageSource? Preview, string FileName, string? SourcePath = null);

    public ObservableCollection<InputImageItem> SelectedImages { get; } = [];

    public int InputImageCount => SelectedImages.Count;
    public bool CanAddImage => SelectedImages.Count < Capabilities.MaxImageInputs;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsCustomDimensions))]
    [NotifyPropertyChangedFor(nameof(SupportsImagePromptStrength))]
    [NotifyPropertyChangedFor(nameof(SupportsResolution))]
    [NotifyPropertyChangedFor(nameof(SupportsGptQuality))]
    [NotifyPropertyChangedFor(nameof(ImagePromptCardTitle))]
    [NotifyPropertyChangedFor(nameof(CanAddImage))]
    private ModelCapabilities _capabilities;  // hydrated from registry in ctor
    private bool _cachedCatalogLoaded;

    // Sticky aspect ratio across model swaps: capture the user's last explicit AR pick so
    // we can restore it whenever a model switch lands on a model that supports it. Updated
    // only via the Parameters.PropertyChanged event when _suppressPreferredArUpdate is false,
    // so automatic writes (model fallback, image-add auto-select, image-remove fallback)
    // don't pollute it.
    private string? _preferredAspectRatio;
    private bool _suppressPreferredArUpdate;

    // Persisted model: only user-driven Parameters.Model changes should land in Preferences.
    // Set this true around any code path that writes Parameters.Model as automatic plumbing
    // — chiefly ApplyCatalog (FilteredModels reassignment makes the Picker reset SelectedItem
    // to the first row, which round-trips through SelectedModel → Parameters.Model → persist
    // and clobbers the user's saved value on every launch) and LoadSavedUiState (the value
    // we're applying just came from the store; persisting it back is wasted I/O).
    private bool _suppressModelPersist;

    public bool SupportsCustomDimensions => Capabilities.CustomDimensions && IsCustomAspectRatio;
    public bool SupportsImagePromptStrength => Capabilities.ImagePromptStrength && SelectedImages.Count > 0;
    public string ImagePromptCardTitle => Capabilities.MaxImageInputs > 1
        ? $"Input Images (optional, up to {Capabilities.MaxImageInputs})"
        : "Input Image (optional)";
    public bool SupportsResolution => Capabilities.Resolutions is not null;
    public bool SupportsGptQuality => Capabilities.GptQualityOptions is not null;

    partial void OnParametersChanged(ImageGenerationParameters value)
    {
        UpdateCustomAspectRatio(value.AspectRatio);
        ValidateParameters();
        SyncSelectionFromParameters(value.Model);
    }

    private int _lastImageCount;

    private void OnSelectedImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Mirror into the domain-layer entity so the factory sees the same list.
        Parameters.ImagePrompts.Clear();
        foreach (var item in SelectedImages) Parameters.ImagePrompts.Add(item.Base64);

        // Auto-select match_input_image on 0→1, fall back on 1→0. Models without the option
        // in their AR list (Flux 1.1 Pro/Pro Ultra) no-op the first branch. Both writes are
        // automatic and must not pollute _preferredAspectRatio.
        var count = SelectedImages.Count;
        if (_lastImageCount == 0 && count > 0 && AspectRatioOptions.Contains("match_input_image"))
        {
            _suppressPreferredArUpdate = true;
            Parameters.AspectRatio = "match_input_image";
            _suppressPreferredArUpdate = false;
        }
        else if (_lastImageCount > 0 && count == 0 && Parameters.AspectRatio == "match_input_image")
        {
            // Prefer the user's last explicit AR if it's still valid for the current model
            // and isn't itself "match_input_image"; otherwise fall back to the first concrete AR.
            var preferred = _preferredAspectRatio is { } pref && pref != "match_input_image"
                            && Capabilities.AspectRatios.Contains(pref) ? pref : null;
            var fallback = preferred ?? Capabilities.AspectRatios.FirstOrDefault(r => r != "match_input_image");
            if (fallback != null)
            {
                _suppressPreferredArUpdate = true;
                Parameters.AspectRatio = fallback;
                _suppressPreferredArUpdate = false;
            }
        }
        _lastImageCount = count;

        OnPropertyChanged(nameof(InputImageCount));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(SupportsImagePromptStrength));
    }

    partial void OnSelectedProviderChanged(string value)
    {
        RecomputeFilteredModels();
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        if (value != null && Parameters.Model != value.Value)
        {
            Parameters.Model = value.Value;
        }
        RefreshCapabilities(value?.Value);
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
            (_preferredAspectRatio is { } pref && caps.AspectRatios.Contains(pref)) ? pref :
            caps.AspectRatios.Contains(current) ? current :
            caps.AspectRatios[0];
        if (!string.Equals(target, current, StringComparison.Ordinal))
        {
            _suppressPreferredArUpdate = true;
            Parameters.AspectRatio = target;
            _suppressPreferredArUpdate = false;
        }

        ResolutionOptions = caps.Resolutions?.ToList() ?? [];
        if (ResolutionOptions.Count > 0 && !ResolutionOptions.Contains(Parameters.Resolution))
        {
            Parameters.Resolution = ResolutionOptions[0];
        }

        GptQualityOptions = caps.GptQualityOptions?.ToList() ?? [];
        GptBackgroundOptions = caps.GptBackgroundOptions?.ToList() ?? [];
        GptModerationOptions = caps.GptModerationOptions?.ToList() ?? [];
        GptInputFidelityOptions = caps.GptInputFidelityOptions?.ToList() ?? [];

        // Truncate attached images to the new model's cap so users don't silently lose excess
        // images at generation time. The CollectionChanged handler raises CanAddImage etc.
        while (SelectedImages.Count > caps.MaxImageInputs) SelectedImages.RemoveAt(SelectedImages.Count - 1);

        Capabilities = caps;
    }

    private void RecomputeFilteredModels()
    {
        var list = SelectedProvider == AllProvidersLabel
            ? AllModels.OrderBy(m => m.Provider).ThenBy(m => m.Display).ToList()
            : AllModels.Where(m => m.Provider == SelectedProvider).OrderBy(m => m.Display).ToList();

        FilteredModels = list;

        if (SelectedModel is null || !list.Contains(SelectedModel))
        {
            SelectedModel = list.FirstOrDefault(m => m.Value == Parameters.Model) ?? list.FirstOrDefault();
        }
    }

    private void SyncSelectionFromParameters(string modelValue)
    {
        var match = AllModels.FirstOrDefault(m => m.Value == modelValue);
        if (match == null) return;

        if (SelectedProvider != AllProvidersLabel && SelectedProvider != match.Provider)
        {
            SelectedProvider = match.Provider;
        }
        if (SelectedModel?.Value != match.Value)
        {
            SelectedModel = FilteredModels.FirstOrDefault(m => m.Value == match.Value) ?? match;
        }
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
        IsValid = tokenOk && !string.IsNullOrWhiteSpace(Parameters.Prompt);
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
        _promptBatchParser = promptBatchParser ?? throw new ArgumentNullException(nameof(promptBatchParser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        SelectedImages.CollectionChanged += OnSelectedImagesChanged;
        Jobs.CollectionChanged += (_, _) => DispatchToUi(() => OnPropertyChanged(nameof(HasJobs)));

        // Hydrate the picker + capabilities from the registry — used to come from a static
        // HardcodedCatalogSeed and a static ModelCapabilities.For() lookup.
        _allModels = _registry.Seeds.ToList();
        _capabilities = _registry.CapabilitiesFor(ModelConstants.Flux.Pro11).Capabilities;
        _aspectRatioOptions = _capabilities.AspectRatios.ToList();
        _providers = BuildProvidersFrom(_registry.Seeds);

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
        _preferredAspectRatio = _parameters.AspectRatio;

        _parameters.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ImageGenerationParameters.AspectRatio):
                    UpdateCustomAspectRatio(_parameters.AspectRatio);
                    if (!_suppressPreferredArUpdate)
                        _preferredAspectRatio = _parameters.AspectRatio;
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
                case nameof(ImageGenerationParameters.Model):
                    SyncSelectionFromParameters(_parameters.Model);
                    if (!_suppressModelPersist) _uiStateStore.PersistModel(_parameters.Model);
                    // Switching to or from a Pollinations model changes the token-required rule
                    // and toggles the Pollinations-only UI sections.
                    ValidateParameters();
                    OnPropertyChanged(nameof(IsPollinationsSelected));
                    break;
            }
        };

        RecomputeFilteredModels();
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

    private static void DispatchToUi(Action action)
    {
        try
        {
            if (MainThread.IsMainThread) action();
            else MainThread.BeginInvokeOnMainThread(action);
        }
        catch
        {
            // MainThread throws in unit-test contexts where WinRT isn't initialised.
            // Running synchronously is safe because tests run on a single thread anyway.
            action();
        }
    }

    [RelayCommand]
    private async Task AddImageAsync()
    {
        if (!CanAddImage)
        {
            SetStatus($"Maximum {Capabilities.MaxImageInputs} image(s) for this model.", StatusKind.Error);
            return;
        }

        SetStatus("Opening file picker…", StatusKind.Info);

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Pick an image",
            FileTypes = FilePickerFileType.Images
        });

        if (result == null)
        {
            SetStatus(string.Empty, StatusKind.None);
            return;
        }

        if (SelectedImages.Any(i => string.Equals(i.SourcePath, result.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"'{result.FileName}' is already in the list.", StatusKind.Warning);
            return;
        }

        await using var stream = await result.OpenReadAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var imageBytes = memoryStream.ToArray();
        var base64 = Convert.ToBase64String(imageBytes);
        var preview = ImageSource.FromStream(() => new MemoryStream(imageBytes));

        SelectedImages.Add(new InputImageItem(base64, preview, result.FileName, result.FullPath));
        SetStatus($"Added image: {result.FileName} ({SelectedImages.Count}/{Capabilities.MaxImageInputs})", StatusKind.Info);
    }

    [RelayCommand]
    private void RemoveImage(InputImageItem? item)
    {
        if (item is null) return;
        SelectedImages.Remove(item);
        SetStatus(string.Empty, StatusKind.None);
    }

    [RelayCommand]
    private void ClearImages()
    {
        SelectedImages.Clear();
        SetStatus(string.Empty, StatusKind.None);
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

        ApplyCatalog(merged);
        SetStatus($"Loaded {merged.Count} models.", StatusKind.Success);
    }

    private void ApplyCatalog(IReadOnlyList<ModelOption> mergedModels)
    {
        _suppressModelPersist = true;
        try
        {
            AllModels = mergedModels.ToList();
            Providers = BuildProvidersFrom(mergedModels);
            RecomputeFilteredModels();
        }
        finally
        {
            _suppressModelPersist = false;
        }
    }

    private static List<string> BuildProvidersFrom(IEnumerable<ModelOption> models)
    {
        var list = new List<string> { AllProvidersLabel };
        list.AddRange(models.Select(m => m.Provider).Distinct().OrderBy(p => p));
        return list;
    }

    /// <summary>
    /// Hydrate every provider token from secure storage. One pass per provider; each
    /// TokenProviderViewModel knows its own store and pushes through to Parameters internally.
    /// </summary>
    public async Task LoadAllTokensAsync()
    {
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
        var savedPrompt = _uiStateStore.LoadPrompt();
        if (!string.IsNullOrEmpty(savedPrompt))
        {
            Parameters.Prompt = savedPrompt;
        }

        var savedModel = _uiStateStore.LoadModel();
        if (string.IsNullOrEmpty(savedModel)) return;

        var match = FilteredModels.FirstOrDefault(m => m.Value == savedModel)
                 ?? AllModels.FirstOrDefault(m => m.Value == savedModel);
        if (match == null) return;

        _suppressModelPersist = true;
        try
        {
            SelectedModel = match;
        }
        finally
        {
            _suppressModelPersist = false;
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
                ApplyCatalog(merged);
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

    /// <summary>
    /// Opens a file picker for a .txt prompt file, parses it, and returns the prompts.
    /// Returns null on cancel, on empty parse, or on read/cap errors (status surface set
    /// in those cases). Public for the View to call from its button click handler.
    /// </summary>
    public async Task<IReadOnlyList<string>?> PickAndParsePromptsAsync()
    {
        // Pollinations models can run anonymously; only require the Replicate token when the
        // currently-selected model actually needs it.
        if (!ModelConstants.Pollinations.IsId(Parameters.Model) && string.IsNullOrWhiteSpace(Parameters.ApiToken))
        {
            SetStatus("Enter an API token before running a batch.", StatusKind.Error);
            return null;
        }

        SetStatus("Opening file picker…", StatusKind.Info);

        FileResult? result;
        try
        {
            result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Pick a prompt textfile",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.WinUI, new[] { ".txt" } },
                }),
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open file picker: {ex.Message}", StatusKind.Error);
            return null;
        }

        if (result == null)
        {
            SetStatus(string.Empty, StatusKind.None);
            return null;
        }

        try
        {
            var text = await File.ReadAllTextAsync(result.FullPath);
            var prompts = _promptBatchParser.Parse(text);
            if (prompts.Count == 0)
            {
                SetStatus("No prompts found in file.", StatusKind.Warning);
                return null;
            }
            SetStatus($"Loaded {prompts.Count} prompts from {result.FileName}.", StatusKind.Info);
            return prompts;
        }
        catch (PromptBatchTooLargeException ex)
        {
            SetStatus($"File contains {ex.PromptCount} prompts; cap is {ex.MaxAllowed}.", StatusKind.Error);
            return null;
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't read prompt file: {ex.Message}", StatusKind.Error);
            return null;
        }
    }

    /// <summary>
    /// Runs the supplied prompts as a sequential batch using the currently-selected model
    /// and parameters. Each prompt becomes its own <see cref="GenerationJob"/> queued at the
    /// top of <see cref="Jobs"/> in original file order. Failures don't abort the batch.
    /// </summary>
    public async Task RunBatchAsync(IReadOnlyList<string> prompts)
    {
        if (prompts is null || prompts.Count == 0) return;
        if (IsBatchRunning) return; // re-entrancy guard

        _batchCts = new CancellationTokenSource();
        IsBatchRunning = true;

        try
        {
            // Build all jobs as Queued, freezing per-prompt parameter snapshots up-front
            // so a model swap mid-batch can't bleed into pending jobs.
            var batch = new List<GenerationJob>(prompts.Count);
            for (var i = 0; i < prompts.Count; i++)
            {
                if (Parameters.RandomizeSeed)
                    Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);

                var snapshot = Parameters.Clone();
                snapshot.Prompt = prompts[i];

                var job = new GenerationJob(snapshot, AddAsInputAsync)
                {
                    IsRunning = false,
                    StatusKind = StatusKind.Info,
                    StatusMessage = $"Queued ({i + 1}/{prompts.Count})"
                };
                batch.Add(job);
            }

            // Insert reverse so the FIRST prompt sits at the top of the Jobs list, matching
            // single-prompt mode's "newest first via Insert(0, …)" convention.
            for (var i = batch.Count - 1; i >= 0; i--)
            {
                var jobToInsert = batch[i];
                DispatchToUi(() => Jobs.Insert(0, jobToInsert));
            }

            var succeeded = 0;
            var failed = 0;
            var canceled = 0;

            foreach (var job in batch)
            {
                if (_batchCts.IsCancellationRequested)
                {
                    DispatchToUi(() =>
                    {
                        job.IsRunning = false;
                        job.StatusKind = StatusKind.Canceled;
                        job.StatusMessage = "Canceled.";
                    });
                    canceled++;
                    continue;
                }

                // CancelBatch drains the queue but lets the in-flight job finish — the
                // Replicate prediction is already paid for and the image would be wasted
                // if we killed mid-poll. The per-card Cancel button still aborts a single
                // running job if the user really wants to.
                DispatchToUi(() =>
                {
                    job.IsRunning = true;
                    job.StatusKind = StatusKind.Info;
                    job.StatusMessage = "Generating image…";
                });

                await RunJobAsync(job);

                switch (job.StatusKind)
                {
                    case StatusKind.Success: succeeded++; break;
                    case StatusKind.Canceled: canceled++; break;
                    default: failed++; break;
                }
            }

            var summary = $"Batch complete — {succeeded} ok, {failed} failed, {canceled} canceled.";
            var kind = (failed > 0 || canceled > 0) ? StatusKind.Warning : StatusKind.Success;
            SetStatus(summary, kind);
        }
        finally
        {
            _batchCts?.Dispose();
            _batchCts = null;
            IsBatchRunning = false;
        }
    }

    [RelayCommand]
    private void CancelBatch()
    {
        try { _batchCts?.Cancel(); }
        catch (ObjectDisposedException) { /* race with RunBatchAsync's finally */ }
    }

    internal async Task AddAsInputAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            SetStatus("File not found.", StatusKind.Error);
            return;
        }
        if (SelectedImages.Any(i => string.Equals(i.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            SetStatus($"'{Path.GetFileName(filePath)}' is already in the list.", StatusKind.Warning);
            return;
        }
        if (!CanAddImage)
        {
            SetStatus($"Maximum {Capabilities.MaxImageInputs} image(s) for this model.", StatusKind.Error);
            return;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(filePath);
            var base64 = Convert.ToBase64String(imageBytes);
            var preview = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            var name = Path.GetFileName(filePath);
            SelectedImages.Add(new InputImageItem(base64, preview, name, filePath));
            SetStatus("Added as input.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Error using image as input: {ex.Message}", StatusKind.Error);
        }
    }
}
