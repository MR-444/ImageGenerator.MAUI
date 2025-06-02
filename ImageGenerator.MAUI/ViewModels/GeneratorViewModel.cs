using System.Windows.Input;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Common;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace ImageGenerator.MAUI.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IImageGenerationService? _imageService;
    private readonly IImageFileService _imageFileService;

    [ObservableProperty]
    private ImageGenerationParameters _parameters;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _generatedImagePath;

    [ObservableProperty]
    private List<string> _allModels =
    [
        "openAI/gpt-image-1",
        "black-forest-labs/flux-dev", 
        "black-forest-labs/flux-pro", 
        "black-forest-labs/flux-1.1-pro",
        "black-forest-labs/flux-schnell", 
        "black-forest-labs/flux-1.1-pro-ultra",
        "black-forest-labs/flux-kontext-max",
        "black-forest-labs/flux-kontext-pro"
    ];

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ["1:1", "16:9", "9:16","3:2", "2:3", "4:3", "3:4","4:5", "5:4", "9:21", "21:9", "custom"];

    [ObservableProperty]
    private List<string> _outputFormats = [nameof(ImageOutputFormat.Png).ToLower(), nameof(ImageOutputFormat.Jpg).ToLower(), nameof(ImageOutputFormat.Webp).ToLower()];

    [ObservableProperty]
    private bool _isCustomAspectRatio;
    
    [ObservableProperty]
    private double _guidance;
    
    [ObservableProperty]
    private ImageSource? _selectedImagePreview;

    [ObservableProperty]
    private bool _isImageSelected;

    [ObservableProperty]
    private string? _imagePromptBase64;

    [ObservableProperty]
    private bool _isValid;

    [ObservableProperty]
    private MauiColor _statusMessageColor = MauiColor.FromArgb("#000000");

    [ObservableProperty]
    private bool _isGenerating;

    partial void OnParametersChanged(ImageGenerationParameters value)
    {
        UpdateCustomAspectRatio(value.AspectRatio);
        ValidateParameters();
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

    private void UpdateCustomAspectRatio(string aspectRatio)
    {
        IsCustomAspectRatio = aspectRatio == "custom";
        
        if (IsCustomAspectRatio)
        {
            // Ensure width and height are within valid ranges
            Parameters.Width = Math.Clamp(Parameters.Width, ValidationConstants.ImageWidthMin, ValidationConstants.ImageWidthMax);
            Parameters.Height = Math.Clamp(Parameters.Height, ValidationConstants.ImageHeightMin, ValidationConstants.ImageHeightMax);
        }
    }

    private void ValidateParameters()
    {
        IsValid = !string.IsNullOrWhiteSpace(Parameters.ApiToken);
    }

    public ICommand? GenerateImageCommand { get; }
       
    public GeneratorViewModel(IImageGenerationService imageService)
        : this(imageService, new ImageFileService(new ImageEncoderProvider())) { }

    // For DI/testing
    public GeneratorViewModel(IImageGenerationService imageService, IImageFileService imageFileService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        _imageFileService = imageFileService ?? throw new ArgumentNullException(nameof(imageFileService));
        
        _parameters = new ImageGenerationParameters
        {
            // Default values for sliders, etc.
            ApiToken = "",
            Model = "black-forest-labs/flux-1.1-pro",
            AspectRatio = "16:9",
            Width = 1920,
            Height = 1080,
            OutputFormat = ImageOutputFormat.Png,
            OutputQuality = ValidationConstants.OutputQualityMax,
            SafetyTolerance = ValidationConstants.SafetyMax,
            PromptUpsampling = false,
            Seed = Random.Shared.NextInt64(),
            RandomizeSeed = true
        };
        
        // Subscribe to aspect ratio changes
        _parameters.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ImageGenerationParameters.AspectRatio))
            {
                UpdateCustomAspectRatio(_parameters.AspectRatio);
            }
            else if (e.PropertyName == nameof(ImageGenerationParameters.ApiToken))
            {
                ValidateParameters();
            }
        };
        
        GenerateImageCommand = new AsyncRelayCommand(GenerateImageAsync);
    }

    private async Task GenerateImageAsync()
    {
        if (string.IsNullOrWhiteSpace(Parameters.ApiToken))
        {
            StatusMessage = "API Token is required to generate images.";
            StatusMessageColor = MauiColor.FromArgb("#FF0000"); // Red for error
            return;
        }

        try
        {
            IsGenerating = true;
            StatusMessage = "Generating image...";
            StatusMessageColor = MauiColor.FromArgb("#000000"); // Black for normal status
            
            // Explicitly randomize before calling the service if required
            if (Parameters.RandomizeSeed)
            {
                Parameters.Seed = Random.Shared.NextInt64(0, ValidationConstants.SeedMaxValue);
            }

            var result = await _imageService!.GenerateImageAsync(Parameters);
            
            if (!string.IsNullOrEmpty(result.ImageDataBase64))
            {
                var outputDir = Path.Combine(AppContext.BaseDirectory, "Generated_Pictures");
                Directory.CreateDirectory(outputDir);

                var newImagePath = Path.Combine(outputDir, _imageFileService.BuildFileName(Parameters));

                // Decode from Base64 just once here
                var imageBytes = Convert.FromBase64String(result.ImageDataBase64);

                await _imageFileService.SaveImageWithMetadataAsync(newImagePath, imageBytes, Parameters);

                GeneratedImagePath = newImagePath;
                StatusMessage = result.Message;
                StatusMessageColor = MauiColor.FromArgb("#008000"); // Green for success
            }
            else
            {
                // Error
                StatusMessage = result.Message;
                StatusMessageColor = MauiColor.FromArgb("#FF0000"); // Red for error
                GeneratedImagePath = null;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            StatusMessageColor = MauiColor.FromArgb("#FF0000"); // Red for error
            GeneratedImagePath = null;
        }
        finally
        {
            IsGenerating = false;
        }
    }
    
    // Removed file I/O and image processing methods. Now handled by ImageFileService.
    
    [RelayCommand]
    private async Task SelectImagePromptAsync()
    {
        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Pick an image",
            FileTypes = FilePickerFileType.Images
        });

        if (result != null)
        {
            // First set IsImageSelected to true to update aspect ratio options
            IsImageSelected = true;

            await using var stream = await result.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            // Store base64 for API parameter
            ImagePromptBase64 = Convert.ToBase64String(imageBytes);
            Parameters.ImagePrompt = ImagePromptBase64;

            // Update image preview
            SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(imageBytes));
        }
        else
        {
            // If no image is selected, clear everything
            IsImageSelected = false;
            ImagePromptBase64 = null;
            Parameters.ImagePrompt = null;
            SelectedImagePreview = null;
        }
    }

    [RelayCommand]
    private async Task UseGeneratedImageAsInputAsync()
    {
        if (string.IsNullOrEmpty(GeneratedImagePath) || !File.Exists(GeneratedImagePath))
        {
            StatusMessage = "No generated image available to use as input.";
            StatusMessageColor = MauiColor.FromArgb("#FF0000"); // Red for error
            return;
        }

        try
        {
            // Read the generated image file
            var imageBytes = await File.ReadAllBytesAsync(GeneratedImagePath);

            // First set IsImageSelected to true to update aspect ratio options
            IsImageSelected = true;

            // Store base64 for API parameter
            ImagePromptBase64 = Convert.ToBase64String(imageBytes);
            Parameters.ImagePrompt = ImagePromptBase64;

            // Update image preview
            SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(imageBytes));

            StatusMessage = "Generated image set as input prompt.";
            StatusMessageColor = MauiColor.FromArgb("#008000"); // Green for success
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error using generated image as input: {ex.Message}";
            StatusMessageColor = MauiColor.FromArgb("#FF0000"); // Red for error
        }
    }
}