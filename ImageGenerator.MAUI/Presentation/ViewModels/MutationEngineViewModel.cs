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
    /// <summary>Slot-review sentinel (friendly): leave <see cref="Element.SlotTag"/> null so the engine
    /// infers it. Aliases <see cref="SlotTagDisplay.Auto"/> so the picker and the sentinel agree.</summary>
    public const string AutoTag = SlotTagDisplay.Auto;

    private const int MinCount = 1;
    private const int MaxCount = 100;       // mirrors CaptionMutationEngine's own ceiling
    private const int DefaultTargetSize = 1024;

    /// <summary>Bounded concurrency for the AI fan-out: one paid call per variant, a few at a time.</summary>
    private const int AiMaxConcurrency = 4;

    // Rough one-call cost estimates (USD) — input+output of a typical mutation turn at each tier's rate
    // (Sonnet $3/$15, Opus $5/$25 per MTok). Display only; the user sees N×rate before running.
    private const decimal SonnetPerCallUsd = 0.017m;
    private const decimal OpusPerCallUsd = 0.028m;

    private readonly GeneratorViewModel? _generator;
    private readonly IMutationLibraryService _libraryService;
    private readonly CaptionMutationEngine _engine;
    private readonly ICaptionMutationLlmService? _mutationLlm;
    private readonly IClipboardService? _clipboard;
    private readonly ILogger<MutationEngineViewModel> _logger;

    /// <summary>The typed base whose elements (and their slot tags) the run mutates.</summary>
    private V4JsonPrompt? _base;

    /// <summary>When set (handed off from the Gallery "Breed selected"), the AI mutate command breeds
    /// from these winners instead of mutating a single base. Consumed/reset on each appearance.</summary>
    private IReadOnlyList<V4JsonPrompt>? _breedSet;

    /// <summary>
    /// Canonical JSON of the currently-loaded base. The VM is a singleton, so a back-and-forth to
    /// MainPage re-runs Initialize with the same base — when the new base serializes identically we
    /// PRESERVE the run state (seed, slot-tag edits, …) instead of rebuilding it from scratch.
    /// </summary>
    private string? _lastBaseJson;

    /// <summary>
    /// Optional generator so unit tests can construct the VM stand-alone (mirrors how the structure
    /// editor VM takes its generator). Navigation + batch dispatch need it; the engine seam does not.
    /// </summary>
    public MutationEngineViewModel(
        IMutationLibraryService libraryService,
        CaptionMutationEngine engine,
        ILogger<MutationEngineViewModel> logger,
        GeneratorViewModel? generator = null,
        IClipboardService? clipboard = null,
        ICaptionMutationLlmService? mutationLlm = null)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _generator = generator;
        _clipboard = clipboard;
        _mutationLlm = mutationLlm;
    }

    // --- Run configuration (bound to the page) ---------------------------------------------------

    public IReadOnlyList<MutationAxis> AxisOptions { get; } = [MutationAxis.Look, MutationAxis.Scene];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScene))]
    [NotifyPropertyChangedFor(nameof(ShowStrength))]
    [NotifyPropertyChangedFor(nameof(ShowStylePin))]
    [NotifyPropertyChangedFor(nameof(OperatorsForAxis))]
    private MutationAxis _selectedAxis = MutationAxis.Look;

    /// <summary>SCENE pins LOOK and mutates geometry — only then does bbox strength matter.</summary>
    public bool IsScene => SelectedAxis == MutationAxis.Scene;

    /// <summary>Read-only echo of which operators a run on the pinned axis will draw from, in
    /// plain-English "what it does" form (the raw operator names are engine-internal jargon).</summary>
    public IReadOnlyList<string> OperatorsForAxis =>
        CaptionMutationEngine.DefaultOperators
            .Where(o => o.Axis == SelectedAxis)
            .Select(o => CaptionDiff.OperatorBlurb(o.Name))
            .ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostEstimate))]
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
    [NotifyCanExecuteChangedFor(nameof(MutateWithAiCommand))]
    private bool _hasBase;

    // --- AI (LLM) mutation ----------------------------------------------------------------------

    /// <summary>Off = deterministic engine (free, reproducible). On = LLM-driven semantic mutation.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeterministicMode))]
    [NotifyPropertyChangedFor(nameof(ShowStrength))]
    [NotifyPropertyChangedFor(nameof(ShowStylePin))]
    private bool _isAiMode;

    /// <summary>Inverse of <see cref="IsAiMode"/> — gates the deterministic-only controls.</summary>
    public bool IsDeterministicMode => !IsAiMode;

    /// <summary>Placement strength only matters for the deterministic SCENE axis.</summary>
    public bool ShowStrength => IsDeterministicMode && IsScene;

    /// <summary>The "swap to a specific style" picker only applies to the deterministic LOOK axis.</summary>
    public bool ShowStylePin => IsDeterministicMode && !IsScene;

    /// <summary>Picker sentinel for "let the engine draw a random style per variant" (maps to a null pin).</summary>
    public const string RandomStyleSentinel = "Any style (random)";

    /// <summary>"Any (random)" + every saved style name, for the LOOK "Swap to" picker; refreshed each
    /// appearance by <see cref="LoadLibraryAsync"/> so a style just saved in the gallery shows up.</summary>
    public ObservableCollection<string> StyleNames { get; } = [RandomStyleSentinel];

    /// <summary>Selected entry of <see cref="StyleNames"/>; the sentinel means random (no pin).</summary>
    [ObservableProperty]
    private string _selectedStyleName = RandomStyleSentinel;

    /// <summary>Free-text steer for the LLM, e.g. "make it winter", "1970s film look".</summary>
    [ObservableProperty]
    private string _steer = string.Empty;

    /// <summary>Named steer presets (bundled + user-editable) shown in the "Start from a preset…"
    /// picker; populated by <see cref="LoadAnchorPresetsAsync"/> on first appearance.</summary>
    public ObservableCollection<AnchorPreset> AnchorPresets { get; } = [];

    /// <summary>Picking a preset REPLACES the steer text with its starting direction (the user edits
    /// it afterward). Setting it to null (no selection) leaves the steer untouched.</summary>
    [ObservableProperty]
    private AnchorPreset? _selectedAnchorPreset;

    partial void OnSelectedAnchorPresetChanged(AnchorPreset? value)
    {
        if (value is not null) Steer = value.Steer;
    }

    public IReadOnlyList<ModelTier> ModelTierOptions { get; } = [ModelTier.Sonnet, ModelTier.Opus, ModelTier.Local];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CostEstimate))]
    [NotifyPropertyChangedFor(nameof(IsLocalTier))]
    private ModelTier _selectedModelTier = ModelTier.Sonnet;

    /// <summary>True when the Local (Ollama) tier is picked — reveals the inline Ollama model picker.</summary>
    public bool IsLocalTier => SelectedModelTier == ModelTier.Local;

    /// <summary>The host generator, exposed so the page can bind the Ollama model picker (which lives on
    /// the generator with the other server settings) without duplicating that state here.</summary>
    public GeneratorViewModel? Generator => _generator;

    /// <summary>True when the page was entered via the Gallery "Breed selected" hand-off — the AI mutate
    /// command breeds from the winners, and the page shows the breed banner.</summary>
    [ObservableProperty]
    private bool _isBreedMode;

    /// <summary>Banner text shown in breed mode (e.g. "Breeding from 3 winner(s) — …").</summary>
    [ObservableProperty]
    private string _breedSummary = string.Empty;

    /// <summary>N × per-tier rate, shown before running. Local is free.</summary>
    public string CostEstimate
    {
        get
        {
            var plural = Count == 1 ? "" : "s";
            if (SelectedModelTier == ModelTier.Local)
                return $"Free — local Ollama ({Count} call{plural}). Quality not guaranteed; verifies the round-trip.";

            var per = SelectedModelTier == ModelTier.Opus ? OpusPerCallUsd : SonnetPerCallUsd;
            var total = (per * Count).ToString("0.00", CultureInfo.InvariantCulture);
            return $"Estimated cost ≈ ${total} for {Count} variant{plural} on {SelectedModelTier} (one paid call each).";
        }
    }

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
        // Breed hand-off takes priority and is consumed first, so a later ordinary visit always
        // resets breed state (no stale winners). Breeding is an AI-only path; we seed the base from
        // the first winner so HasBase (the run gate) and the target-frame derivation still work.
        var breed = _generator?.PendingBreedSet;
        if (_generator is not null) _generator.PendingBreedSet = null;
        _breedSet = breed is { Count: > 0 } ? breed : null;
        IsBreedMode = _breedSet is not null;
        if (IsBreedMode)
        {
            IsAiMode = true;
            BreedSummary = $"Breeding from {_breedSet!.Count} winner(s) — set a steer + model, then run.";
        }

        var pending = _breedSet?[0] ?? _generator?.PendingMutationBase;
        if (_generator is not null) _generator.PendingMutationBase = null;

        var promptJson = _generator?.Parameters.Prompt ?? string.Empty;
        var resolution = _generator?.Parameters.Resolution ?? string.Empty;
        var aspectRatio = _generator?.Parameters.AspectRatio ?? string.Empty;
        var model = _generator?.Parameters.Model ?? string.Empty;

        SettingsEcho = string.IsNullOrWhiteSpace(model)
            ? string.Empty
            : $"Variants render at the current settings — model {model}, "
              + $"{(string.IsNullOrWhiteSpace(aspectRatio) ? "default AR" : aspectRatio)}, "
              + $"resolution {(string.IsNullOrWhiteSpace(resolution) ? "Auto" : resolution)}. "
              + "Set a draft preset + low MP first.";

        InitializeFrom(pending, promptJson, aspectRatio, resolution);
    }

    /// <summary>
    /// Fill the library-backed pickers from the JSON stores. Anchor presets load once (they don't change
    /// within a session); the LOOK "Swap to" style list is refreshed every appearance so a style just
    /// saved in the gallery shows up when the user returns. Best-effort: a load failure leaves the pickers
    /// at their defaults and is logged, never surfaced. Called from the page's OnAppearing.
    /// </summary>
    public async Task LoadLibraryAsync()
    {
        try
        {
            var library = await _libraryService.LoadAsync();

            if (AnchorPresets.Count == 0)
                foreach (var preset in library.AnchorPresets)
                    AnchorPresets.Add(preset);

            // Refresh the style names, keeping the current pick if it still exists (else fall back to random).
            var previous = SelectedStyleName;
            StyleNames.Clear();
            StyleNames.Add(RandomStyleSentinel);
            foreach (var fragment in library.StyleFragments)
                StyleNames.Add(fragment.Name);
            SelectedStyleName = StyleNames.Contains(previous) ? previous : RandomStyleSentinel;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MutationEngineVM.{Op}", "LoadLibrary");
        }
    }

    /// <summary>
    /// Shell-free core of <see cref="Initialize"/>: resolve the base (typed hand-off &gt; prompt
    /// string), default the seed + target frame, and build the slot-review rows from inferred tags.
    /// The target frame (the AR reference for SCENE bbox placement) follows the actual render aspect
    /// ratio; <paramref name="resolution"/> is only a fallback when the AR carries no ratio
    /// (e.g. "custom").
    /// </summary>
    internal void InitializeFrom(V4JsonPrompt? pendingBase, string promptJson, string? aspectRatio, string? resolution)
    {
        var newBase = pendingBase ?? TryParse(promptJson);
        var newJson = newBase is null ? null : V4JsonPromptSerializer.Serialize(newBase);

        // Same base as the last time the page appeared → keep the existing run state (seed, count,
        // axis, strength, and the user's slot-tag edits). Crucially, do NOT replace _base: the
        // SlotReview rows hold references to its Elements, and Run() writes tags back onto them.
        if (newBase is not null && newJson == _lastBaseJson && _base is not null)
        {
            HasBase = true;
            GenerateCommand.NotifyCanExecuteChanged();
            SetStatus($"Ready: {SlotReview.Count} element(s) to vary.", StatusKind.Info);
            return;
        }

        _base = newBase;
        _lastBaseJson = newJson;
        HasBase = _base is not null;
        GenerateCommand.NotifyCanExecuteChanged();

        if (Seed == 0) Seed = Random.Shared.Next();
        (TargetWidth, TargetHeight) = DeriveTarget(aspectRatio, resolution);

        SlotReview.Clear();
        if (_base is null)
        {
            SetStatus("The prompt box isn't a structured prompt — open one in the editor first.", StatusKind.Warning);
            return;
        }

        var tags = SlotTagger.Resolve(_base);
        foreach (var element in _base.CompositionalDeconstruction?.Elements ?? [])
        {
            // Pre-select the picker on the inferred tag's FRIENDLY label (or Auto when nothing
            // inferred), so each row visibly shows what the app guessed for that element.
            var inferred = SlotTagDisplay.ToFriendly(tags.TryGetValue(element, out var t) ? t : null);
            SlotReview.Add(new MutationSlotReviewItem(element, Describe(element), inferred));
        }

        SetStatus($"Ready: {SlotReview.Count} element(s) to vary.", StatusKind.Info);
    }

    private static V4JsonPrompt? TryParse(string json)
    {
        try { return V4JsonPromptSerializer.Deserialize(json); }
        catch (V4JsonPromptParseException) { return null; }
    }

    /// <summary>
    /// The target frame the SCENE bbox operator uses as its AR reference. Only the ratio matters
    /// (operators read height/width), so this follows the actual render aspect ratio — the AR that
    /// every variant renders at. Resolution is a fallback for ratio-less AR values ("custom",
    /// "match_input_image"); failing both, square.
    /// </summary>
    private static (int width, int height) DeriveTarget(string? aspectRatio, string? resolution) =>
        TryParseAspectRatio(aspectRatio, out var w, out var h)
            ? ScaleToCanonical(w, h)
            : ParseTarget(resolution);

    /// <summary>Read the leading "W:H" ratio every AR value starts with ("2:3 (Portrait Photo)",
    /// "16:9", "1:1 (Square)"). Ratio-less values ("custom", "match_input_image") return false.</summary>
    private static bool TryParseAspectRatio(string? aspectRatio, out int w, out int h)
    {
        w = h = 0;
        if (string.IsNullOrWhiteSpace(aspectRatio)) return false;
        var token = aspectRatio.Trim().Split(' ', 2)[0]; // "2:3 (Portrait Photo)" -> "2:3"
        var parts = token.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out w)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out h)
            && w > 0 && h > 0;
    }

    /// <summary>Scale a w:h ratio so the longer side is <see cref="DefaultTargetSize"/> (only the ratio
    /// is used downstream; this keeps the stored pair sane).</summary>
    private static (int width, int height) ScaleToCanonical(int w, int h) =>
        w >= h
            ? (DefaultTargetSize, Math.Max(1, (int)Math.Round((double)h / w * DefaultTargetSize)))
            : (Math.Max(1, (int)Math.Round((double)w / h * DefaultTargetSize)), DefaultTargetSize);

    /// <summary>Parse a "WIDTHxHEIGHT" resolution into a target frame; anything else falls back to square.</summary>
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

            // Drop byte-identical variants: the engine allows variant↔variant collisions (a blend/
            // swap can draw the same library entry twice), and two identical captions render the same
            // image twice and read as duplicate cards. Keep first occurrence — the base reference,
            // emitted first, is preserved.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unique = result.Variants.Where(v => seen.Add(v.Caption)).ToList();
            if (unique.Count < result.Variants.Count)
                _logger.LogInformation(
                    "Mutation run: {Unique} distinct of {Total} variants ({Dropped} duplicate captions skipped)",
                    unique.Count, result.Variants.Count, result.Variants.Count - unique.Count);

            var prompts = unique.Select(v => v.Caption).ToList();
            var labels = unique.Select(DescribeVariant).ToList();

            if (_generator is null) return; // stand-alone (tests): variants built, nothing to dispatch to.

            // Pin the render seed so EVERY variant (incl. the base reference) renders at the same
            // seed — then the only visible difference between cards is the mutation itself. The page's
            // Seed field (with Randomize/Copy) is the single knob; without this, RunBatchAsync re-rolls
            // per job whenever RandomizeSeed is on and the comparison is lost to seed noise.
            _generator.Parameters.Seed = Seed;
            _generator.Parameters.RandomizeSeed = false;

            // Pop first so the queue fills on MainPage; RunBatchAsync owns its own re-entrancy guard.
            await Shell.Current.GoToAsync("..");
            await _generator.Batch.RunBatchAsync(prompts, labels);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MutationEngineVM.{Op}", "Generate");
            SetStatus($"Couldn't run the mutation: {ex.Message}", StatusKind.Error);
        }
    }

    private bool CanMutateWithAi() => HasBase && _mutationLlm is not null;

    /// <summary>
    /// AI path: fan out N independent LLM calls (one validated variant each, bounded concurrency), then
    /// hand the successful captions to the SAME batch pipeline the deterministic path uses — render seed
    /// pinned so only the caption differs. In-flight paid calls are never aborted mid-run.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanMutateWithAi))]
    private async Task MutateWithAiAsync()
    {
        if (_base is null)
        {
            SetStatus("Nothing to mutate — load a structured prompt first.", StatusKind.Warning);
            return;
        }
        if (_mutationLlm is null)
        {
            SetStatus("AI mutation isn't available in this build.", StatusKind.Error);
            return;
        }

        var tier = SelectedModelTier;
        var steer = Steer ?? string.Empty;
        var baseCaption = _base;
        var count = Count;
        var breedSet = _breedSet;

        try
        {
            var verb = breedSet is { Count: > 0 } ? "breed" : "produce";
            SetStatus($"Asking {tier} to {verb} {count} variant{(count == 1 ? "" : "s")}…", StatusKind.Info);

            var results = breedSet is { Count: > 0 }
                ? await FanOutAsync(count, ConcurrencyFor(tier),
                    i => _mutationLlm.BreedAsync(breedSet, steer, i, tier))
                : await FanOutAsync(count, ConcurrencyFor(tier),
                    i => _mutationLlm.MutateAsync(baseCaption, steer, i, tier));
            await DispatchAiResultsAsync(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MutationEngineVM.{Op}", "MutateWithAi");
            SetStatus($"Couldn't run the AI mutation: {ex.Message}", StatusKind.Error);
        }
    }

    /// <summary>A local model on one GPU serves requests serially, so concurrent calls would queue
    /// server-side and trip the per-call timeout; run the Local tier one-at-a-time. Cloud tiers fan out.</summary>
    private static int ConcurrencyFor(ModelTier tier) => tier == ModelTier.Local ? 1 : AiMaxConcurrency;

    /// <summary>Run <paramref name="count"/> LLM calls with bounded concurrency, preserving index order.</summary>
    private static async Task<LlmVariantResult[]> FanOutAsync(
        int count, int maxConcurrency, Func<int, Task<LlmVariantResult>> call)
    {
        using var gate = new SemaphoreSlim(maxConcurrency);
        var tasks = new Task<LlmVariantResult>[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            tasks[i] = RunGatedAsync(gate, () => call(index));
        }
        return await Task.WhenAll(tasks);
    }

    private static async Task<LlmVariantResult> RunGatedAsync(SemaphoreSlim gate, Func<Task<LlmVariantResult>> call)
    {
        await gate.WaitAsync();
        try { return await call(); }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Keep only the successful variants, then serialize each caption + pair it with its label — the
    /// exact (prompts, labels) the batch pipeline expects. Static + internal so it's unit-testable
    /// without a generator.
    /// </summary>
    internal static (List<string> Prompts, List<string> Labels) BuildAiBatch(IReadOnlyList<LlmVariantResult> results)
    {
        var ok = results.Where(r => r.Success && r.Prompt is not null).ToList();
        var prompts = ok.Select(r => V4JsonPromptSerializer.Serialize(r.Prompt!)).ToList();
        var labels = ok.Select(r => r.Label ?? "AI variant").ToList();
        return (prompts, labels);
    }

    /// <summary>Turn successful AI variants into the seed-pinned render batch (shared handoff).</summary>
    private async Task DispatchAiResultsAsync(IReadOnlyList<LlmVariantResult> results)
    {
        var ok = results.Where(r => r.Success && r.Prompt is not null).ToList();
        if (ok.Count == 0)
        {
            var why = results.Select(r => r.Error).FirstOrDefault(e => !string.IsNullOrWhiteSpace(e))
                      ?? "no variants produced";
            SetStatus($"AI mutation produced nothing: {why}", StatusKind.Error);
            return;
        }

        var (prompts, labels) = BuildAiBatch(results);
        var failed = results.Count - ok.Count;

        if (_generator is null)
        {
            SetStatus($"{ok.Count} AI variant(s) ready.", StatusKind.Success);
            return; // stand-alone (tests): variants built, nothing to dispatch to.
        }

        if (failed > 0)
            _logger.LogInformation("AI mutation: {Ok} of {Total} variants succeeded ({Failed} failed)",
                ok.Count, results.Count, failed);

        // Same seed pin as the deterministic path: every variant renders at one seed so the only visible
        // difference between cards is the mutation itself.
        _generator.Parameters.Seed = Seed;
        _generator.Parameters.RandomizeSeed = false;

        await Shell.Current.GoToAsync("..");
        await _generator.Batch.RunBatchAsync(prompts, labels);
    }

    /// <summary>
    /// Shell-free engine seam: write the reviewed slot tags onto the base, then run. Returns the raw
    /// result so callers can surface the drop log. Tests assert determinism + slot write-through here.
    /// </summary>
    internal MutationRunResult Run(MutationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(_base);

        foreach (var item in SlotReview)
            item.Element.SlotTag = SlotTagDisplay.ToRaw(item.SelectedTag);

        var config = new MutationRunConfig
        {
            Axis = SelectedAxis,
            Count = Count,
            Seed = Seed,
            TargetWidth = TargetWidth,
            TargetHeight = TargetHeight,
            IncludeBaseAsReference = IncludeBase,
            Strength = SelectedStrength,
            // LOOK only, and the sentinel means "random" → no pin.
            PinnedStyleName = SelectedAxis == MutationAxis.Look && SelectedStyleName != RandomStyleSentinel
                ? SelectedStyleName
                : null
        };

        return _engine.Generate(_base, config, library);
    }

    /// <summary>Test/host seam: set the base directly without a generator hand-off or Shell.</summary>
    internal void SetBaseForTest(V4JsonPrompt model) => InitializeFrom(model, string.Empty, null, null);

    /// <summary>Test/host seam: enter breed mode with the given winners (mirrors the Gallery hand-off
    /// the singleton would otherwise consume in <see cref="Initialize"/>).</summary>
    internal void SetBreedSetForTest(IReadOnlyList<V4JsonPrompt> winners)
    {
        _breedSet = winners is { Count: > 0 } ? winners : null;
        IsBreedMode = _breedSet is not null;
        if (IsBreedMode)
        {
            IsAiMode = true;
            BreedSummary = $"Breeding from {_breedSet!.Count} winner(s) — set a steer + model, then run.";
            SetBaseForTest(_breedSet[0]);
        }
    }

    /// <summary>
    /// Job-card label for a variant: the unmutated base reads "Original (reference)"; a mutated
    /// variant gets a before→after summary of its single change (falling back to the operator name
    /// if the caption somehow can't be re-parsed).
    /// </summary>
    private string DescribeVariant(MutationVariant variant)
    {
        if (variant.OperatorName is null) return "Original (reference)";
        if (_base is null) return CaptionDiff.FriendlyOperator(variant.OperatorName);
        try
        {
            return CaptionDiff.Describe(_base, V4JsonPromptSerializer.Deserialize(variant.Caption), variant.OperatorName);
        }
        catch (V4JsonPromptParseException)
        {
            return CaptionDiff.FriendlyOperator(variant.OperatorName);
        }
    }

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

    public IReadOnlyList<string> Options { get; } = SlotTagDisplay.Options;

    [ObservableProperty]
    private string _selectedTag;
}
