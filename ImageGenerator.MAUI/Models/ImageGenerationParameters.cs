using CommunityToolkit.Mvvm.ComponentModel;

namespace ImageGenerator.MAUI.Models;

// Make it partial so that [ObservableProperty] can generate the backing fields
public partial class ImageGenerationParameters : ObservableObject
{
    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private string _model = "black-forest-labs/flux-1.1-pro";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private long _seed;

    [ObservableProperty]
    private bool _randomizeSeed = true;

    [ObservableProperty]
    private string _aspectRatio = "16:9";
    
    [ObservableProperty]
    private string? _imagePrompt;

    [ObservableProperty]
    private int _width = 1024;

    [ObservableProperty]
    private int _height = 1024;

    [ObservableProperty]
    private int _safetyTolerance = 6;

    [ObservableProperty]
    private string _outputFormat = "png";

    [ObservableProperty]
    private int _outputQuality = 100;

    [ObservableProperty]
    private bool _promptUpsampling;
}