using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class GalleryPage
{
    private readonly GalleryViewModel _viewModel;
    private readonly ILogger<GalleryPage> _logger;

    public GalleryPage(GalleryViewModel viewModel, ILogger<GalleryPage> logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
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
            _logger.LogError(ex, "GalleryPage.{Op}", "OnAppearing");
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
            _logger.LogError(ex, "GalleryPage.{Op}", "OnDisappearing");
        }
    }

    private void OnOpenTileClicked(object? sender, EventArgs e)
    {
        try
        {
            // Tile tap now toggles multi-selection, so opening the viewer is an explicit button.
            if (sender is Button { CommandParameter: GalleryItem item })
            {
                _viewModel.OpenInViewerCommand.Execute(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryPage.{Op}", "OnOpenTileClicked");
        }
    }

    private async void OnPostToCivitaiClicked(object? sender, EventArgs e)
    {
        try
        {
            var count = _viewModel.SelectedItems.Count;
            if (count == 0) return;

            // Posting publishes immediately at generation time, but the Gallery batch drafts —
            // spell out exactly what happens so the user reviews before any upload runs.
            var confirmed = await DisplayAlertAsync(
                "Post to CivitAI?",
                $"This uploads the {count} selected image(s) into ONE draft post on your CivitAI " +
                "account. Nothing is published — open the draft to review and publish it on civitai.com.",
                "Post draft",
                "Cancel");

            if (confirmed)
            {
                await _viewModel.PostSelectedAsOnePostCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryPage.{Op}", "OnPostToCivitaiClicked");
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
            _logger.LogError(ex, "GalleryPage.{Op}", "OnShowMetadataClicked");
        }
    }
}
