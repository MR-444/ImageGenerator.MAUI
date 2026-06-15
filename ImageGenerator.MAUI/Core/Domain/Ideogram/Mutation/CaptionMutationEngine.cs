using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Drives a single mutation run: turns one base caption into <c>N</c> mutated caption strings under a strict
/// <c>(1+λ)</c> scheme — every variant applies <b>exactly one</b> operator to the <b>original</b> base (never a
/// chain), so any visible difference traces to a single cause. The run pins one <see cref="MutationAxis"/> and
/// only that axis's operators run. Output is fully reproducible: a single run-wide <see cref="Random"/> seeded
/// from <see cref="MutationRunConfig.Seed"/> is the only entropy source, and it advances exactly once per
/// variant slot (the recorded <see cref="MutationVariant.SubSeed"/>) regardless of how many internal attempts a
/// slot needs. The produced strings are the canonical compact captions the existing batch pipeline consumes;
/// this engine never renders, scores, or selects — the human is the fitness function.
/// </summary>
public sealed class CaptionMutationEngine
{
    private const int MaxCount = 100;            // hard ceiling on variants per run (one render each is a lot)
    private const int MinCount = 0;              // Count <= 0 ⇒ no mutated variants (reference may still emit)
    private const int MaxAttemptsPerVariant = 20; // bound on operator resampling per slot — best-effort, no hang

    private readonly IReadOnlyList<ICaptionOperator> _catalog;

    /// <summary>Engine over the full built-in operator set — the default for production wiring.</summary>
    public CaptionMutationEngine() : this(DefaultOperators) { }

    /// <summary>Engine over a caller-supplied operator catalog — tests inject subsets, spies, or stubs.</summary>
    public CaptionMutationEngine(IReadOnlyList<ICaptionOperator> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// The eight built-in operators in a fixed declared order (3 LOOK, then 5 SCENE). The order is part of the
    /// determinism contract: operator selection draws an index into the pinned-axis slice, so reordering would
    /// change which operator a given seed picks.
    /// </summary>
    public static IReadOnlyList<ICaptionOperator> DefaultOperators { get; } =
    [
        new SwapStyleOperator(),
        new BlendStyleOperator(),
        new ApplyOrnamentKitOperator(),
        new MutateBboxOperator(),
        new MutatePaletteOperator(),
        new RemoveElementOperator(),
        new AddElementOperator(),
        new SwapElementDescOperator()
    ];

    /// <summary>
    /// Produces the run's variants. When <see cref="MutationRunConfig.IncludeBaseAsReference"/> is set, the
    /// unmutated base is emitted first as "variant 0" (<see cref="MutationVariant.OperatorName"/> <c>null</c>).
    /// Each subsequent slot picks one pinned-axis operator at random and applies it to the original base,
    /// resampling up to an internal cap when the operator reports no legal mutation; a slot that exhausts its
    /// attempts is skipped (best-effort) and recorded in the drop log. Variant count may therefore be less than
    /// <see cref="MutationRunConfig.Count"/>.
    /// </summary>
    public MutationRunResult Generate(V4JsonPrompt baseCaption, MutationRunConfig config, MutationLibrary library)
    {
        ArgumentNullException.ThrowIfNull(baseCaption);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(library);

        var variants = new List<MutationVariant>();
        var dropLog = new List<string>();

        // Variant 0: the reference base, routed through the canonical serializer so it is byte-canonical like
        // every mutated variant. The engine does not validate the base — it is the caller's input.
        if (config.IncludeBaseAsReference)
            variants.Add(new MutationVariant(V4JsonPromptSerializer.Serialize(baseCaption), null, config.Seed));

        // One run-wide context: the slot-tag map is resolved once from the base (operators read tags from here,
        // never from a clone, since the clone path strips Element.SlotTag).
        var tags = SlotTagger.Resolve(baseCaption);
        var context = new MutationContext(
            config.TargetWidth, config.TargetHeight, tags, library, config.Strength, config.PinnedStyleName);

        var axisOperators = _catalog.Where(o => o.Axis == config.Axis).ToList();

        // Pinning a style is a LOOK-only directive: restrict the run to the style swap so every variant
        // becomes exactly the chosen style (SwapStyle reads the pinned name from the context). Identical
        // swaps collapse to one at the dispatch dedup — "apply this style" is a single transformation.
        if (config.Axis == MutationAxis.Look && !string.IsNullOrWhiteSpace(config.PinnedStyleName))
            axisOperators = axisOperators.Where(o => o.Name == "SwapStyle").ToList();
        var count = Math.Clamp(config.Count, MinCount, MaxCount);
        var master = new Random(config.Seed);

        for (var i = 0; i < count; i++)
        {
            // Exactly one master draw per slot — keeps the recorded SubSeed stable no matter how many attempts a
            // slot consumes, and makes the whole sequence reproducible.
            var subSeed = master.Next();

            if (axisOperators.Count == 0)
            {
                dropLog.Add($"variant {i}: no operators for axis {config.Axis}");
                continue;
            }

            var variantRng = new Random(subSeed);
            var emitted = false;

            for (var attempt = 0; attempt < MaxAttemptsPerVariant; attempt++)
            {
                var op = axisOperators[variantRng.Next(axisOperators.Count)];
                var mutated = op.Apply(baseCaption, variantRng, context);
                if (mutated is null)
                {
                    dropLog.Add($"variant {i} attempt {attempt}: {op.Name} produced no legal mutation");
                    continue;
                }

                variants.Add(new MutationVariant(V4JsonPromptSerializer.Serialize(mutated), op.Name, subSeed));
                emitted = true;
                break;
            }

            if (!emitted)
                dropLog.Add($"variant {i}: exhausted {MaxAttemptsPerVariant} attempts on axis {config.Axis}");
        }

        return new MutationRunResult(variants, dropLog);
    }
}
