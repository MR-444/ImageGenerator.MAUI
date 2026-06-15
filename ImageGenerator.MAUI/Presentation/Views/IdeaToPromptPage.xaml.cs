using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

public partial class IdeaToPromptPage
{
    public IdeaToPromptPage(IdeaToPromptViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
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
