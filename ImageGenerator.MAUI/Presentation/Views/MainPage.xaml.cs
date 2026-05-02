using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class MainPage
{
    private readonly GeneratorViewModel _viewModel;

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
}