using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class MainPage
{
    // Notice we inject GeneratorViewModel from the DI container:
    public MainPage(GeneratorViewModel viewModel)
    {
        InitializeComponent();
            
        // Assign the BindingContext to the injected viewModel:
        BindingContext = viewModel;
    }
}