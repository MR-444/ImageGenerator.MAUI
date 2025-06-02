using System.Windows.Input;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Common;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace ImageGenerator.MAUI.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IImageGenerationService? _imageService;

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
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        
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

                var newImagePath = Path.Combine(outputDir, BuildFileName(Parameters));

                // Decode from Base64 just once here
                var imageBytes = Convert.FromBase64String(result.ImageDataBase64);

                await SaveImageWithMetadataAsync(newImagePath, imageBytes, Parameters);

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
    
    // Helper method (clearly organize filenames):
    private static string BuildFileName(ImageGenerationParameters parameters)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var invalidChars = Path.GetInvalidFileNameChars();
        var safePrompt = new string(parameters.Prompt.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray())
            .Replace(" ", "_")
            .Replace("__", "_"); // Replace double underscores
 
        safePrompt = safePrompt.Length > 30 ? safePrompt[..30] : safePrompt;

        var fileExtension = parameters.OutputFormat.ToString().ToLowerInvariant();

        return $"{timestamp}_{safePrompt}_{parameters.Seed}.{fileExtension}";
    }

    
    private static async Task SaveImageWithMetadataAsync(string imagePath,
                                                  byte[] imageBytes,
                                                  ImageGenerationParameters parameters)
    {
        using var image = await Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes));

        // Construct your metadata string (you can format differently)
        var metadataText = 
            $"Prompt: {parameters.Prompt}\n" +
            $"ModelName: {parameters.Model}\n" +
            $"Seed: {parameters.Seed}\n" +
            $"AspectRatio: {parameters.AspectRatio}\n" +
            $"Dimensions: {parameters.Width}x{parameters.Height}\n" +
            $"Format: {parameters.OutputFormat}\n" +
            $"Quality: {parameters.OutputQuality}\n" +
            $"Upsampling: {parameters.PromptUpsampling}";

        image.Metadata.ExifProfile ??= new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, metadataText);

        // Use the extracted encoder selection method
        var encoder = GetImageEncoder(parameters.OutputFormat, parameters.OutputQuality);
        await image.SaveAsync(imagePath, encoder);
    }

    // Extracted encoder selection logic
    private static IImageEncoder GetImageEncoder(ImageOutputFormat format, int quality)
    {
        return format switch
        {
            ImageOutputFormat.Jpg => new JpegEncoder { Quality = quality },
            ImageOutputFormat.Webp => new WebpEncoder { Quality = quality },
            ImageOutputFormat.Png => new PngEncoder(),_ => new PngEncoder(),
        };
    }
    
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
}