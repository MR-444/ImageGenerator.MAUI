using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private const string AllProvidersLabel = "All providers";

    private readonly IJobRunner _jobRunner;
    private readonly IApiTokenStore _tokenStore;
    private readonly IModelCatalogCoordinator _catalogCoordinator;
    private readonly IModelDescriptorRegistry _registry;

    [ObservableProperty]
    private ImageGenerationParameters _parameters;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

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
    private List<string> _outputFormats = [nameof(ImageOutputFormat.Png).ToLower(), nameof(ImageOutputFormat.Jpg).ToLower(), nameof(ImageOutputFormat.Webp).ToLower()];

    [ObservableProperty]
    private bool _isCustomAspectRatio;

    partial void OnIsCustomAspectRatioChanged(bool value)
    {
        OnPropertyChanged(nameof(SupportsCustomDimensions));
    }

    public sealed record InputImageItem(string Base64, ImageSource? Preview, string FileName, string? SourcePath = null);

    public ObservableCollection<InputImageItem> SelectedImages { get; } = [];

    public int InputImageCount => SelectedImages.Count;
    public bool CanAddImage => SelectedImages.Count < MaxImageInputs;

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

    private ModelCapabilities _capabilities;  // hydrated from registry in ctor
    private bool _cachedCatalogLoaded;

    public bool SupportsSafetyTolerance => _capabilities.SafetyTolerance;
    public bool SupportsPromptUpsampling => _capabilities.PromptUpsampling;
    public bool SupportsOutputQuality => _capabilities.OutputQuality;
    public bool SupportsAspectRatio => _capabilities.AspectRatio;
    public bool SupportsCustomDimensions => _capabilities.CustomDimensions && IsCustomAspectRatio;
    public bool SupportsSeed => _capabilities.Seed;
    public bool SupportsImagePrompt => _capabilities.ImagePrompt;
    public bool SupportsImagePromptStrength => _capabilities.ImagePromptStrength && SelectedImages.Count > 0;
    public int MaxImageInputs => _capabilities.MaxImageInputs;
    public string ImagePromptCardTitle => _capabilities.MaxImageInputs > 1
        ? $"Input Images (optional, up to {_capabilities.MaxImageInputs})"
        : "Input Image (optional)";
    public bool SupportsResolution => _capabilities.Resolutions is not null;
    public bool SupportsGptQuality => _capabilities.GptQualityOptions is not null;
    public bool SupportsGptBackground => _capabilities.GptBackgroundOptions is not null;
    public bool SupportsGptModeration => _capabilities.GptModerationOptions is not null;
    public bool SupportsGptInputFidelity => _capabilities.GptInputFidelityOptions is not null;
    public string AspectRatioLabel => _capabilities.AspectRatioLabel;

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
        // in their AR list (Flux 1.1 Pro/Pro Ultra) no-op the first branch.
        var count = SelectedImages.Count;
        if (_lastImageCount == 0 && count > 0 && AspectRatioOptions.Contains("match_input_image"))
        {
            Parameters.AspectRatio = "match_input_image";
        }
        else if (_lastImageCount > 0 && count == 0 && Parameters.AspectRatio == "match_input_image")
        {
            var fallback = _capabilities.AspectRatios.FirstOrDefault(r => r != "match_input_image");
            if (fallback != null) Parameters.AspectRatio = fallback;
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
        _capabilities = _registry.CapabilitiesFor(modelValue ?? string.Empty).Capabilities;
        AspectRatioOptions = _capabilities.AspectRatios.ToList();
        if (!_capabilities.AspectRatios.Contains(Parameters.AspectRatio))
        {
            Parameters.AspectRatio = _capabilities.AspectRatios[0];
        }

        ResolutionOptions = _capabilities.Resolutions?.ToList() ?? [];
        if (ResolutionOptions.Count > 0 && !ResolutionOptions.Contains(Parameters.Resolution))
        {
            Parameters.Resolution = ResolutionOptions[0];
        }

        GptQualityOptions = _capabilities.GptQualityOptions?.ToList() ?? [];
        GptBackgroundOptions = _capabilities.GptBackgroundOptions?.ToList() ?? [];
        GptModerationOptions = _capabilities.GptModerationOptions?.ToList() ?? [];
        GptInputFidelityOptions = _capabilities.GptInputFidelityOptions?.ToList() ?? [];

        // Truncate attached images to the new model's cap so users don't silently lose excess
        // images at generation time. The CollectionChanged handler raises CanAddImage etc.
        while (SelectedImages.Count > _capabilities.MaxImageInputs) SelectedImages.RemoveAt(SelectedImages.Count - 1);

        OnPropertyChanged(nameof(SupportsSafetyTolerance));
        OnPropertyChanged(nameof(SupportsPromptUpsampling));
        OnPropertyChanged(nameof(SupportsOutputQuality));
        OnPropertyChanged(nameof(SupportsAspectRatio));
        OnPropertyChanged(nameof(SupportsCustomDimensions));
        OnPropertyChanged(nameof(SupportsSeed));
        OnPropertyChanged(nameof(SupportsImagePrompt));
        OnPropertyChanged(nameof(SupportsImagePromptStrength));
        OnPropertyChanged(nameof(MaxImageInputs));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(ImagePromptCardTitle));
        OnPropertyChanged(nameof(SupportsResolution));
        OnPropertyChanged(nameof(SupportsGptQuality));
        OnPropertyChanged(nameof(SupportsGptBackground));
        OnPropertyChanged(nameof(SupportsGptModeration));
        OnPropertyChanged(nameof(SupportsGptInputFidelity));
        OnPropertyChanged(nameof(AspectRatioLabel));
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
        IsValid = !string.IsNullOrWhiteSpace(Parameters.ApiToken)
               && !string.IsNullOrWhiteSpace(Parameters.Prompt);
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }

    public GeneratorViewModel(
        IJobRunner jobRunner,
        IApiTokenStore tokenStore,
        IModelCatalogCoordinator catalogCoordinator,
        IModelDescriptorRegistry registry)
    {
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _catalogCoordinator = catalogCoordinator ?? throw new ArgumentNullException(nameof(catalogCoordinator));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));

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

        _parameters.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ImageGenerationParameters.AspectRatio):
                    UpdateCustomAspectRatio(_parameters.AspectRatio);
                    break;
                case nameof(ImageGenerationParameters.ApiToken):
                    ValidateParameters();
                    _tokenStore.Persist(_parameters.ApiToken);
                    break;
                case nameof(ImageGenerationParameters.Prompt):
                    ValidateParameters();
                    break;
                case nameof(ImageGenerationParameters.Model):
                    SyncSelectionFromParameters(_parameters.Model);
                    break;
            }
        };

        RecomputeFilteredModels();
        ValidateParameters();
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
            SetStatus($"Maximum {MaxImageInputs} image(s) for this model.", StatusKind.Error);
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
        SetStatus($"Added image: {result.FileName} ({SelectedImages.Count}/{MaxImageInputs})", StatusKind.Info);
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
    private async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Parameters.ApiToken))
        {
            SetStatus("Enter an API token before refreshing models.", StatusKind.Error);
            return;
        }

        SetStatus("Fetching model catalogs…", StatusKind.Info);
        var merged = await _catalogCoordinator.RefreshAsync(Parameters.ApiToken, cancellationToken);

        if (merged is null)
        {
            SetStatus("No models returned. Check your API token or network.", StatusKind.Error);
            return;
        }

        ApplyCatalog(merged);
        SetStatus($"Loaded {merged.Count} models.", StatusKind.Success);
    }

    private void ApplyCatalog(IReadOnlyList<ModelOption> mergedModels)
    {
        AllModels = mergedModels.ToList();
        Providers = BuildProvidersFrom(mergedModels);
        RecomputeFilteredModels();
    }

    private static List<string> BuildProvidersFrom(IEnumerable<ModelOption> models)
    {
        var list = new List<string> { AllProvidersLabel };
        list.AddRange(models.Select(m => m.Provider).Distinct().OrderBy(p => p));
        return list;
    }

    public async Task LoadSavedTokenAsync()
    {
        var saved = await _tokenStore.LoadAsync();
        if (!string.IsNullOrEmpty(saved))
        {
            Parameters.ApiToken = saved;
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
            Debug.WriteLine($"Model catalog hydrate failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForgetToken()
    {
        _tokenStore.Forget();
        Parameters.ApiToken = string.Empty;
        SetStatus("API token cleared from secure storage.", StatusKind.Info);
    }

    internal async Task AddAsInputAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            SetStatus("File not found.", StatusKind.Error);
            return;
        }
        if (!CanAddImage)
        {
            SetStatus($"Maximum {MaxImageInputs} image(s) for this model.", StatusKind.Error);
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
