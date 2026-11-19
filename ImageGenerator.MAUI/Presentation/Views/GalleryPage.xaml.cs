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

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadOnAppearingAsync();
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

    private void OnGalleryHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is CollectionView view)
            ApplyGalleryGridLayout(view);
    }

    private void OnGallerySizeChanged(object? sender, EventArgs e)
    {
        if (sender is CollectionView view)
            ApplyGalleryGridLayout(view);
    }

    private void ApplyGalleryGridLayout(CollectionView view)
    {
        if (view.Width <= 0 || view.ItemsLayout is not GridItemsLayout layout) return;

        const double spacing = 12;
        const double scrollbarAndRoundingGuard = 18;

        var span = view.Width switch
        {
            < 520 => 2,
            < 760 => 3,
            < 1280 => 4,
            _ => 5
        };

        if (layout.Span != span)
            layout.Span = span;

        var tileWidth = Math.Floor((view.Width - (span - 1) * spacing - scrollbarAndRoundingGuard) / span);
        if (tileWidth < 80) tileWidth = 80;

        var imageHeight = Math.Min(tileWidth - 16, 200);
        var tileHeight = Math.Max(180, imageHeight + 92);

#if WINDOWS
        ApplyWindowsUniformGridLayout(view, span, tileWidth, tileHeight, spacing);
#endif
    }

#if WINDOWS
    private void ApplyWindowsUniformGridLayout(
        CollectionView view,
        int span,
        double tileWidth,
        double tileHeight,
        double spacing)
    {
        var platformView = view.Handler?.PlatformView as Microsoft.UI.Xaml.DependencyObject;
        if (platformView is null) return;

        Microsoft.UI.Xaml.Controls.UniformGridLayout? uniformGrid = null;

        if (platformView is Microsoft.UI.Xaml.Controls.ItemsView itemsView)
        {
            uniformGrid = itemsView.Layout as Microsoft.UI.Xaml.Controls.UniformGridLayout;
        }
        else if (FindDescendant<Microsoft.UI.Xaml.Controls.ItemsRepeater>(platformView) is { } repeater)
        {
            uniformGrid = repeater.Layout as Microsoft.UI.Xaml.Controls.UniformGridLayout;
        }

        if (uniformGrid is null) return;

        uniformGrid.MaximumRowsOrColumns = span;
        uniformGrid.MinItemWidth = tileWidth;
        uniformGrid.MinItemHeight = tileHeight;
        uniformGrid.MinColumnSpacing = spacing;
        uniformGrid.MinRowSpacing = spacing;
        uniformGrid.ItemsJustification = Microsoft.UI.Xaml.Controls.UniformGridLayoutItemsJustification.Start;
        uniformGrid.ItemsStretch = Microsoft.UI.Xaml.Controls.UniformGridLayoutItemsStretch.None;
    }

    private static T? FindDescendant<T>(Microsoft.UI.Xaml.DependencyObject root)
        where T : Microsoft.UI.Xaml.DependencyObject
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (FindDescendant<T>(child) is { } nested) return nested;
        }
        return null;
    }
#endif

    private async void OnShowMetadataClicked(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Button { CommandParameter: GalleryItem item })
            {
                var encoded = Uri.EscapeDataString(item.FilePath);
                await Shell.Current.GoToAsync($"detail?path={encoded}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryPage.{Op}", "OnShowMetadataClicked");
        }
    }
    
    private async Task LoadOnAppearingAsync()
    {
        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GalleryPage.{Op}", nameof(OnAppearing));
        }
    }
}