using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Sub-VM that owns the image-prompt collection and the sticky-AR state machine.
/// Created and held by GeneratorViewModel; bound from the Input Image card on MainPage.
///
/// Intake: every file arriving through either entry point is shrunk to a payload a hosted API
/// takes (<see cref="ImagePayloadLimits.ModelInput"/>) before it joins the collection, so nothing
/// downstream ever sees a 36 MB photo.
///
/// AR sticky: tracks the user's last explicit AR pick so a model swap that lands on a model
/// supporting it can restore it; also auto-switches AR to "match_input_image" on 0→1 image
/// transitions and back to the preferred AR on 1→0. All automatic AR writes go through the
/// host's setAspectRatioProgrammatically callback; only the host's picker records a pick.
/// </summary>
public sealed partial class InputImagesCoordinator : ObservableObject
{
    public sealed record InputImageItem(string Base64, ImageSource? Preview, string FileName, string? SourcePath = null);

    private readonly Func<ModelCapabilities> _capsAccessor;
    private readonly Action<string> _setAspectRatioProgrammatically;
    private readonly Action _mirrorImagePromptsToParameters;
    private readonly Action<string, StatusKind> _setStatus;
    private readonly ILogger _logger;

    private int _lastImageCount;
    private string? _preferredAspectRatio;

    public ObservableCollection<InputImageItem> SelectedImages { get; } = [];

    public int InputImageCount => SelectedImages.Count;
    public bool CanAddImage => SelectedImages.Count < _capsAccessor().MaxImageInputs;
    public bool SupportsImagePromptStrength => _capsAccessor().ImagePromptStrength && SelectedImages.Count > 0;
    public string ImagePromptCardTitle => _capsAccessor().MaxImageInputs > 1
        ? $"Input Images (optional, up to {_capsAccessor().MaxImageInputs})"
        : "Input Image (optional)";

    public string? PreferredAspectRatio => _preferredAspectRatio;

    public InputImagesCoordinator(
        Func<ModelCapabilities> capsAccessor,
        Action<string> setAspectRatioProgrammatically,
        Action mirrorImagePromptsToParameters,
        Action<string, StatusKind> setStatus,
        ILogger logger,
        string? initialPreferredAspectRatio = null)
    {
        _capsAccessor = capsAccessor ?? throw new ArgumentNullException(nameof(capsAccessor));
        _setAspectRatioProgrammatically = setAspectRatioProgrammatically ?? throw new ArgumentNullException(nameof(setAspectRatioProgrammatically));
        _mirrorImagePromptsToParameters = mirrorImagePromptsToParameters ?? throw new ArgumentNullException(nameof(mirrorImagePromptsToParameters));
        _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferredAspectRatio = initialPreferredAspectRatio;

        SelectedImages.CollectionChanged += OnSelectedImagesChanged;
    }

    /// <summary>
    /// Host calls this from its Parameters.PropertyChanged hook when a non-programmatic AR
    /// write lands. The coordinator captures it for later restore on model swaps.
    /// </summary>
    public void RecordExplicitAspectRatioPick(string aspectRatio)
    {
        _preferredAspectRatio = aspectRatio;
    }

    /// <summary>
    /// Host calls this after the VM's Capabilities property changes so the coordinator's
    /// caps-derived computed properties refresh in the UI.
    /// </summary>
    public void OnCapabilitiesChanged()
    {
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(SupportsImagePromptStrength));
        OnPropertyChanged(nameof(ImagePromptCardTitle));
    }

    /// <summary>
    /// Host calls this from RefreshCapabilities to enforce the per-model image cap. Returns
    /// true if any images were dropped.
    /// </summary>
    public bool TruncateToMaxInputs(int maxImageInputs)
    {
        var dropped = false;
        while (SelectedImages.Count > maxImageInputs)
        {
            SelectedImages.RemoveAt(SelectedImages.Count - 1);
            dropped = true;
        }
        return dropped;
    }

    private void OnSelectedImagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Mirror into the domain-layer entity so the factory sees the same list.
        _mirrorImagePromptsToParameters();

        // Auto-select match_input_image on 0→1, fall back on 1→0. Models without the option
        // in their AR list (Flux 1.1 Pro/Pro Ultra) no-op the first branch. Both writes are
        // automatic, so they go through setAspectRatioProgrammatically and never become
        // _preferredAspectRatio.
        var count = SelectedImages.Count;
        var caps = _capsAccessor();
        if (_lastImageCount == 0 && count > 0 && caps.AspectRatios.Contains("match_input_image"))
        {
            _setAspectRatioProgrammatically("match_input_image");
        }
        else if (_lastImageCount > 0 && count == 0)
        {
            // Prefer the user's last explicit AR if it's still valid for the current model
            // and isn't itself "match_input_image"; otherwise fall back to the first concrete AR.
            var preferred = _preferredAspectRatio is { } pref && pref != "match_input_image"
                            && caps.AspectRatios.Contains(pref) ? pref : null;
            var fallback = preferred ?? caps.AspectRatios.FirstOrDefault(r => r != "match_input_image");
            if (fallback != null)
            {
                _setAspectRatioProgrammatically(fallback);
            }
        }
        _lastImageCount = count;

        OnPropertyChanged(nameof(InputImageCount));
        OnPropertyChanged(nameof(CanAddImage));
        OnPropertyChanged(nameof(SupportsImagePromptStrength));
    }

    [RelayCommand]
    private async Task AddImageAsync()
    {
        if (!CanAddImage)
        {
            _setStatus($"Maximum {_capsAccessor().MaxImageInputs} image(s) for this model.", StatusKind.Error);
            return;
        }

        _setStatus("Opening file picker…", StatusKind.Info);

        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Pick an image",
            FileTypes = FilePickerFileType.Images
        });

        if (result == null)
        {
            _setStatus(string.Empty, StatusKind.None);
            return;
        }

        if (SelectedImages.Any(i => string.Equals(i.SourcePath, result.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            _setStatus($"'{result.FileName}' is already in the list.", StatusKind.Warning);
            return;
        }

        try
        {
            await using var stream = await result.OpenReadAsync();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);

            await AttachAsync(
                memoryStream.ToArray(),
                result.FileName,
                result.FullPath,
                () => $"Added image: {result.FileName} ({SelectedImages.Count}/{_capsAccessor().MaxImageInputs})",
                StatusKind.Info);
        }
        catch (Exception ex)
        {
            _setStatus($"Error adding image: {ex.Message}", StatusKind.Error);
        }
    }

    [RelayCommand]
    private void RemoveImage(InputImageItem? item)
    {
        if (item is null) return;
        SelectedImages.Remove(item);
        _setStatus($"Removed {item.FileName}.", StatusKind.Info);
    }

    [RelayCommand]
    private void ClearImages()
    {
        SelectedImages.Clear();
        _setStatus("Input images cleared.", StatusKind.Info);
    }

    /// <summary>
    /// File-path entry point used by MainPage drag-drop and the "Use as input" gallery flow.
    /// </summary>
    public async Task AddAsInputAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            _setStatus("File not found.", StatusKind.Error);
            return;
        }
        if (SelectedImages.Any(i => string.Equals(i.SourcePath, filePath, StringComparison.OrdinalIgnoreCase)))
        {
            _setStatus($"'{Path.GetFileName(filePath)}' is already in the list.", StatusKind.Warning);
            return;
        }
        if (!CanAddImage)
        {
            _setStatus($"Maximum {_capsAccessor().MaxImageInputs} image(s) for this model.", StatusKind.Error);
            return;
        }

        try
        {
            await AttachAsync(
                await File.ReadAllBytesAsync(filePath),
                Path.GetFileName(filePath),
                filePath,
                () => "Added as input.",
                StatusKind.Success);
        }
        catch (Exception ex)
        {
            _setStatus($"Error using image as input: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>
    /// Shared tail of both add paths: reduce the file to a payload a hosted model can take, queue
    /// it (which mirrors it into the parameters), report. A file nothing can decode is refused here,
    /// while the user is still looking at the picker, rather than at generate time as a provider
    /// error nobody recognises.
    ///
    /// <paramref name="addedMessage"/> is built after the add so it can report the position in the
    /// list; <paramref name="kind"/> is the status colour that belongs to it.
    /// </summary>
    private async Task AttachAsync(
        byte[] imageBytes,
        string fileName,
        string? sourcePath,
        Func<string> addedMessage,
        StatusKind kind)
    {
        var prepared = await ImagePayloadNormalizer.PrepareAsync(imageBytes, ImagePayloadLimits.ModelInput);
        if (prepared.Bytes is null)
        {
            _setStatus(prepared.Error!, StatusKind.Error);
            return;
        }

        SelectedImages.Add(new InputImageItem(
            Convert.ToBase64String(prepared.Bytes),
            PreviewFor(prepared.Bytes),
            fileName,
            sourcePath));

        var message = addedMessage();
        if (prepared.Note is { } note)
        {
            // What goes out is not the file that came in — resized, re-encoded, or both — and the
            // model is about to work from that other picture, so say so where the add was reported.
            _logger.LogInformation("Input image re-encoded File={FileName} {Change}", fileName, note);
            message = $"{message} · {note}";
        }
        _setStatus(message, kind);
    }

    /// <summary>
    /// Thumbnail made from the bytes that will actually be sent rather than from the original file,
    /// so the list never decodes a 36 MB photo to draw a 40-pixel square. A thumbnail that cannot be
    /// made must not cost the user the attachment, so failure here is swallowed on purpose: the
    /// image stays queued, only its picture is missing.
    /// </summary>
    private ImageSource? PreviewFor(byte[] bytes)
    {
        try
        {
            return ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Input image preview failed Bytes={Bytes}", bytes.Length);
            return null;
        }
    }
}
