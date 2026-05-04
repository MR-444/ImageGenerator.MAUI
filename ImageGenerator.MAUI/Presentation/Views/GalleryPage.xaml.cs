using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class GalleryPage
{
    private readonly GalleryViewModel _viewModel;

    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            // async void: any escape here would crash the dispatcher with no managed
            // exception entry on disk. Log explicitly.
            CrashLogger.Log("GalleryPage.OnAppearing", ex);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        try
        {
            _viewModel.OnDisappearing();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("GalleryPage.OnDisappearing", ex);
        }
    }

    private void OnTileTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            // The Image's BindingContext inside the DataTemplate is the GalleryItem; pull it
            // off the sender rather than relying on cross-DataTemplate binding paths.
            if (sender is BindableObject bindable && bindable.BindingContext is GalleryItem item)
            {
                _viewModel.OpenInViewerCommand.Execute(item);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("GalleryPage.OnTileTapped", ex);
        }
    }

    private async void OnShowMetadataClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Button { CommandParameter: GalleryItem item })
            {
                // Navigate to the detail page with the file path as a Shell route parameter.
                // The page's [QueryProperty(NavPath, "path")] receives it on appearing.
                var encoded = Uri.EscapeDataString(item.FilePath);
                await Shell.Current.GoToAsync($"detail?path={encoded}");
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("GalleryPage.OnShowMetadataClicked", ex);
        }
    }
}
