using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Storage;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private const string AllProvidersLabel = "All providers";
    private const string OutputFolderName = "ImageGenerator.MAUI";
    private const string TokenStorageKey = "imggen.api_token";

    private readonly IImageGenerationService _imageService;
    private readonly IImageFileService _imageFileService;
    private readonly IModelCatalogService _catalogService;

    // Save to the user's Pictures folder so images are easy to find and survive app rebuilds.
    private static string OutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), OutputFolderName);

    // Master catalog — first-launch seed before Refresh Models hydrates from the live APIs.
    private static readonly IReadOnlyList<ModelOption> HardcodedCatalogSeed =
    [
        new("GPT Image 1.5", ModelConstants.OpenAI.GptImage15OnReplicate, "OpenAI (via Replicate)"),
        new("Flux 1.1 Pro", ModelConstants.Flux.Pro11, "Black Forest Labs"),
        new("Flux 1.1 Pro Ultra", ModelConstants.Flux.Pro11Ultra, "Black Forest Labs"),
        new("Flux 2 Klein 4B", ModelConstants.Flux.Klein4b, "Black Forest Labs"),
        new("Nano Banana 2", ModelConstants.Google.NanoBanana2, "Google"),
    ];

    [ObservableProperty]
    private ImageGenerationParameters _parameters;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    [ObservableProperty]
    private string? _generatedImagePath;

    [ObservableProperty]
    private List<ModelOption> _allModels = HardcodedCatalogSeed.ToList();

    [ObservableProperty]
    private List<string> _providers;

    [ObservableProperty]
    private string _selectedProvider = AllProvidersLabel;

    [ObservableProperty]
    private List<ModelOption> _filteredModels = [];

    [ObservableProperty]
    private ModelOption? _selectedModel;

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ModelCapabilities.For(ModelConstants.Flux.Pro11).AspectRatios.ToList();

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

    public sealed record InputImageItem(string Base64, ImageSource? Preview, string FileName);

    public ObservableCollection<InputImageItem> SelectedImages { get; } = [];

    public int InputImageCount => SelectedImages.Count;
    public bool CanAddImage => SelectedImages.Count < MaxImageInputs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateImageCommand))]
    private bool _isValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateImageCommand))]
    private bool _isGenerating;

    private ModelCapabilities _capabilities = ModelCapabilities.For(ModelConstants.Flux.Pro11);
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
        _capabilities = ModelCapabilities.For(modelValue);
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

    public GeneratorViewModel(IImageGenerationService imageService, IImageFileService imageFileService, IModelCatalogService catalogService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _imageFileService = imageFileService ?? throw new ArgumentNullException(nameof(imageFileService));
        _catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));

        SelectedImages.CollectionChanged += OnSelectedImagesChanged;

        _providers = BuildProviders();

        _parameters = new ImageGenerationParameters
        {
            ApiToken = "",
            Model = ModelConstants.Flux.Pro11,
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
                    PersistApiToken(_parameters.ApiToken);
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

    private static List<string> BuildProviders() => BuildProvidersFrom(HardcodedCatalogSeed);

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GenerateImageAsync(CancellationToken cancellationToken)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(Parameters.ApiToken)) missing.Add("API Token");
        if (string.IsNullOrWhiteSpace(Parameters.Prompt)) missing.Add("Prompt");
        if (missing.Count > 0)
        {
            SetStatus($"Missing required field(s): {string.Join(", ", missing)}.", StatusKind.Error);
            return;
        }

        try
        {
            IsGenerating = true;
            SetStatus("Generating image…", StatusKind.Info);

            if (Parameters.RandomizeSeed)
            {
                Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);
            }

            var result = await _imageService.GenerateImageAsync(Parameters, cancellationToken);

            if (!string.IsNullOrEmpty(result.ImageDataBase64))
            {
                Directory.CreateDirectory(OutputDirectory);

                var newImagePath = _imageFileService.GetUniqueSavePath(OutputDirectory, Parameters);
                var imageBytes = Convert.FromBase64String(result.ImageDataBase64);

                await _imageFileService.SaveImageWithMetadataAsync(newImagePath, imageBytes, Parameters);

                GeneratedImagePath = newImagePath;
                SetStatus($"Saved to {newImagePath}", StatusKind.Success);
            }
            else
            {
                var kind = result.Message?.Contains("canceled", StringComparison.OrdinalIgnoreCase) == true
                    ? StatusKind.Canceled
                    : StatusKind.Error;
                SetStatus(result.Message ?? "Image generation failed.", kind);
                GeneratedImagePath = null;
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Image generation was canceled.", StatusKind.Canceled);
            GeneratedImagePath = null;
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", StatusKind.Error);
            GeneratedImagePath = null;
        }
        finally
        {
            IsGenerating = false;
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

        await using var stream = await result.OpenReadAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var imageBytes = memoryStream.ToArray();
        var base64 = Convert.ToBase64String(imageBytes);
        var preview = ImageSource.FromStream(() => new MemoryStream(imageBytes));

        SelectedImages.Add(new InputImageItem(base64, preview, result.FileName));
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
    private void ShowInFolder()
    {
        var target = !string.IsNullOrEmpty(GeneratedImagePath) && File.Exists(GeneratedImagePath)
            ? GeneratedImagePath
            : null;

        try
        {
            if (target != null)
            {
                // Windows-only: open Explorer with the file highlighted.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                Directory.CreateDirectory(OutputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{OutputDirectory}\"",
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open Explorer: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private void OpenImage()
    {
        if (string.IsNullOrEmpty(GeneratedImagePath) || !File.Exists(GeneratedImagePath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = GeneratedImagePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open image: {ex.Message}", StatusKind.Error);
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
        var fetched = await _catalogService.FetchAsync(Parameters.ApiToken, cancellationToken);

        if (fetched.Count == 0)
        {
            SetStatus("No models returned. Check your API token or network.", StatusKind.Error);
            return;
        }

        ApplyCatalog(fetched);
        await _catalogService.SaveCachedAsync(fetched, cancellationToken);
        SetStatus($"Loaded {fetched.Count} models.", StatusKind.Success);
    }

    private void ApplyCatalog(IReadOnlyList<ModelOption> models)
    {
        AllModels = models.ToList();
        Providers = BuildProvidersFrom(models);
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
        try
        {
            var saved = await SecureStorage.Default.GetAsync(TokenStorageKey);
            if (!string.IsNullOrEmpty(saved))
            {
                Parameters.ApiToken = saved;
            }
        }
        catch (Exception ex)
        {
            // Secure storage read failures aren't actionable by the user — log and continue.
            Debug.WriteLine($"SecureStorage.Get failed: {ex.Message}");
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
            var cached = await _catalogService.LoadCachedAsync(ct);
            if (cached is { Count: > 0 })
            {
                ApplyCatalog(cached);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Model catalog hydrate failed: {ex.Message}");
        }
    }

    private static void PersistApiToken(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        try
        {
            _ = SecureStorage.Default.SetAsync(TokenStorageKey, value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SecureStorage.Set failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ForgetToken()
    {
        try
        {
            SecureStorage.Default.Remove(TokenStorageKey);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"SecureStorage.Remove failed: {ex.Message}");
        }
        Parameters.ApiToken = string.Empty;
        SetStatus("API token cleared from secure storage.", StatusKind.Info);
    }

    [RelayCommand]
    private async Task UseGeneratedImageAsInputAsync()
    {
        if (string.IsNullOrEmpty(GeneratedImagePath) || !File.Exists(GeneratedImagePath))
        {
            SetStatus("No generated image available to use as input.", StatusKind.Error);
            return;
        }
        if (!CanAddImage)
        {
            SetStatus($"Maximum {MaxImageInputs} image(s) for this model.", StatusKind.Error);
            return;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(GeneratedImagePath);
            var base64 = Convert.ToBase64String(imageBytes);
            var preview = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            var name = Path.GetFileName(GeneratedImagePath);

            SelectedImages.Add(new InputImageItem(base64, preview, name));

            SetStatus("Generated image added as input.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Error using generated image as input: {ex.Message}", StatusKind.Error);
        }
    }
}
