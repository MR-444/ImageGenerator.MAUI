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

    // Live sampler progress (ComfyUI ws). HasProgress gates the card's ProgressBar: it flips
    // on with the first percent report and off again with the final outcome.
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _hasProgress;

    // CivitAI posting is a post-save side effect with its own status line on the job card —
    // it never touches StatusKind, so a failed post can't make a saved image look failed.
    [ObservableProperty] private string? _civitaiStatusMessage;
    [ObservableProperty] private string? _civitaiPostUrl;

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
    private void Cancel()
    {
        // The owning ViewModel disposes Cts in RunJobAsync's finally; if the user clicks Cancel
        // after completion (binding-update lag could allow it briefly), Cancel() throws ODE.
        try { Cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

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
            // explorer.exe /select,<path> opens the parent folder with the file highlighted.
            // ArgumentList handles quoting/escaping internally so a filename containing a quote
            // can't break out of the argument — mirrors Infrastructure/Services/FileLauncher.cs.
            var psi = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = true
            };
            psi.ArgumentList.Add($"/select,{ResultPath}");
            Process.Start(psi);
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

    [RelayCommand]
    private void OpenCivitaiPost()
    {
        if (CivitaiPostUrl is null) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = CivitaiPostUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OpenCivitaiPost failed for '{CivitaiPostUrl}': {ex.Message}");
        }
    }
}
