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
        await _viewModel.LoadSavedTokenAsync();
        await _viewModel.LoadCachedCatalogAsync();
    }
}