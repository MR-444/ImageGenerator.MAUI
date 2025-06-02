using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Common;

namespace ImageGenerator.MAUI.Models;

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
    private int _width = ValidationConstants.ImageWidthMax / 2;

    [ObservableProperty]
    private int _height = ValidationConstants.ImageHeightMax / 2;

    [ObservableProperty]
    private int _safetyTolerance = ValidationConstants.SafetyMax;

    [ObservableProperty]
    private ImageOutputFormat _outputFormat = ImageOutputFormat.Png;

    [ObservableProperty]
    private int _outputQuality = ValidationConstants.OutputQualityMax;

    [ObservableProperty]
    private bool _promptUpsampling;
    
    // Use by FluxPro Ultra
    [ObservableProperty]
    private double _imagePromptStrength = 0.5; // default init value
    
    [ObservableProperty]
    private bool _raw; // Optional flag used by Flux Ultra
}