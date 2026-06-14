using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate.
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Drives a single caption-mutation run: take a base structured prompt, pin an axis (LOOK ⟂ SCENE),
/// pick N / strength / seed / slot tags, then turn the engine's variants into a batch the existing
/// pipeline renders. The page is a thin front for <see cref="CaptionMutationEngine"/> — this VM never
/// renders, scores, or selects; the human is the fitness function. Transient (fresh per navigation);
/// the singleton <see cref="GeneratorViewModel"/> holds the base hand-off and the batch coordinator.
/// </summary>
public partial class MutationEngineViewModel : ObservableObject
{
    /// <summary>Slot-review sentinel: leave <see cref="Element.SlotTag"/> null so the engine infers it.</summary>
    public const string AutoTag = "(auto / infer)";

    private const int MinCount = 1;
    private const int MaxCount = 100;       // mirrors CaptionMutationEngine's own ceiling
    private const int DefaultTargetSize = 1024;

    private readonly GeneratorViewModel? _generator;
    private readonly IMutationLibraryService _libraryService;
    private readonly CaptionMutationEngine _engine;
    private readonly IClipboardService? _clipboard;
    private readonly ILogger<MutationEngineViewModel> _logger;

    /// <summary>The typed base whose elements (and their slot tags) the run mutates.</summary>
    private V4JsonPrompt? _base;

    /// <summary>
    /// Optional generator so unit tests can construct the VM stand-alone (mirrors how the structure
    /// editor VM takes its generator). Navigation + batch dispatch need it; the engine seam does not.
    /// </summary>
    public MutationEngineViewModel(
        IMutationLibraryService libraryService,
        CaptionMutationEngine engine,
        ILogger<MutationEngineViewModel> logger,
        GeneratorViewModel? generator = null,
        IClipboardService? clipboard = null)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;
        _clipboard = clipboard;
    }

    // --- Run configuration (bound to the page) ---------------------------------------------------

    public IReadOnlyList<MutationAxis> AxisOptions { get; } = [MutationAxis.Look, MutationAxis.Scene];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScene))]
    [NotifyPropertyChangedFor(nameof(OperatorsForAxis))]
    private MutationAxis _selectedAxis = MutationAxis.Look;

    /// <summary>SCENE pins LOOK and mutates geometry — only then does bbox strength matter.</summary>
    public bool IsScene => SelectedAxis == MutationAxis.Scene;

    /// <summary>Read-only echo of which operators a run on the pinned axis will draw from.</summary>
    public IReadOnlyList<string> OperatorsForAxis =>
        CaptionMutationEngine.DefaultOperators
            .Where(o => o.Axis == SelectedAxis)
            .Select(o => o.Name)
            .ToList();

    [ObservableProperty]
    private int _count = 8;

    partial void OnCountChanged(int value)
    {
        var clamped = Math.Clamp(value, MinCount, MaxCount);
        if (clamped != value) Count = clamped;
    }

    public IReadOnlyList<MutationStrength> StrengthOptions { get; } =
        [MutationStrength.Subtle, MutationStrength.Moderate, MutationStrength.Bold];

    [ObservableProperty]
    private MutationStrength _selectedStrength = MutationStrength.Moderate;

    [ObservableProperty]
    private bool _includeBase = true;

    [ObservableProperty]
    private int _seed;

    [ObservableProperty]
    private int _targetWidth = DefaultTargetSize;

    [ObservableProperty]
    private int _targetHeight = DefaultTargetSize;

    /// <summary>Read-only reminder that every variant renders at the current generator settings.</summary>
    [ObservableProperty]
    private string _settingsEcho = string.Empty;

    /// <summary>Per-element slot-tag review; edits land on <see cref="Element.SlotTag"/> at run time.</summary>
    public ObservableCollection<MutationSlotReviewItem> SlotReview { get; } = [];

    /// <summary>False until a parseable base is loaded — gates the Generate button.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand))]
    private bool _hasBase;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private StatusKind _statusKind = StatusKind.None;

    // --- Lifecycle -------------------------------------------------------------------------------

    /// <summary>
    /// Called from the page's OnAppearing. Prefers the typed base the editor stashed (slot tags
    /// intact) over re-parsing the prompt box (tags only inferred). Consuming the hand-off clears it
    /// so a later visit doesn't resurrect a stale base.
    /// </summary>
    public void Initialize()
    {
        var pending = _generator?.PendingMutationBase;
        if (_generator is not null) _generator.PendingMutationBase = null;

        var promptJson = _generator?.Parameters.Prompt ?? string.Empty;
        var resolution = _generator?.Parameters.Resolution ?? string.Empty;
        var model = _generator?.Parameters.Model ?? string.Empty;

        SettingsEcho = string.IsNullOrWhiteSpace(model)
            ? string.Empty
            : $"Variants render at the current settings — model {model}, resolution {(string.IsNullOrWhiteSpace(resolution) ? "Auto" : resolution)}. Set a draft preset + low MP first.";

        InitializeFrom(pending, promptJson, resolution);
    }

    /// <summary>
    /// Shell-free core of <see cref="Initialize"/>: resolve the base (typed hand-off &gt; prompt
    /// string), default the seed + target AR, and build the slot-review rows from inferred tags.
    /// </summary>
    internal void InitializeFrom(V4JsonPrompt? pendingBase, string promptJson, string? resolution)
    {
        _base = pendingBase ?? TryParse(promptJson);
        HasBase = _base is not null;
        GenerateCommand.NotifyCanExecuteChanged();

        if (Seed == 0) Seed = Random.Shared.Next();
        (TargetWidth, TargetHeight) = ParseTarget(resolution);

        SlotReview.Clear();
        if (_base is null)
        {
            SetStatus("The prompt box isn't a structured prompt — open one in the editor first.", StatusKind.Warning);
            return;
        }

        var tags = SlotTagger.Resolve(_base);
        foreach (var element in _base.CompositionalDeconstruction?.Elements ?? [])
        {
            var inferred = tags.TryGetValue(element, out var t) ? t : AutoTag;
            SlotReview.Add(new MutationSlotReviewItem(element, Describe(element), inferred));
        }

        SetStatus($"Ready: {SlotReview.Count} element(s) to vary.", StatusKind.Info);
    }

    private static V4JsonPrompt? TryParse(string json)
    {
        try { return V4JsonPromptSerializer.Deserialize(json); }
        catch (V4JsonPromptParseException) { return null; }
    }

    /// <summary>Parse a "WIDTHxHEIGHT" resolution into a target AR; anything else falls back to square.</summary>
    private static (int width, int height) ParseTarget(string? resolution)
    {
        if (!string.IsNullOrWhiteSpace(resolution))
        {
            var x = resolution.IndexOf('x');
            if (x > 0 && x < resolution.Length - 1
                && int.TryParse(resolution[..x], NumberStyles.None, CultureInfo.InvariantCulture, out var w)
                && int.TryParse(resolution[(x + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
                && w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        return (DefaultTargetSize, DefaultTargetSize);
    }

    // --- Commands --------------------------------------------------------------------------------

    [RelayCommand]
    private void RandomizeSeed() => Seed = Random.Shared.Next();

    [RelayCommand]
    private async Task CopySeedAsync()
    {
        if (_clipboard is null) return;
        await _clipboard.SetTextAsync(Seed.ToString(CultureInfo.InvariantCulture));
    }

    private bool CanGenerate() => HasBase;

    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (_base is null)
        {
            SetStatus("Nothing to mutate — load a structured prompt first.", StatusKind.Warning);
            return;
        }

        try
        {
            var library = await _libraryService.LoadAsync();
            var result = Run(library);

            if (result.Variants.Count == 0)
            {
                var why = result.DropLog.Count > 0 ? result.DropLog[^1] : "no operator produced a legal mutation";
                SetStatus($"No variants produced ({why}). Try the other axis or a different base.", StatusKind.Error);
                return;
            }

            var prompts = result.Variants.Select(v => v.Caption).ToList();

            if (_generator is null) return; // stand-alone (tests): variants built, nothing to dispatch to.

            // Pop first so the queue fills on MainPage; RunBatchAsync owns its own re-entrancy guard.
            await Shell.Current.GoToAsync("..");
            await _generator.Batch.RunBatchAsync(prompts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MutationEngineVM.{Op}", "Generate");
            SetStatus($"Couldn't run the mutation: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>
    /// Shell-free engine seam: write the reviewed slot tags onto the base, then run. Returns the raw
    /// result so callers can surface the drop log. Tests assert determinism + slot write-through here.
    /// </summary>
    internal MutationRunResult Run(MutationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(_base);

        foreach (var item in SlotReview)
            item.Element.SlotTag = item.SelectedTag == AutoTag ? null : item.SelectedTag;

        var config = new MutationRunConfig
        {
            Axis = SelectedAxis,
            Count = Count,
            Seed = Seed,
            TargetWidth = TargetWidth,
            TargetHeight = TargetHeight,
            IncludeBaseAsReference = IncludeBase,
            Strength = SelectedStrength
        };

        return _engine.Generate(_base, config, library);
    }

    /// <summary>Test/host seam: set the base directly without a generator hand-off or Shell.</summary>
    internal void SetBaseForTest(V4JsonPrompt model) => InitializeFrom(model, string.Empty, null);

    /// <summary>One-line element label for the review list (matches the editor's element summary).</summary>
    private static string Describe(Element element)
    {
        var body = element.Type == Element.TextType && !string.IsNullOrWhiteSpace(element.Text)
            ? $"“{element.Text}”"
            : element.Desc;
        if (string.IsNullOrWhiteSpace(body)) body = "(empty)";
        return $"[{element.Type}] {body}";
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusMessage = message;
        StatusKind = kind;
    }
}

/// <summary>
/// One element's slot-tag review row. Holds a back-reference to the live <see cref="Element"/> so the
/// run can write the chosen tag straight onto <see cref="Element.SlotTag"/>. Top-level (not nested) so
/// the page's compiled <c>x:DataType</c> binding is straightforward.
/// </summary>
public partial class MutationSlotReviewItem : ObservableObject
{
    public MutationSlotReviewItem(Element element, string summary, string selectedTag)
    {
        Element = element;
        Summary = summary;
        _selectedTag = selectedTag;
    }

    public Element Element { get; }

    public string Summary { get; }

    public IReadOnlyList<string> Options { get; } = [MutationEngineViewModel.AutoTag, .. SlotTag.All];

    [ObservableProperty]
    private string _selectedTag;
}
