using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Entities;

// CommunityToolkit.Mvvm is cross-platform and UI-framework-agnostic (it only implements
// INotifyPropertyChanged via source gen). Keeping it in Core is a deliberate pragmatic
// call for this single-.csproj app — revisit if the project is ever split into separate
// Core/Infrastructure/Presentation assemblies, in which case swap for hand-rolled
// INotifyPropertyChanged so Core carries zero external deps.
public partial class ImageGenerationParameters : ObservableObject
{
    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private string _model = "openai/gpt-image-1.5";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private long _seed;

    [ObservableProperty]
    private bool _randomizeSeed = true;

    [ObservableProperty]
    private string _aspectRatio = "16:9";

    public ObservableCollection<string> ImagePrompts { get; } = [];

    [ObservableProperty]
    private int _width = ValidationConstants.ImageWidthMax / 2;

    [ObservableProperty]
    private int _height = ValidationConstants.ImageHeightMax / 2;

    partial void OnWidthChanged(int value)
    {
        if (value < ValidationConstants.ImageWidthMin) Width = ValidationConstants.ImageWidthMin;
        else if (value > ValidationConstants.ImageWidthMax) Width = ValidationConstants.ImageWidthMax;
    }

    partial void OnHeightChanged(int value)
    {
        if (value < ValidationConstants.ImageHeightMin) Height = ValidationConstants.ImageHeightMin;
        else if (value > ValidationConstants.ImageHeightMax) Height = ValidationConstants.ImageHeightMax;
    }

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

    // google/nano-banana-2: "1K" | "2K" | "4K".
    [ObservableProperty]
    private string _resolution = "1K";

    // openai/gpt-image-1.5 advanced knobs. Defaults match the API defaults so
    // silence on other models stays equivalent to not sending the field.
    [ObservableProperty]
    private string _gptQuality = "auto";

    [ObservableProperty]
    private string _gptBackground = "auto";

    [ObservableProperty]
    private string _gptModeration = "auto";

    [ObservableProperty]
    private string _gptInputFidelity = "low";

    public ImageGenerationParameters Clone()
    {
        var copy = new ImageGenerationParameters
        {
            ApiToken         = ApiToken,
            Model            = Model,
            Prompt           = Prompt,
            Seed             = Seed,
            RandomizeSeed    = RandomizeSeed,
            AspectRatio      = AspectRatio,
            Width            = Width,
            Height           = Height,
            SafetyTolerance  = SafetyTolerance,
            OutputFormat     = OutputFormat,
            OutputQuality    = OutputQuality,
            PromptUpsampling = PromptUpsampling,
            ImagePromptStrength = ImagePromptStrength,
            Raw              = Raw,
            Resolution       = Resolution,
            GptQuality       = GptQuality,
            GptBackground    = GptBackground,
            GptModeration    = GptModeration,
            GptInputFidelity = GptInputFidelity,
        };
        foreach (var p in ImagePrompts) copy.ImagePrompts.Add(p);
        return copy;
    }
}