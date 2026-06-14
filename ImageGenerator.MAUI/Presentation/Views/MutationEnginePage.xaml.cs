using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class MutationEnginePage
{
    private readonly MutationEngineViewModel _viewModel;

    public MutationEnginePage(MutationEngineViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    // Resolve the base + slot review here: Shell shows the page after the generator stashed the
    // typed hand-off, and Initialize touches bindable state.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Initialize();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch
        {
            // Back is best-effort; a failed pop just leaves the user on the page.
        }
    }
}
