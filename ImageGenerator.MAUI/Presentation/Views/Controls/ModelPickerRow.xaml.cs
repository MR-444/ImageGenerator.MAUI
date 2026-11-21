using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;

namespace ImageGenerator.MAUI.Presentation.Views.Controls;

/// <summary>
/// Provides a reusable view component for choosing a model from a single provider, exposing bindable inputs for the catalog, the current choice, refresh execution, and user-facing feedback.
/// </summary>
public partial class ModelPickerRow
{
    /// <summary>
    /// The whole decision: provider, model, list, refresh, status, helper. When it is set it wins over
    /// the seven properties below, which stay for the pages that have not moved over yet.
    ///
    /// <para>
    /// Both shapes resolve through the same elements — nothing in the markup is written twice — because a
    /// control carrying two copies of itself is the duplication this control was extracted to remove, and
    /// the copy kept for migration is the one people forget to delete.
    /// </para>
    /// </summary>
    public static readonly BindableProperty ChoiceProperty = BindableProperty.Create(
        nameof(Choice), typeof(ModelChoiceState), typeof(ModelPickerRow),
        propertyChanged: (b, o, n) => ((ModelPickerRow)b).OnChoiceChanged((ModelChoiceState?)o, (ModelChoiceState?)n));
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

    /// <summary>
    /// True while the host is busy with its own call. Greys out the picker and Refresh, which is what
    /// every host hand-rolled as IsEnabled + InverseBool before this control existed.
    /// </summary>
    public static readonly BindableProperty IsBusyProperty = BindableProperty.Create(
        nameof(IsBusy), typeof(bool), typeof(ModelPickerRow), false);

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

    /// <summary>Automation id of the provider picker; only rendered when a <see cref="Choice"/> offers
    /// more than one provider. See <see cref="ModelPickerAutomationIdProperty"/> for why the page owns it.</summary>
    public static readonly BindableProperty ProviderPickerAutomationIdProperty = BindableProperty.Create(
        nameof(ProviderPickerAutomationId), typeof(string), typeof(ModelPickerRow), string.Empty);

    /// <summary>Automation id of the helper sentence. Pages had a label of their own under the picker and
    /// UI tests that read it; the row says the same thing, so the handle moves here rather than dying. See
    /// <see cref="ModelPickerAutomationIdProperty"/> for why the page owns it.</summary>
    public static readonly BindableProperty HelperAutomationIdProperty = BindableProperty.Create(
        nameof(HelperAutomationId), typeof(string), typeof(ModelPickerRow), string.Empty);

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

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
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

    public string ProviderPickerAutomationId
    {
        get => (string)GetValue(ProviderPickerAutomationIdProperty);
        set => SetValue(ProviderPickerAutomationIdProperty, value);
    }

    public string HelperAutomationId
    {
        get => (string)GetValue(HelperAutomationIdProperty);
        set => SetValue(HelperAutomationIdProperty, value);
    }

    public ModelChoiceState Choice
    {
        get => (ModelChoiceState)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    // ── what the elements actually bind to ────────────────────────────────────────────
    //
    // Reads of either the choice or the old per-property shape, so one set of elements serves both. They
    // are pure functions of state — no copies, no mirroring, nothing that can drift out of sync — and the
    // ObservableCollection a choice hands over is the coordinator's own instance, which is what lets a
    // refresh arrive in the picker without this control noticing anything at all.

    /// <summary>A provider picker is only worth a row when there is more than one provider to choose;</summary>
    public bool ShowProviderPicker => Choice is { Providers.Count: > 1 };

    /// <summary>
    /// The provider picker's entries as words. MAUI's Picker has no item template in this version, so the
    /// row spells the providers out itself — through <see cref="ModelProviderLabels"/>, the one place that
    /// knows what a provider is called, rather than letting an enum ToString itself into "LocalOllama".
    /// </summary>
    public IReadOnlyList<string> ProviderLabels =>
        Choice?.Providers.Select(ModelProviderLabels.ForList).ToArray() ?? (IReadOnlyList<string>)NoLabels;

    /// <summary>
    /// The picker's selection, as the position the control understands. An index rather than the label
    /// string, so that two providers ever spelled alike cannot select each other.
    /// </summary>
    public int ProviderIndex
    {
        get => Choice?.IndexOfProvider(Choice.SelectedProvider) ?? -1;
        set
        {
            if (Choice == null || value < 0 || value >= Choice.Providers.Count) return;
            Choice.SelectedProvider = Choice.Providers[value];
        }
    }

    // An array, not a collection expression: the WinRT analyzer needs a concrete type behind a read-only
    // interface (CsWinRT1032).
    private static readonly string[] NoLabels = [];

    public IEnumerable ModelsSource => (IEnumerable?)Choice?.Models ?? Models;

    public object PickedModel
    {
        get => (object?)Choice?.SelectedModel ?? SelectedModel;
        set
        {
            if (Choice != null) Choice.SelectedModel = value as string;
            else SelectedModel = value;
        }
    }

    public ICommand RefreshModelsCommand => Choice?.RefreshCommand ?? RefreshCommand;

    public string ReportedStatus => Choice?.Status ?? Status;

    public string ReportedHelper => Choice != null
        ? Choice.Helper ?? string.Empty
        : Helper;

    /// <summary>Labels for the provider picker's entries come from the converter in application
    /// resources, so the spelling of a provider has one owner and no page can write "LocalOllama" again.</summary>
    private void OnChoiceChanged(ModelChoiceState? previous, ModelChoiceState? current)
    {
        if (previous != null) previous.PropertyChanged -= OnChoicePropertyChanged;
        if (current != null) current.PropertyChanged += OnChoicePropertyChanged;
        RaiseEffective();
    }

    private void OnChoicePropertyChanged(object? sender, PropertyChangedEventArgs e) => RaiseEffective(e.PropertyName);

    private void RaiseEffective(string? except = null)
    {
        foreach (var name in new[]
                 {
                     nameof(ShowProviderPicker), nameof(ProviderLabels),
                     nameof(ProviderIndex), nameof(ModelsSource), nameof(PickedModel),
                     nameof(RefreshModelsCommand), nameof(ReportedStatus), nameof(ReportedHelper),
                 })
        {
            if (name != except) OnPropertyChanged(name);
        }
    }
}
