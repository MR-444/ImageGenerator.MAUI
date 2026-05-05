using ImageGenerator.MAUI.Presentation.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

// DataPackageOperation collides between MAUI (cross-platform) and WinRT (platform). The
// MAUI DragEventArgs.AcceptedOperation expects the MAUI type; the WinUI DragEventArgs
// inside PlatformArgs expects the WinRT type.
using MauiDataPackageOperation = Microsoft.Maui.Controls.DataPackageOperation;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace ImageGenerator.MAUI.Presentation.Views;

[QueryProperty(nameof(AddInputPath), "addInput")]
public partial class MainPage
{
    private readonly GeneratorViewModel _viewModel;

    /// <summary>
    /// Set by Shell when the user navigates back from the gallery detail page via the
    /// "Use as input" button (which calls Shell.GoToAsync("//MainPage?addInput=…")).
    /// </summary>
    public string? AddInputPath
    {
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            var path = Uri.UnescapeDataString(value);
            // Fire-and-forget: AddAsInputAsync sets a status message internally and never
            // throws past its own catch. Awaiting from a property setter isn't possible.
            _ = _viewModel.AddAsInputAsync(path);
        }
    }

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    public MainPage(GeneratorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // async void: callees swallow internally today, but a future refactor that lets one
        // through would crash via SynchronizationContext. Keep a defensive net here.
        try
        {
            await _viewModel.LoadSavedTokenAsync();
            await _viewModel.LoadCachedCatalogAsync();
            _viewModel.LoadSavedUiState();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnAppearing failed: {ex.Message}");
        }
    }

    // Code-behind because RelativeSource lookups inside a CollectionView.ItemTemplate
    // don't reliably cross the DataTemplate scope under MAUI's compiled bindings.
    private void OnRemoveImageClicked(object sender, EventArgs e)
    {
        if (sender is Button { CommandParameter: GeneratorViewModel.InputImageItem item })
        {
            _viewModel.RemoveImageCommand.Execute(item);
        }
    }

    // Batch-from-textfile entry point. Code-behind orchestrates because DisplayAlertAsync
    // belongs to the Page (the VM owns the FilePicker call and the parser, both of which
    // are independently testable).
    private async void OnImportPromptsClicked(object sender, EventArgs e)
    {
        try
        {
            var prompts = await _viewModel.PickAndParsePromptsAsync();
            if (prompts is null || prompts.Count == 0) return;

            var modelName = _viewModel.SelectedModel?.Display ?? _viewModel.Parameters.Model;
            var confirm = await DisplayAlertAsync(
                "Run batch?",
                $"Submit {prompts.Count} prompts using {modelName}?\n\nCurrent settings (aspect ratio, format, etc.) will apply to every prompt.",
                "Run", "Cancel");
            if (!confirm) return;

            await _viewModel.RunBatchAsync(prompts);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnImportPromptsClicked failed: {ex.Message}");
            _viewModel.StatusMessage = $"Batch failed: {ex.Message}";
            _viewModel.StatusKind = StatusKind.Error;
        }
    }

    private async void OnAboutClicked(object sender, EventArgs e)
    {
        var message =
            $"Version {_viewModel.AppVersion}\n\n" +
            "A hobby MAUI desktop app for Replicate-based image generation.\n\n" +
            "MIT License\n" +
            "https://github.com/MR-444/ImageGenerator.MAUI";
        await DisplayAlertAsync("About AI Image Generator", message, "OK");
    }

    // Drag-and-drop for image-prompt input. MAUI's cross-platform DropGestureRecognizer
    // surfaces the gesture, but file paths require the WinUI DragEventArgs.DataView seen
    // through DropEventArgs.PlatformArgs.
    private void OnImageDragOver(object? sender, DragEventArgs e)
    {
        var winArgs = e.PlatformArgs?.DragEventArgs;
        if (winArgs?.DataView is { } dv && dv.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = MauiDataPackageOperation.Copy;
            winArgs.AcceptedOperation = WinDataPackageOperation.Copy;
            winArgs.DragUIOverride.Caption = "Add as input image";
        }
    }

    private async void OnImageDropped(object? sender, DropEventArgs e)
    {
        var dv = e.PlatformArgs?.DragEventArgs?.DataView;
        if (dv is null || !dv.Contains(StandardDataFormats.StorageItems)) return;

        try
        {
            var items = await dv.GetStorageItemsAsync();
            var imagePaths = items.OfType<StorageFile>()
                                  .Select(f => f.Path)
                                  .Where(p => ImageExtensions.Contains(Path.GetExtension(p)))
                                  .ToList();
            var skipped = items.Count - imagePaths.Count;

            foreach (var path in imagePaths)
            {
                await _viewModel.AddAsInputAsync(path);
            }

            if (skipped > 0)
            {
                _viewModel.StatusMessage = imagePaths.Count > 0
                    ? $"Added {imagePaths.Count} image(s); skipped {skipped} non-image file(s)."
                    : $"Skipped {skipped} non-image file(s).";
                _viewModel.StatusKind = StatusKind.Warning;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"OnImageDropped failed: {ex.Message}");
        }
    }
}