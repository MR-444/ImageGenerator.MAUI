using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views;

/// <summary>
/// Set-once configuration: provider API tokens and the ComfyUI server URL. Binds the
/// singleton <see cref="GeneratorViewModel"/> directly — token state lives there because
/// generation validation (IsValid) depends on it, and a separate VM would just mirror it.
/// </summary>
public partial class SettingsPage
{
    private readonly GeneratorViewModel _viewModel;

    public SettingsPage(GeneratorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        ShowCategory(SettingsCategory.Appearance);
    }

    private enum SettingsCategory { Appearance, ApiKeys, Generation, AiServices, Output, Integrations }

    private void ShowCategory(SettingsCategory category)
    {
        AppearanceCard.IsVisible = category == SettingsCategory.Appearance;
        ApiTokensCard.IsVisible = category == SettingsCategory.ApiKeys;
        GenerationCard.IsVisible = category == SettingsCategory.Generation;
        OllamaCard.IsVisible = category == SettingsCategory.AiServices;
        OpenRouterCard.IsVisible = category == SettingsCategory.AiServices;
        OutputCard.IsVisible = category == SettingsCategory.Output;
        IntegrationsCard.IsVisible = category == SettingsCategory.Integrations;
    }

    private void OnAppearanceCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.Appearance);
    private void OnApiKeysCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.ApiKeys);
    private void OnGenerationCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.Generation);
    private void OnAiServicesCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.AiServices);
    private void OnOutputCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.Output);
    private void OnIntegrationsCategoryClicked(object? sender, EventArgs e) => ShowCategory(SettingsCategory.Integrations);

    // The view->VM half of the token Entry's split binding (see the XAML comment). Provider
    // switches are safe: the Picker swaps SelectedTokenProvider BEFORE the binding rewrites
    // the Entry text, so this echoes the new provider's own value (the [ObservableProperty]
    // equality check stops it).
    private void OnTokenTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_viewModel.SelectedTokenProvider is { } provider)
            provider.Value = e.NewTextValue ?? string.Empty;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
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
