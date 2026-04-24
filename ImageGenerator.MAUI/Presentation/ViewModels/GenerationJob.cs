using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

public partial class GenerationJob : ObservableObject
{
    private readonly Func<string, Task>? _useAsInput;

    public ImageGenerationParameters Parameters { get; }
    public string Prompt { get; }
    public string MetaLine { get; }

    [ObservableProperty] private string _statusMessage = "Generating image…";
    [ObservableProperty] private StatusKind _statusKind = StatusKind.Info;
    [ObservableProperty] private string? _resultPath;
    [ObservableProperty] private bool _isRunning = true;

    public CancellationTokenSource Cts { get; } = new();

    public GenerationJob(ImageGenerationParameters parameters, Func<string, Task>? useAsInput = null)
    {
        Parameters = parameters;
        _useAsInput = useAsInput;
        Prompt = parameters.Prompt;
        var modelShort = parameters.Model.Contains('/')
            ? parameters.Model[(parameters.Model.LastIndexOf('/') + 1)..]
            : parameters.Model;
        MetaLine = $"{modelShort} · {parameters.AspectRatio} · seed {parameters.Seed}";
    }

    [RelayCommand]
    private void Cancel() => Cts.Cancel();

    [RelayCommand]
    private void OpenImage()
    {
        if (ResultPath is null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ResultPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenImage failed for '{ResultPath}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (ResultPath is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{ResultPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ShowInFolder failed for '{ResultPath}': {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UseAsInput()
    {
        if (_useAsInput is null || ResultPath is null) return;
        await _useAsInput(ResultPath);
    }
}
