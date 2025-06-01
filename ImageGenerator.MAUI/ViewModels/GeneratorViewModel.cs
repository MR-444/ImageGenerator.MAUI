using System.Windows.Input;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Common;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;


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
        "black-forest-labs/flux-1.1-pro-ultra"
    ];

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ["1:1", "16:9", "9:16","3:2", "2:3", "4:3", "3:4","4:5", "5:4", "9:21", "21:9", "custom"];

    [ObservableProperty]
    private List<string> _outputFormats = [ImageOutputFormat.Png.ToString().ToLower(), ImageOutputFormat.Jpg.ToString().ToLower()];

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


    public ICommand? GenerateImageCommand { get; }
       
    public GeneratorViewModel(IImageGenerationService imageService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        
        _parameters = new ImageGenerationParameters
        {
            // Default values for your sliders, etc.
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
        
        GenerateImageCommand = new AsyncRelayCommand(GenerateImageAsync);
    }

    private async Task GenerateImageAsync()
    {
        StatusMessage = "Generating image...";
        
        // Explicitly randomize before calling the service if required
        if (Parameters.RandomizeSeed)
        {
            // Use Random.Shared for a better randomness /static instance:
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
        }
        else
        {
            // Error
            StatusMessage = result.Message;
            GeneratedImagePath = null;
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

        // Choose the encoder depending on file format
        switch (parameters.OutputFormat)
        {
            case ImageOutputFormat.Jpg:
                // JPEG Encoder with specified quality
                var jpegEncoder = new JpegEncoder { Quality = parameters.OutputQuality };
                await image.SaveAsync(imagePath, jpegEncoder);
                break;

            case ImageOutputFormat.Webp:
                var webpEncoder = new WebpEncoder { Quality = parameters.OutputQuality };
                await image.SaveAsync(imagePath, webpEncoder);
                break;

            default:
                // PNG Encoder
                var pngEncoder = new PngEncoder(); // PNG typically doesn't have quality param
                await image.SaveAsync(imagePath, pngEncoder);
                break;
        }
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
            await using var stream = await result.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            // Store base64 for API parameter
            ImagePromptBase64 = Convert.ToBase64String(imageBytes);
            Parameters.ImagePrompt = ImagePromptBase64;

            // Update image preview
            SelectedImagePreview = ImageSource.FromStream(() => new MemoryStream(imageBytes));
            IsImageSelected = true;
        }
        else
        {
            // If no image is selected
            ImagePromptBase64 = null;
            Parameters.ImagePrompt = null;
            SelectedImagePreview = null;
            IsImageSelected = false;
        }
    }
}