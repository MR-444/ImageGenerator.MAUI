using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private const string AllProvidersLabel = "All providers";

    private readonly IImageGenerationService _imageService;
    private readonly IImageFileService _imageFileService;

    // Master catalog — single source of truth for model metadata.
    private static readonly IReadOnlyList<ModelOption> Catalog =
    [
        new("GPT Image 1", ModelConstants.OpenAI.GptImage1, "OpenAI"),
        new("Flux 1.1 Pro", ModelConstants.Flux.Pro11, "Black Forest Labs"),
        new("Flux 1.1 Pro Ultra", ModelConstants.Flux.Pro11Ultra, "Black Forest Labs"),
        new("Flux Pro", ModelConstants.Flux.Pro, "Black Forest Labs"),
        new("Flux Dev", ModelConstants.Flux.Dev, "Black Forest Labs"),
        new("Flux Schnell", ModelConstants.Flux.Schnell, "Black Forest Labs"),
        new("Flux Kontext Max", ModelConstants.Flux.KontextMax, "Black Forest Labs"),
        new("Flux Kontext Pro", ModelConstants.Flux.KontextPro, "Black Forest Labs"),
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
    private List<ModelOption> _allModels = Catalog.ToList();

    [ObservableProperty]
    private List<string> _providers;

    [ObservableProperty]
    private string _selectedProvider = AllProvidersLabel;

    [ObservableProperty]
    private List<ModelOption> _filteredModels = [];

    [ObservableProperty]
    private ModelOption? _selectedModel;

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ["1:1", "16:9", "9:16", "3:2", "2:3", "4:3", "3:4", "4:5", "5:4", "9:21", "21:9", "custom"];

    [ObservableProperty]
    private List<string> _outputFormats = [nameof(ImageOutputFormat.Png).ToLower(), nameof(ImageOutputFormat.Jpg).ToLower(), nameof(ImageOutputFormat.Webp).ToLower()];

    [ObservableProperty]
    private bool _isCustomAspectRatio;

    [ObservableProperty]
    private ImageSource? _selectedImagePreview;

    [ObservableProperty]
    private bool _isImageSelected;

    [ObservableProperty]
    private string? _imagePromptBase64;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateImageCommand))]
    private bool _isValid;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateImageCommand))]
    private bool _isGenerating;

    partial void OnParametersChanged(ImageGenerationParameters value)
    {
        UpdateCustomAspectRatio(value.AspectRatio);
        ValidateParameters();
        SyncSelectionFromParameters(value.Model);
    }

    partial void OnIsImageSelectedChanged(bool value)
    {
        if (value)
        {
            if (AspectRatioOptions.Contains("match_input_image")) return;

            var newOptions = new List<string>(AspectRatioOptions);
            newOptions.Insert(0, "match_input_image");
            AspectRatioOptions = newOptions;
            Parameters.AspectRatio = "match_input_image";
        }
        else
        {
            if (Parameters.AspectRatio == "match_input_image")
            {
                Parameters.AspectRatio = "16:9";
            }
            var newOptions = new List<string>(AspectRatioOptions);
            newOptions.Remove("match_input_image");
            AspectRatioOptions = newOptions;
        }
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
    }

    private void RecomputeFilteredModels()
    {
        var list = SelectedProvider == AllProvidersLabel
            ? Catalog.OrderBy(m => m.Provider).ThenBy(m => m.Display).ToList()
            : Catalog.Where(m => m.Provider == SelectedProvider).OrderBy(m => m.Display).ToList();

        FilteredModels = list;

        if (SelectedModel is null || !list.Contains(SelectedModel))
        {
            SelectedModel = list.FirstOrDefault(m => m.Value == Parameters.Model) ?? list.FirstOrDefault();
        }
    }

    private void SyncSelectionFromParameters(string modelValue)
    {
        var match = Catalog.FirstOrDefault(m => m.Value == modelValue);
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

    public GeneratorViewModel(IImageGenerationService imageService)
        : this(imageService, new ImageFileService(new ImageEncoderProvider())) { }

    public GeneratorViewModel(IImageGenerationService imageService, IImageFileService imageFileService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _imageFileService = imageFileService ?? throw new ArgumentNullException(nameof(imageFileService));

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

    private static List<string> BuildProviders()
    {
        var list = new List<string> { AllProvidersLabel };
        list.AddRange(Catalog.Select(m => m.Provider).Distinct().OrderBy(p => p));
        return list;
    }

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
                var outputDir = Path.Combine(AppContext.BaseDirectory, "Generated_Pictures");
                Directory.CreateDirectory(outputDir);

                var newImagePath = Path.Combine(outputDir, _imageFileService.BuildFileName(Parameters));
                var imageBytes = Convert.FromBase64String(result.ImageDataBase64);

                await _imageFileService.SaveImageWithMetadataAsync(newImagePath, imageBytes, Parameters);

                GeneratedImagePath = newImagePath;
                SetStatus(result.Message ?? "Image generated.", StatusKind.Success);
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
    private async Task SelectImagePromptAsync()
    {
        SetStatus("Opening file picker…", StatusKind.Info);

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Pick an image",
            FileTypes = FilePickerFileType.Images
        });

        if (result != null)
        {
            IsImageSelected = true;

            await using var stream = await result.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            ImagePromptBase64 = Convert.ToBase64String(imageBytes);
            Parameters.ImagePrompt = ImagePromptBase64;

            SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            SetStatus($"Selected image: {result.FileName}", StatusKind.Info);
        }
        else
        {
            IsImageSelected = false;
            ImagePromptBase64 = null;
            Parameters.ImagePrompt = null;
            SelectedImagePreview = null;
            SetStatus(string.Empty, StatusKind.None);
        }
    }

    [RelayCommand]
    private async Task UseGeneratedImageAsInputAsync()
    {
        if (string.IsNullOrEmpty(GeneratedImagePath) || !File.Exists(GeneratedImagePath))
        {
            SetStatus("No generated image available to use as input.", StatusKind.Error);
            return;
        }

        try
        {
            var imageBytes = await File.ReadAllBytesAsync(GeneratedImagePath);

            IsImageSelected = true;
            ImagePromptBase64 = Convert.ToBase64String(imageBytes);
            Parameters.ImagePrompt = ImagePromptBase64;

            SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(imageBytes));

            SetStatus("Generated image set as input prompt.", StatusKind.Success);
        }
        catch (Exception ex)
        {
            SetStatus($"Error using generated image as input: {ex.Message}", StatusKind.Error);
        }
    }
}
