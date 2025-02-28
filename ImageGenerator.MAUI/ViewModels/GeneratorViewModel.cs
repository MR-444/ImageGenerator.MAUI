using System.Windows.Input;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ImageGenerator.MAUI.ViewModels;

public partial class GeneratorViewModel : ObservableObject
{
    private readonly IImageGenerationService? _imageService;

    [ObservableProperty]
    private ImageGenerationParameters _parameters = new ImageGenerationParameters
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
        "black-forest-labs/flux-dev", "black-forest-labs/flux-pro", "black-forest-labs/flux-1.1-pro",
        "black-forest-labs/flux-schnell", "black-forest-labs/flux-1.1-pro-ultra"
    ];

    [ObservableProperty]
    private List<string> _aspectRatioOptions = ["1:1", "16:9", "4:3", "custom"];

    [ObservableProperty]
    private List<string> _outputFormats = ["png", "jpg", "bmp"];

    [ObservableProperty]
    private bool _isCustomAspectRatio;
    
    [ObservableProperty]
    private double _guidance;

    
    public ICommand? GenerateImageCommand { get; }
  
    
    // Default constructor
    public GeneratorViewModel()
    {
        // Optionally initialize fields here
    }

        
    public GeneratorViewModel(IImageGenerationService imageService)
    {
        _imageService = imageService ?? throw new ArgumentNullException(nameof(imageService));
        GenerateImageCommand = new AsyncRelayCommand(GenerateImageAsync);
    }

    private async Task GenerateImageAsync()
    {
        StatusMessage = "Generating image...";
        // Optional: you can do more UI toggling here (e.g., busy indicator)

        var result = await _imageService!.GenerateImageAsync(Parameters);

        if (!string.IsNullOrEmpty(result.FilePath))
        {
            // Success
            StatusMessage = result.Message;
            GeneratedImagePath = result.FilePath;
        }
        else
        {
            // Error
            StatusMessage = result.Message;
            GeneratedImagePath = null;
        }

        // Update the seed if the service returned a new one
        Parameters.Seed = result.UpdatedSeed;
    }
}