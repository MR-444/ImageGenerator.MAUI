using System.Collections;
using System.Windows.Input;

namespace ImageGenerator.MAUI.Presentation.Views.Controls;

/// <summary>
/// Provides a reusable view component for choosing a model from a single provider, exposing bindable inputs for the catalog, the current choice, refresh execution, and user-facing feedback.
/// </summary>
public partial class ModelPickerRow
{
    /// <summary>Field label above the picker, e.g. "Local model (Ollama)". Empty hides it.</summary>
    public static readonly BindableProperty TitleTextProperty = BindableProperty.Create(
        nameof(TitleText), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Placeholder shown while the catalog is empty, before the first Refresh.</summary>
    public static readonly BindableProperty PickerTitleProperty = BindableProperty.Create(
        nameof(PickerTitle), typeof(string), typeof(ModelPickerRow), "Refresh, then pick a model…");

    /// <summary>The catalog the picker lists - already filtered to what this role can use.</summary>
    public static readonly BindableProperty ModelsProperty = BindableProperty.Create(
        nameof(Models), typeof(IEnumerable), typeof(ModelPickerRow));

    /// <summary>
    /// The picked model. Two-way by default: the picker writes into it, and the page persists it.
    /// </summary>
    public static readonly BindableProperty SelectedModelProperty = BindableProperty.Create(
        nameof(SelectedModel), typeof(object), typeof(ModelPickerRow),
        defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Repopulates <see cref="Models"/> from the provider; owned by the page's role.</summary>
    public static readonly BindableProperty RefreshCommandProperty = BindableProperty.Create(
        nameof(RefreshCommand), typeof(ICommand), typeof(ModelPickerRow));

    /// <summary>One-line explanation under the picker. Empty hides the label.</summary>
    public static readonly BindableProperty HelperProperty = BindableProperty.Create(
        nameof(Helper), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Bindable property that stores the last catalog outcome for the provider; an empty string means there is nothing to report and the status label is hidden.</summary>
    public static readonly BindableProperty StatusProperty = BindableProperty.Create(
        nameof(Status), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Defines the automation identifier for the model picker, serving as the UI test handle for controls associated with the owning page.</summary>
    public static readonly BindableProperty ModelPickerAutomationIdProperty = BindableProperty.Create(
        nameof(ModelPickerAutomationId), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Automation id of the Refresh button; see <see cref="ModelPickerAutomationIdProperty"/>.</summary>
    public static readonly BindableProperty RefreshAutomationIdProperty = BindableProperty.Create(
        nameof(RefreshAutomationId), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Automation id of the status label; see <see cref="ModelPickerAutomationIdProperty"/>.</summary>
    public static readonly BindableProperty StatusAutomationIdProperty = BindableProperty.Create(
        nameof(StatusAutomationId), typeof(string), typeof(ModelPickerRow), string.Empty);

    public ModelPickerRow()
    {
        InitializeComponent();
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public string PickerTitle
    {
        get => (string)GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    public IEnumerable Models
    {
        get => (IEnumerable)GetValue(ModelsProperty);
        set => SetValue(ModelsProperty, value);
    }

    public object SelectedModel
    {
        get => GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
    }

    public ICommand RefreshCommand
    {
        get => (ICommand)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public string Helper
    {
        get => (string)GetValue(HelperProperty);
        set => SetValue(HelperProperty, value);
    }

    public string Status
    {
        get => (string)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public string ModelPickerAutomationId
    {
        get => (string)GetValue(ModelPickerAutomationIdProperty);
        set => SetValue(ModelPickerAutomationIdProperty, value);
    }

    public string RefreshAutomationId
    {
        get => (string)GetValue(RefreshAutomationIdProperty);
        set => SetValue(RefreshAutomationIdProperty, value);
    }

    public string StatusAutomationId
    {
        get => (string)GetValue(StatusAutomationIdProperty);
        set => SetValue(StatusAutomationIdProperty, value);
    }
}
