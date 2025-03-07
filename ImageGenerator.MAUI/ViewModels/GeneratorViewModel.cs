using System.Windows.Input;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Image = SixLabors.ImageSharp.Image;


namespace ImageGenerator.MAUI.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IImageGenerationService? _imageService;

    [ObservableProperty]
    private ImageGenerationParameters _parameters = new()
    {
        // Default values for your sliders, etc.
        ApiToken = "",
        Model = "black-forest-labs/flux-1.1-pro",
        Steps = 25,
        Guidance = 3.0,
        AspectRatio = "1:1",
        Width = 1024,
        Height = 1024,
        OutputFormat = "png",
        OutputQuality = 100,
        Raw = false,
        PromptUpsampling = false,
        Seed = 12345
    };

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _generatedImagePath;

    [ObservableProperty]
    private List<string> _allModels =
    [
        "black-forest-labs/flux-dev", 
        "black-forest-labs/flux-pro", 
        "black-forest-labs/flux-1.1-pro",
        "black-forest-labs/flux-schnell", 
        "black-forest-labs/flux-1.1-pro-ultra"
    ];

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ["1:1", "16:9", "9:16","3:2", "2:3", "4:3", "3:4","4:5", "5:4", "custom"];

    [ObservableProperty]
    private List<string> _outputFormats = ["png", "jpg"];

    [ObservableProperty]
    private bool _isCustomAspectRatio;
    
    [ObservableProperty]
    private double _guidance;

    public ICommand? GenerateImageCommand { get; }
       
    public GeneratorViewModel(IImageGenerationService imageService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        GenerateImageCommand = new AsyncRelayCommand(GenerateImageAsync);
    }

    private async Task GenerateImageAsync()
    {
        StatusMessage = "Generating image...";

        var result = await _imageService!.GenerateImageAsync(Parameters);

        if (!string.IsNullOrEmpty(result.FilePath))
        {
            // Success
            StatusMessage = result.Message;
            
            // load image into memory entirely
            var imageBytes = await File.ReadAllBytesAsync(result.FilePath);
            
            // Now immediately augment EXIF metadata in-memory and save overwriting original
            await SaveImageWithMetadataAsync(result.FilePath, imageBytes, Parameters);

            GeneratedImagePath = result.FilePath;
        }
        else
        {
            // Error
            StatusMessage = result.Message;
            GeneratedImagePath = null;
        }

        // Update the seed with the new returned one.
        Parameters.Seed = result.UpdatedSeed;
    }
    
    
    private async Task SaveImageWithMetadataAsync(string imagePath,
                                                  byte[] imageBytes,
                                                  ImageGenerationParameters parameters)
    {
        using var image = await Image.LoadAsync<Rgba32>(new MemoryStream(imageBytes));

        // Construct your metadata string (you can format differently)
        var metadataText = 
            $"Prompt: {parameters.Prompt}\n" +
            $"Model: {parameters.Model}\n" +
            $"Seed: {parameters.Seed}\n" +
            $"Steps: {parameters.Steps}\n" +
            $"Guidance: {parameters.Guidance}\n" +
            $"AspectRatio: {parameters.AspectRatio}\n" +
            $"Dimensions: {parameters.Width}x{parameters.Height}\n" +
            $"Format: {parameters.OutputFormat}\n" +
            $"Quality: {parameters.OutputQuality}\n" +
            $"Raw: {parameters.Raw}\n" +
            $"Upsampling: {parameters.PromptUpsampling}";

        image.Metadata.ExifProfile ??= new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.UserComment, metadataText);

        // Choose the encoder depending on file format
        switch (parameters.OutputFormat.ToLowerInvariant())
        {
            case "jpeg":
            case "jpg":
                // JPEG Encoder with specified quality
                var jpegEncoder = new JpegEncoder { Quality = parameters.OutputQuality };
                await image.SaveAsync(imagePath, jpegEncoder);
                break;

            default:
                // PNG Encoder
                var pngEncoder = new PngEncoder(); // PNG typically doesn't have quality param
                await image.SaveAsync(imagePath, pngEncoder);
                break;
        }
    }
}