using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Sub-VM that owns the provider filter + model-picker state machine. Created and held by
/// GeneratorViewModel; bound from the Prompt &amp; Model card on MainPage.
///
/// Suppression flag: a model write that originates here (catalog application, saved-state
/// restore) sets <see cref="SuppressModelPersist"/> for the duration of the operation so the
/// host's PropertyChanged hook on Parameters.Model knows to skip the persist. User-driven
/// picks via the Picker leave the flag false and persist normally.
/// </summary>
public sealed partial class ProviderFilterCoordinator : ObservableObject
{
    public const string AllProvidersLabel = "All providers";

    [ObservableProperty]
    private List<ModelOption> _allModels = [];

    [ObservableProperty]
    private List<string> _providers = [AllProvidersLabel];

    [ObservableProperty]
    private string _selectedProvider = AllProvidersLabel;

    [ObservableProperty]
    private List<ModelOption> _filteredModels = [];

    [ObservableProperty]
    private ModelOption? _selectedModel;

    public bool SuppressModelPersist { get; private set; }

    private readonly Func<string> _currentModelAccessor;
    private readonly Action<string> _setParametersModel;
    private readonly Action<string?> _refreshCapabilities;

    public ProviderFilterCoordinator(
        IReadOnlyList<ModelOption> initialSeeds,
        Func<string> currentModelAccessor,
        Action<string> setParametersModel,
        Action<string?> refreshCapabilities)
    {
        _currentModelAccessor = currentModelAccessor ?? throw new ArgumentNullException(nameof(currentModelAccessor));
        _setParametersModel = setParametersModel ?? throw new ArgumentNullException(nameof(setParametersModel));
        _refreshCapabilities = refreshCapabilities ?? throw new ArgumentNullException(nameof(refreshCapabilities));

        _allModels = [.. initialSeeds];
        _providers = BuildProvidersFrom(initialSeeds);
        RecomputeFilteredModels();
    }

    partial void OnSelectedProviderChanged(string value) => RecomputeFilteredModels();

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        if (value != null && _currentModelAccessor() != value.Value)
        {
            _setParametersModel(value.Value);
        }
        _refreshCapabilities(value?.Value);
    }

    /// <summary>
    /// Apply a freshly-fetched (or freshly-hydrated-from-cache) model catalog. Wraps the
    /// reassignment in a persist-suppression window so the user's saved model doesn't get
    /// clobbered by the Picker's automatic "select first row" round-trip.
    /// </summary>
    public void ApplyCatalog(IReadOnlyList<ModelOption> mergedModels)
    {
        SuppressModelPersist = true;
        try
        {
            AllModels = [.. mergedModels];
            Providers = BuildProvidersFrom(mergedModels);
            RecomputeFilteredModels();
        }
        finally
        {
            SuppressModelPersist = false;
        }
    }

    /// <summary>
    /// Restore the saved model from Preferences (or Remix). Switch SelectedProvider first if
    /// the target model isn't in the active filter so FilteredModels contains it BEFORE
    /// SelectedModel updates, avoiding MAUI Picker rejections.
    /// </summary>
    public void RestoreSelectedModel(string savedModel)
    {
        if (string.IsNullOrEmpty(savedModel)) return;

        var match = FilteredModels.FirstOrDefault(m => m.Value == savedModel)
                 ?? AllModels.FirstOrDefault(m => m.Value == savedModel);
        if (match is null) return;

        SuppressModelPersist = true;
        try
        {
            if (SelectedProvider != AllProvidersLabel && SelectedProvider != match.Provider)
            {
                SelectedProvider = match.Provider;
            }
            SelectedModel = FilteredModels.FirstOrDefault(m => m.Value == match.Value) ?? match;
        }
        finally
        {
            SuppressModelPersist = false;
        }
    }

    /// <summary>
    /// Reconcile picker selection after a Parameters.Model change came from elsewhere
    /// (e.g. the new-Parameters propagation in the host's OnParametersChanged hook).
    /// </summary>
    public void SyncSelectionFromParameters(string modelValue)
    {
        var match = AllModels.FirstOrDefault(m => m.Value == modelValue);
        if (match == null) return;

        if (SelectedProvider != AllProvidersLabel && SelectedProvider != match.Provider)
        {
            SelectedProvider = match.Provider;
        }
        if (SelectedModel?.Value != match.Value)
        {
            SelectedModel = FilteredModels.FirstOrDefault(m => m.Value == match.Value) ?? match;
        }
    }

    private void RecomputeFilteredModels()
    {
        var list = SelectedProvider == AllProvidersLabel
            ? AllModels.OrderBy(m => m.Provider).ThenBy(m => m.Display).ToList()
            : AllModels.Where(m => m.Provider == SelectedProvider).OrderBy(m => m.Display).ToList();

        FilteredModels = list;

        if (SelectedModel is null || !list.Contains(SelectedModel))
        {
            SelectedModel = list.FirstOrDefault(m => m.Value == _currentModelAccessor()) ?? list.FirstOrDefault();
        }
    }

    private static List<string> BuildProvidersFrom(IEnumerable<ModelOption> models)
    {
        List<string> list =
        [
            AllProvidersLabel,
            .. models.Select(m => m.Provider).Distinct().OrderBy(p => p)
        ];
        return list;
    }
}
