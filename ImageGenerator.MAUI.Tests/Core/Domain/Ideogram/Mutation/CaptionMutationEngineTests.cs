using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class CaptionMutationEngineTests
{
    private static MutationRunConfig Config(
        MutationAxis axis,
        int count,
        int seed = 7,
        bool includeBase = false,
        MutationStrength strength = MutationStrength.Moderate) =>
        new()
        {
            Axis = axis,
            Count = count,
            Seed = seed,
            TargetWidth = 1000,
            TargetHeight = 1000,
            IncludeBaseAsReference = includeBase,
            Strength = strength
        };

    // ---- Determinism / reproducibility -------------------------------------------------

    [Theory]
    [InlineData(MutationAxis.Look)]
    [InlineData(MutationAxis.Scene)]
    public void Generate_IsByteIdentical_ForSameBaseAndConfig(MutationAxis axis)
    {
        var engine = new CaptionMutationEngine();
        var config = Config(axis, 12, seed: 42, includeBase: true);

        var a = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());
        var b = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());

        a.Variants.Should().Equal(b.Variants);
        a.DropLog.Should().Equal(b.DropLog);
    }

    [Fact]
    public void Generate_RecordsStableSubSeeds_MatchingTheMasterSequence()
    {
        // A single always-succeeds operator means every slot emits exactly once, so the i-th mutated
        // variant's SubSeed equals the i-th draw of a fresh master Random(seed).
        var engine = new CaptionMutationEngine([new RecordingOperator(MutationAxis.Look, "L")]);
        var config = Config(MutationAxis.Look, 6, seed: 99, includeBase: false);

        var result = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());

        var master = new Random(99);
        var expected = Enumerable.Range(0, 6).Select(_ => master.Next()).ToList();
        result.Variants.Select(v => v.SubSeed).Should().Equal(expected);
    }

    [Fact]
    public void Generate_DiffersAcrossSeeds()
    {
        var engine = new CaptionMutationEngine();

        var a = engine.Generate(MutationTestData.BaseCaption(), Config(MutationAxis.Scene, 10, seed: 1), MutationTestData.Library());
        var b = engine.Generate(MutationTestData.BaseCaption(), Config(MutationAxis.Scene, 10, seed: 2), MutationTestData.Library());

        a.Variants.Select(v => v.Caption).Should().NotEqual(b.Variants.Select(v => v.Caption));
    }

    // ---- Pinned style (LOOK) -----------------------------------------------------------

    [Fact]
    public void Generate_PinnedStyle_RestylesEveryLookVariantToThatExactStyle()
    {
        var engine = new CaptionMutationEngine();
        var config = Config(MutationAxis.Look, 8, seed: 7) with { PinnedStyleName = "anime" };

        var result = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());
        var anime = MutationTestData.AnimeStyle();

        result.Variants.Should().NotBeEmpty();
        foreach (var variant in result.Variants)
        {
            variant.OperatorName.Should().Be("SwapStyle", "pinning restricts the LOOK run to the style swap");
            var caption = V4JsonPromptSerializer.Deserialize(variant.Caption);
            StyleMath.SameStyle(caption.StyleDescription, anime).Should().BeTrue();
        }
    }

    // ---- Count clamp / bounds ----------------------------------------------------------

    [Theory]
    [InlineData(101)]
    [InlineData(150)]
    [InlineData(1000)]
    public void Generate_ClampsCountToMax(int requested)
    {
        // A single always-succeeds operator isolates the clamp: every slot emits, so the only ceiling is 100.
        var engine = new CaptionMutationEngine([new RecordingOperator(MutationAxis.Look, "L")]);

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, requested), MutationTestData.Library());

        result.Variants.Should().HaveCount(100);
    }

    [Fact]
    public void Generate_CountZero_EmitsOnlyReferenceWhenIncluded()
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 0, includeBase: true), MutationTestData.Library());

        result.Variants.Should().ContainSingle();
        result.Variants[0].OperatorName.Should().BeNull();
    }

    [Fact]
    public void Generate_CountZero_NoReference_EmitsEmpty()
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 0, includeBase: false), MutationTestData.Library());

        result.Variants.Should().BeEmpty();
        result.DropLog.Should().BeEmpty();
    }

    [Fact]
    public void Generate_NegativeCount_TreatedAsZero()
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, -5, includeBase: false), MutationTestData.Library());

        result.Variants.Should().BeEmpty();
    }

    // ---- Base-as-reference toggle ------------------------------------------------------

    [Fact]
    public void Generate_IncludeBaseAsReference_EmitsCanonicalBaseFirst()
    {
        var engine = new CaptionMutationEngine();
        var config = Config(MutationAxis.Look, 3, seed: 5, includeBase: true);

        var result = engine.Generate(MutationTestData.BaseCaption(), config, MutationTestData.Library());

        var reference = result.Variants[0];
        reference.Caption.Should().Be(V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption()));
        reference.OperatorName.Should().BeNull();
        reference.SubSeed.Should().Be(config.Seed);
    }

    [Fact]
    public void Generate_ExcludeBase_FirstVariantIsMutated()
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 3, includeBase: false), MutationTestData.Library());

        var first = result.Variants[0];
        first.OperatorName.Should().NotBeNull();
        first.Caption.Should().NotBe(V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption()));
    }

    // ---- Canonical / validator-clean on the goldens ------------------------------------

    [Theory]
    [InlineData(MutationAxis.Look)]
    [InlineData(MutationAxis.Scene)]
    public void Generate_AllVariantsAreValidatorCleanAndCanonical(MutationAxis axis)
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(axis, 12, includeBase: true), MutationTestData.Library());

        result.Variants.Should().NotBeEmpty();
        foreach (var variant in result.Variants)
        {
            var parsed = V4JsonPromptSerializer.Deserialize(variant.Caption);
            V4JsonPromptValidator.Validate(parsed).Should().BeEmpty();
            // Round-trips byte-identically — the caption is already canonical.
            V4JsonPromptSerializer.Serialize(parsed).Should().Be(variant.Caption);
        }
    }

    // ---- Axis pinning + strict one-change ----------------------------------------------

    [Fact]
    public void Generate_LookRun_LeavesSceneGeometryUntouched()
    {
        // LOOK moves style and/or element ornament (desc); it must NEVER move scene geometry — background,
        // element set, bboxes, or per-element palettes (those are SCENE territory). desc itself can change
        // (ApplyOrnamentKit), so the invariant is the geometry, not byte-identical composition.
        var engine = new CaptionMutationEngine();
        var baseCaption = MutationTestData.BaseCaption();
        var baseString = V4JsonPromptSerializer.Serialize(baseCaption);
        var baseElements = baseCaption.CompositionalDeconstruction.Elements;

        var result = engine.Generate(baseCaption, Config(MutationAxis.Look, 10, includeBase: false), MutationTestData.Library());

        foreach (var variant in result.Variants)
        {
            var comp = V4JsonPromptSerializer.Deserialize(variant.Caption).CompositionalDeconstruction;
            comp.Background.Should().Be(baseCaption.CompositionalDeconstruction.Background);
            comp.Elements.Should().HaveCount(baseElements.Count);
            for (var i = 0; i < baseElements.Count; i++)
            {
                comp.Elements[i].Type.Should().Be(baseElements[i].Type);
                comp.Elements[i].Bbox.Should().Equal(baseElements[i].Bbox);
                comp.Elements[i].ColorPalette.Should().Equal(baseElements[i].ColorPalette);
            }

            variant.Caption.Should().NotBe(baseString); // something on the LOOK axis moved
        }
    }

    [Fact]
    public void Generate_SceneRun_LeavesStyleUntouched_AndMovesComposition()
    {
        var engine = new CaptionMutationEngine();
        var baseCaption = MutationTestData.BaseCaption();
        var baseString = V4JsonPromptSerializer.Serialize(baseCaption);

        var result = engine.Generate(baseCaption, Config(MutationAxis.Scene, 10, includeBase: false), MutationTestData.Library());

        foreach (var variant in result.Variants)
        {
            var parsed = V4JsonPromptSerializer.Deserialize(variant.Caption);
            parsed.StyleDescription.Should().BeEquivalentTo(baseCaption.StyleDescription);
            variant.Caption.Should().NotBe(baseString); // the one axis that moved is composition
        }
    }

    [Fact]
    public void Generate_OnlyRunsOperatorsOfThePinnedAxis()
    {
        var look = new RecordingOperator(MutationAxis.Look, "L");
        var scene = new RecordingOperator(MutationAxis.Scene, "S");
        var engine = new CaptionMutationEngine([look, scene]);

        engine.Generate(MutationTestData.BaseCaption(), Config(MutationAxis.Look, 4), MutationTestData.Library());

        look.Calls.Should().BeGreaterThan(0);
        scene.Calls.Should().Be(0);
    }

    [Fact]
    public void Generate_EachVariantMutatesTheOriginalBase_NotAChain()
    {
        var op = new RecordingOperator(MutationAxis.Look, "L");
        var engine = new CaptionMutationEngine([op]);
        var expectedBase = V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption());

        engine.Generate(MutationTestData.BaseCaption(), Config(MutationAxis.Look, 5), MutationTestData.Library());

        op.ReceivedBases.Should().OnlyContain(s => s == expectedBase);
    }

    // ---- Drop / retry behavior ---------------------------------------------------------

    [Fact]
    public void Generate_EmptyLibrary_LookRun_DropsAllAndLogs()
    {
        var engine = new CaptionMutationEngine();

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 3, includeBase: false), MutationLibrary.Empty);

        result.Variants.Should().BeEmpty();
        result.DropLog.Should().NotBeEmpty();
        result.DropLog.Count(l => l.Contains("exhausted")).Should().Be(3);
    }

    [Fact]
    public void Generate_BoundsAttempts_DoesNotHang()
    {
        var engine = new CaptionMutationEngine([new AlwaysNullOperator(MutationAxis.Look)]);

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 5, includeBase: false), MutationTestData.Library());

        result.Variants.Should().BeEmpty();
        result.DropLog.Count(l => l.Contains("produced no legal mutation")).Should().Be(5 * 20);
        result.DropLog.Count(l => l.Contains("exhausted")).Should().Be(5);
    }

    [Fact]
    public void Generate_NoOperatorsForAxis_LogsAndSkips()
    {
        var look = new RecordingOperator(MutationAxis.Look, "L");
        var engine = new CaptionMutationEngine([look]); // only a Look operator...

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Scene, 4), MutationTestData.Library()); // ...but pin Scene

        result.Variants.Should().BeEmpty();
        result.DropLog.Count(l => l.Contains("no operators for axis")).Should().Be(4);
        look.Calls.Should().Be(0);
    }

    // ---- Dedup decision lock -----------------------------------------------------------

    [Fact]
    public void Generate_DoesNotDedupVariants()
    {
        // A constant operator forces every slot to the same caption; the engine must keep all of them.
        var engine = new CaptionMutationEngine([new RecordingOperator(MutationAxis.Look, "L")]);

        var result = engine.Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 5, includeBase: false), MutationTestData.Library());

        result.Variants.Should().HaveCount(5);
        result.Variants.Select(v => v.Caption).Distinct().Should().ContainSingle();
    }

    // ---- Catalog / constructor ---------------------------------------------------------

    [Fact]
    public void DefaultOperators_ExposesAllEight()
    {
        CaptionMutationEngine.DefaultOperators.Should().HaveCount(8);
        CaptionMutationEngine.DefaultOperators.Count(o => o.Axis == MutationAxis.Look).Should().Be(3);
        CaptionMutationEngine.DefaultOperators.Count(o => o.Axis == MutationAxis.Scene).Should().Be(5);
        CaptionMutationEngine.DefaultOperators.Select(o => o.Name).Should().BeEquivalentTo(
            "SwapStyle", "BlendStyle", "ApplyOrnamentKit",
            "MutateBbox", "MutatePalette", "RemoveElement", "AddElement", "SwapElementDesc");
    }

    [Fact]
    public void Generate_DefaultCtor_UsesDefaultOperators()
    {
        var result = new CaptionMutationEngine().Generate(
            MutationTestData.BaseCaption(), Config(MutationAxis.Look, 4, includeBase: false), MutationTestData.Library());

        result.Variants.Should().NotBeEmpty();
        result.Variants.Should().OnlyContain(v => V4JsonPromptValidator.Validate(
            V4JsonPromptSerializer.Deserialize(v.Caption)).Count == 0);
    }

    // ---- Argument guards ---------------------------------------------------------------

    [Fact]
    public void Generate_NullArguments_Throw()
    {
        var engine = new CaptionMutationEngine();
        var config = Config(MutationAxis.Look, 1);

        engine.Invoking(e => e.Generate(null!, config, MutationTestData.Library()))
            .Should().Throw<ArgumentNullException>();
        engine.Invoking(e => e.Generate(MutationTestData.BaseCaption(), null!, MutationTestData.Library()))
            .Should().Throw<ArgumentNullException>();
        engine.Invoking(e => e.Generate(MutationTestData.BaseCaption(), config, null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCatalog_Throws()
    {
        var act = () => new CaptionMutationEngine(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- Test doubles ------------------------------------------------------------------

    /// <summary>Always succeeds with a constant mutation (sets a fixed high-level description) and records every
    /// call — used to isolate the engine loop from operator randomness and to assert no-chaining / dedup.</summary>
    private sealed class RecordingOperator(MutationAxis axis, string name) : ICaptionOperator
    {
        public List<string> ReceivedBases { get; } = [];
        public int Calls { get; private set; }

        public MutationAxis Axis => axis;
        public string Name => name;

        public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context)
        {
            Calls++;
            ReceivedBases.Add(V4JsonPromptSerializer.Serialize(source));
            var clone = CaptionClone.Clone(source);
            clone.HighLevelDescription = "A constant test mutation of the base caption.";
            return clone;
        }
    }

    /// <summary>Never produces a legal mutation — used to exercise the per-variant attempt bound.</summary>
    private sealed class AlwaysNullOperator(MutationAxis axis) : ICaptionOperator
    {
        public MutationAxis Axis => axis;
        public string Name => "AlwaysNull";

        public V4JsonPrompt? Apply(V4JsonPrompt source, Random rng, MutationContext context) => null;
    }
}
