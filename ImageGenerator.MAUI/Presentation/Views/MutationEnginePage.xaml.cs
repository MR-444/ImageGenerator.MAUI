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
        ApplyModeButtonVisuals();
        // Best-effort: fill the library-backed pickers (anchor presets + saved styles) without blocking
        // the page (the VM swallows + logs). Refreshing here picks up a style just saved in the gallery.
        _ = _viewModel.LoadLibraryAsync();
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

    private void OnSelectAiMode(object? sender, EventArgs e)
    {
        if (!_viewModel.IsMutating) _viewModel.IsAiMode = true;
        ApplyModeButtonVisuals();
    }

    private void OnSelectDeterministicMode(object? sender, EventArgs e)
    {
        if (!_viewModel.IsMutating && !_viewModel.IsBreedMode) _viewModel.IsAiMode = false;
        ApplyModeButtonVisuals();
    }

    private void ApplyModeButtonVisuals()
    {
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var activeBackground = Color.FromArgb(dark ? "#8EA6FF" : "#4F6BED");
        var activeText = Color.FromArgb(dark ? "#10131A" : "#FFFFFF");
        var inactiveText = Color.FromArgb(dark ? "#C8C8C8" : "#404040");

        AiModeButton.BackgroundColor = _viewModel.IsAiMode ? activeBackground : Colors.Transparent;
        AiModeButton.TextColor = _viewModel.IsAiMode ? activeText : inactiveText;
        DeterministicModeButton.BackgroundColor = _viewModel.IsDeterministicMode ? activeBackground : Colors.Transparent;
        DeterministicModeButton.TextColor = _viewModel.IsDeterministicMode ? activeText : inactiveText;
    }
}
