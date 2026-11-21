using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// One role's choice of model: who answers, which model, and the state the picker row shows while the
/// user is deciding.
///
/// <para>
/// This is the object <c>ModelPickerRow</c> binds to. It exists because a provider picker, a model
/// picker, a refresh button and a status label are not four independent things: they are one decision
/// seen four times, and every page that hand-rolled them got a connection between them subtly wrong in a
/// different way — a model restored to a picker on unload and persisted a moment later as though the
/// user had chosen it, four pickers writing one shared setting, a refresh that cleared the pick the user
/// was looking at. Holding the decision in one type turns those connections from convention into code,
/// and code can be tested: this class touches no MAUI type, so its rules are ordinary unit tests rather
/// than markup strings.
/// </para>
/// </summary>
public sealed partial class ModelChoiceState : ObservableObject
{
    private readonly ModelRole _role;
    private readonly IUiStateStore _store;
    private readonly IReadOnlyDictionary<ModelProvider, IModelSource> _sources;
    private readonly Func<ModelProvider, string?>? _helperFor;

    /// <summary>Guards the write-back of a model this object just loaded. Showing a picker is not the
    /// user choosing: without this, opening a page rewrites the setting it came to display.</summary>
    private bool _suppressModelPersist;

    private IModelSource _source;

    public ModelChoiceState(
        ModelRole role,
        IUiStateStore store,
        IReadOnlyDictionary<ModelProvider, IModelSource> sources,
        ModelProvider defaultProvider,
        Func<ModelProvider, string?>? helperFor = null)
    {
        if (sources.Count == 0)
            throw new ArgumentException(
                "A role with no provider behind it has no model to offer.", nameof(sources));

        _role = role;
        _store = store;
        _sources = sources;
        _helperFor = helperFor;

        // Ordered by the enum, not by dictionary order: a picker whose entries reshuffle between
        // launches is a picker the user cannot learn. An array rather than a collection expression — the
        // WinRT analyzer needs the concrete type behind the read-only interface (CsWinRT1032).
        Providers = Enum.GetValues<ModelProvider>().Where(sources.ContainsKey).ToArray();

        var stored = store.LoadLlmProvider(role, defaultProvider);
        // A stored provider this role is not offered must not survive as a selection with no list behind
        // it; a role that gained or lost a provider should fall back, not render an empty row.
        _selectedProvider = Providers.Contains(stored) ? stored : Providers[0];

        _source = sources[_selectedProvider];
        Attach(_source);

        _selectedModel = store.LoadLlmModel(role, _selectedProvider);
        _status = _source.Status;
        RebuildModels();
    }

    /// <summary>The providers this role can choose between. Roles differ: an observer sends images and a
    /// text-only role does not, so they are not offered the same list.</summary>
    public IReadOnlyList<ModelProvider> Providers { get; }

    /// <summary>
    /// Who answers this role. The provider picker writes this directly, so everything that follows from
    /// changing provider happens in one place, below, whether the change came from a click or from code.
    /// </summary>
    [ObservableProperty] private ModelProvider _selectedProvider;

    /// <summary>Which model this role calls on the selected provider. Null means "nobody has picked
    /// one", which the service reports rather than guessing at.</summary>
    [ObservableProperty] private string? _selectedModel;

    /// <summary>What can be picked right now. Never empty merely because nothing has been refreshed: a
    /// saved pick is listed before the first <see cref="RefreshCommand"/> so the row can say what would
    /// actually be used.</summary>
    public ObservableCollection<string> Models { get; } = [];

    /// <summary>What the selected provider last said. Mirrors the source, per provider: an Anthropic
    /// failure must not be readable as the answer to an Ollama button.</summary>
    [ObservableProperty] private string? _status;

    /// <summary>The sentence under the pickers, if the host supplies one. Depends on the provider,
    /// because what is worth warning about differs: one side is free and local, the other bills.</summary>
    public string? Helper => _helperFor?.Invoke(SelectedProvider);

    /// <summary>Ask the selected provider what it has. Delegated entirely — which endpoint, what to page
    /// through, what to do with the answer belongs to the source, not to a role.</summary>
    [RelayCommand]
    private Task RefreshAsync() => _source.RefreshAsync();

    partial void OnSelectedProviderChanged(ModelProvider value)
    {
        if (!_sources.TryGetValue(value, out var source))
            throw new ArgumentException($"{_role} is not offered {value.ForList()}.", nameof(value));

        var previous = _source;
        Detach(previous);
        _source = source;
        Attach(source);

        _store.PersistLlmProvider(_role, value);

        // Re-read rather than carry over: a model id means nothing across providers. An Ollama tag is
        // not a Claude id, and handing an endpoint a name it cannot parse is a call that fails far from
        // the picker that caused it.
        _suppressModelPersist = true;
        try
        {
            SelectedModel = _store.LoadLlmModel(_role, value);
            Status = source.Status;
        }
        finally { _suppressModelPersist = false; }

        RebuildModels();
        OnPropertyChanged(nameof(Helper));
    }

    partial void OnSelectedModelChanged(string? value)
    {
        if (_suppressModelPersist) return;
        _store.PersistLlmModel(_role, SelectedProvider, value);
    }

    /// <summary>
    /// Re-reads the source's list into <see cref="Models"/>, keeping a pick the source no longer lists.
    ///
    /// <para>
    /// Two deliberate refusals. An empty list is not an instruction to clear the pick: a server with
    /// nothing installed looks exactly like a misconfigured URL, and clearing on that evidence turns a
    /// connectivity problem into a lost choice. And a pick missing from a non-empty list is kept as an
    /// entry rather than dropped, because silently selecting something else shows the user a model they
    /// never chose.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Not reconciling a vanished pick against the store is deliberate for now: the coordinator's Ollama
    /// refresh already drops every role's stale local tag, being the only place that can see all five
    /// local picks at once. While both exist, that method owns clearing a local pick; this class never
    /// fights it.
    /// </remarks>
    private void RebuildModels()
    {
        var keep = SelectedModel;

        Models.Clear();
        foreach (var model in _source.Models)
            Models.Add(model);

        if (!string.IsNullOrWhiteSpace(keep) && !Models.Contains(keep!))
            Models.Add(keep!);
    }

    private void Attach(IModelSource source)
    {
        source.ModelsChanged += OnSourceModelsChanged;
        source.StatusChanged += OnSourceStatusChanged;
    }

    private void Detach(IModelSource source)
    {
        source.ModelsChanged -= OnSourceModelsChanged;
        source.StatusChanged -= OnSourceStatusChanged;
    }

    private void OnSourceModelsChanged(object? sender, EventArgs e) => RebuildModels();

    private void OnSourceStatusChanged(object? sender, EventArgs e) => Status = _source.Status;
}
