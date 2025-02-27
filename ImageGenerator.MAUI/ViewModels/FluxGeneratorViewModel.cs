using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsyncAwaitBestPractices.MVVM;
using ImageGenerator.MAUI.Models;
using ImageGenerator.MAUI.Services;

namespace ImageGenerator.MAUI.ViewModels;

public class GeneratorViewModel : INotifyPropertyChanged
{
    private readonly IImageGenerationService _imageGenerationService;
    private readonly string _apiToken; // This might be injected/configured

    public GeneratorViewModel(IImageGenerationService imageGenerationService, string apiToken, GeneratedImage generatedImage)
    {
        _imageGenerationService = imageGenerationService;
        _apiToken = apiToken;
        _generatedImage = generatedImage;
        
        GenerateImageCommand = new AsyncCommand(GenerateImageAsync);
        // Set default parameters:
        Parameters = new ImageGenerationParameters
        {
            Prompt = "Enter prompt...",
            Steps = 50,
            Guidance = 7.5,
            AspectRatio = "16:9",
            Seed = new Random().Next()
        };
    }

    public ImageGenerationParameters Parameters { get; set; }

    private GeneratedImage _generatedImage;
    public GeneratedImage GeneratedImage
    {
        get => _generatedImage;
        set { _generatedImage = value; OnPropertyChanged(); }
    }

    public IAsyncCommand GenerateImageCommand { get; }

    private async Task GenerateImageAsync()
    {
        GeneratedImage = await _imageGenerationService.GenerateImageAsync(Parameters, _apiToken);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
