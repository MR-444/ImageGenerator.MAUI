using ImageGenerator.MAUI.Presentation.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

// DataPackageOperation collides between MAUI (cross-platform) and WinRT (platform). The
// MAUI DragEventArgs.AcceptedOperation expects the MAUI type; the WinUI DragEventArgs
// inside PlatformArgs expects the WinRT type.
using MauiDataPackageOperation = Microsoft.Maui.Controls.DataPackageOperation;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class MainPage
{
    private readonly GeneratorViewModel _viewModel;

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