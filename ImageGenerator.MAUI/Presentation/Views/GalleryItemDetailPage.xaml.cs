using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

[QueryProperty(nameof(NavPath), "path")]
public partial class GalleryItemDetailPage
{
    private readonly GalleryItemDetailViewModel _viewModel;

    public GalleryItemDetailPage(GalleryItemDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>
    /// Receives the file path from the Shell route parameter (`detail?path=…`).
    /// Naming: avoid "Path" or "FilePath" to dodge collision with System.IO.Path
    /// and the VM's own FilePath property when XAML compiles bindings.
    /// </summary>
    public string? NavPath
    {
        set
        {
            try
            {
                if (string.IsNullOrEmpty(value)) return;
                _viewModel.FilePath = Uri.UnescapeDataString(value);
            }
            catch (Exception ex)
            {
                CrashLogger.Log("GalleryItemDetailPage.NavPath", ex);
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Log("GalleryItemDetailPage.OnAppearing", ex);
        }
    }
}
