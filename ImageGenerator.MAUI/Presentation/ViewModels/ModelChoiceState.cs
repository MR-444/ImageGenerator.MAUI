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
    private readonly Func<ModelProvider, string?>? _baseUrlFor;

    /// <summary>Guards the write-back of a model this object just loaded. Showing a picker is not the
    /// user choosing: without this, opening a page rewrites the setting it came to display.</summary>
    private bool _suppressModelPersist;

    private readonly ModelProvider[] _providers;
    private IModelSource _source;

    /// <param name="helperFor">One sentence per provider, or null for none.</param>
    /// <param name="baseUrlFor">Where the selected provider lives, for the roles that run against an
    /// address the user typed (Local) rather than one the product knows (Anthropic, OpenRouter).</param>
    public ModelChoiceState(
        ModelRole role,
        IUiStateStore store,
        IReadOnlyDictionary<ModelProvider, IModelSource> sources,
        ModelProvider defaultProvider,
        Func<ModelProvider, string?>? helperFor = null,
        Func<ModelProvider, string?>? baseUrlFor = null)
    {
        if (sources.Count == 0)
            throw new ArgumentException(
                "A role with no provider behind it has no model to offer.", nameof(sources));

        _role = role;
        _store = store;
        _sources = sources;
        _helperFor = helperFor;
        _baseUrlFor = baseUrlFor;

        // Ordered by the enum, not by dictionary order: a picker whose entries reshuffle between
        // launches is a picker the user cannot learn. An array rather than a collection expression — the
        // WinRT analyzer needs the concrete type behind the read-only interface (CsWinRT1032).
        _providers = Enum.GetValues<ModelProvider>().Where(sources.ContainsKey).ToArray();

        var stored = store.LoadLlmProvider(role, defaultProvider);
        // A stored provider this role is not offered must not survive as a selection with no list behind
        // it; a role that gained or lost a provider should fall back, not render an empty row.
        _selectedProvider = _providers.Contains(stored) ? stored : _providers[0];

        _source = sources[_selectedProvider];
        Attach(_source);

        _selectedModel = store.LoadLlmModel(role, _selectedProvider);
        _status = _source.Status;
        RebuildModels();
    }

    /// <summary>The providers this role can choose between. Roles differ: an observer sends images and a
    /// text-only role does not, so they are not offered the same list.</summary>
    public IReadOnlyList<ModelProvider> Providers => _providers;

    /// <summary>Where a provider sits in this role's list. The row binds a picker, and a picker speaks in
    /// positions; asking it to match on the spelled-out label instead would let two providers ever spelled
    /// alike select each other.</summary>
    public int IndexOfProvider(ModelProvider provider) => Array.IndexOf(_providers, provider);

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

    /// <summary>
    /// The call this selection amounts to, resolved the moment somebody asks to run it.
    ///
    /// <para>
    /// This replaces <c>GeneratorSettingsReader.MutationTarget(tier)</c> and its siblings. They had to
    /// exist because the picker, the persisted value and the model id were three separate memories that a
    /// tier name had to reconcile; with one selection there is nothing to reconcile, and the tier
    /// translation - the last place where "Sonnet" meant both a provider and a model id - goes with them.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Nothing picked yields <see cref="LlmTarget.HasModel"/> false rather than an exception, because the
    /// services already turn that into the "pick a model first" sentence, worded per provider. A second
    /// way to fail the same missing pick would be one more place for the two wordings to disagree.
    /// </remarks>
    public LlmTarget Target => new(SelectedProvider, SelectedModel ?? string.Empty,
        _baseUrlFor?.Invoke(SelectedProvider) ?? string.Empty);

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

        AskIfNothingToList();
    }

    /// <summary>
    /// Ask the provider that has just been chosen for its models, when it has none to show.
    ///
    /// <para>
    /// A hosted list is empty until somebody asks, and asking used to be a button two centimetres from an
    /// empty combo with nothing connecting the two. An untouched list and an empty answer look identical
    /// from the user's side of the screen, which is how "the refresh does nothing" gets reported when
    /// what happened was that nothing was ever asked.
    /// </para>
    /// <para>
    /// Only a switch counts, and only when there is nothing to list: a role opened on a provider is not
    /// an instruction to call anybody, and Local's list is already seeded from the cache plus the
    /// once-per-session load before anyone can switch to it.
    /// </para>
    /// </summary>
    private void AskIfNothingToList()
    {
        if (_source.Models.Count > 0) return;
        _ = AskAsync(_source);
    }

    private async Task AskAsync(IModelSource source)
    {
        try
        {
            await source.RefreshAsync();
        }
        catch (Exception ex)
        {
            // A source is contracted to report its own failures on its status, so this is only the
            // backstop for one that throws instead - and it says so on the row rather than leaving the
            // exception on a task nobody is watching, which would vanish the way this bug did.
            if (ReferenceEquals(source, _source))
                Status = $"Couldn't ask for models: {ex.Message}";
        }
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
