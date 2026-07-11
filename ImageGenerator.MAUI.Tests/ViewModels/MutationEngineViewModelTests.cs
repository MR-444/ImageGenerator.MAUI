using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// Phase-5 VM seam: the page only configures a run and hands the engine's variants to the existing
/// batch. These tests pin the Shell-free seam — base resolution, slot-tag write-through, axis→operator
/// mapping, Count clamp, and determinism — without constructing a generator or touching Shell.
/// </summary>
public class MutationEngineViewModelTests
{
    private static MutationEngineViewModel CreateSut() =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance);

    private static MutationEngineViewModel WithBase(V4JsonPrompt? @base = null)
    {
        var sut = CreateSut();
        sut.SetBaseForTest(@base ?? MutationTestData.BaseCaption());
        return sut;
    }

    [Fact]
    public void DefaultsToFourAiVariants()
    {
        var sut = CreateSut();

        sut.IsAiMode.Should().BeTrue();
        sut.Count.Should().Be(4);
    }

    [Fact]
    public void InitializeFrom_PrefersTypedBaseOverPromptString()
    {
        // The typed hand-off carries slot tags a string would strip, so it must win.
        var typed = MutationTestData.BaseCaption();
        var sut = CreateSut();

        sut.InitializeFrom(typed, "{\"high_level_description\":\"something else entirely\"}", null, "2048x2048");

        sut.HasBase.Should().BeTrue();
        sut.SlotReview.Should().HaveCount(5); // the botanist golden's five elements
        sut.TargetWidth.Should().Be(2048); // no AR ratio ⇒ resolution fallback
        sut.TargetHeight.Should().Be(2048);
    }

    [Fact]
    public void InitializeFrom_DerivesTargetFrameFromAspectRatio_NotResolution()
    {
        var sut = CreateSut();
        var json = V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption());

        // Real render AR is portrait 2:3; the resolution is a ComfyUI MP preset (no W×H).
        sut.InitializeFrom(null, json, "2:3 (Portrait Photo)", "2.0 MP");

        // Longer side normalized to 1024; the ratio (height/width = 1.5) is what bbox ops read.
        sut.TargetWidth.Should().Be(683);
        sut.TargetHeight.Should().Be(1024);
    }

    [Fact]
    public void InitializeFrom_RatiolessAspectRatio_FallsBackToResolutionThenSquare()
    {
        var sut = CreateSut();
        var json = V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption());

        sut.InitializeFrom(null, json, "custom", "2.0 MP"); // neither carries a parseable shape

        sut.TargetWidth.Should().Be(1024);
        sut.TargetHeight.Should().Be(1024);
    }

    [Fact]
    public void InitializeFrom_FallsBackToPromptStringWhenNoHandoff()
    {
        var sut = CreateSut();
        var json = V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption());

        sut.InitializeFrom(null, json, null, null);

        sut.HasBase.Should().BeTrue();
        sut.SlotReview.Should().HaveCount(5);
        sut.TargetWidth.Should().Be(1024); // no AR, unparseable resolution ⇒ square fallback
        sut.TargetHeight.Should().Be(1024);
    }

    [Fact]
    public void InitializeFrom_UnparseablePrompt_DisablesGenerate()
    {
        var sut = CreateSut();

        sut.InitializeFrom(null, "not json at all", null, null);

        sut.HasBase.Should().BeFalse();
        sut.SlotReview.Should().BeEmpty();
        sut.StatusKind.Should().Be(StatusKind.Warning);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(50, 50)]
    [InlineData(250, 100)]
    public void Count_IsClampedToOneThroughHundred(int input, int expected)
    {
        var sut = CreateSut();
        sut.Count = input;
        sut.Count.Should().Be(expected);
    }

    [Fact]
    public void OperatorsForAxis_TracksTheSelectedAxis()
    {
        var sut = CreateSut();

        sut.SelectedAxis = MutationAxis.Look;
        sut.IsScene.Should().BeFalse();
        sut.OperatorsForAxis.Should().HaveCount(3); // SwapStyle, BlendStyle, ApplyOrnamentKit

        sut.SelectedAxis = MutationAxis.Scene;
        sut.IsScene.Should().BeTrue();
        sut.OperatorsForAxis.Should().HaveCount(5); // MutateBbox, MutatePalette, Remove/Add, SwapDesc
    }

    [Fact]
    public void Run_LookAxis_EmitsBaseReferenceThenMutatedVariants()
    {
        var sut = WithBase();
        sut.SelectedAxis = MutationAxis.Look;
        sut.Count = 6;
        sut.IncludeBase = true;
        sut.Seed = 12345;

        var result = sut.Run(MutationTestData.Library());

        // Variant 0 is the unmutated base (slot tags don't serialize, so it stays canonical).
        var canonicalBase = V4JsonPromptSerializer.Serialize(MutationTestData.BaseCaption());
        result.Variants.Should().NotBeEmpty();
        result.Variants[0].Caption.Should().Be(canonicalBase);
        result.Variants[0].OperatorName.Should().BeNull();

        // Every mutated variant differs from the base by exactly one operator application.
        result.Variants.Skip(1).Should().OnlyContain(v => v.Caption != canonicalBase);
        result.Variants.Skip(1).Should().OnlyContain(v => v.OperatorName != null);
    }

    [Fact]
    public void Run_IsDeterministic_SameSeedSameBaseSameVariants()
    {
        MutationRunVariantCaptions Run(int seed)
        {
            var sut = WithBase();
            sut.SelectedAxis = MutationAxis.Look;
            sut.Count = 8;
            sut.Seed = seed;
            return new MutationRunVariantCaptions(
                sut.Run(MutationTestData.Library()).Variants.Select(v => v.Caption).ToList());
        }

        Run(777).Captions.Should().Equal(Run(777).Captions);
        Run(777).Captions.Should().NotEqual(Run(778).Captions);
    }

    [Fact]
    public void Run_WritesReviewedSlotTagsOntoTheBaseElements()
    {
        var sut = WithBase();
        sut.SelectedAxis = MutationAxis.Look;
        sut.Count = 1;

        // The picker holds FRIENDLY labels; Run maps them back to the raw slot vocabulary.
        var explicitRow = sut.SlotReview[0];
        var autoRow = sut.SlotReview[1];
        explicitRow.SelectedTag = SlotTagDisplay.ToFriendly(SlotTag.Subject.Identity);
        autoRow.SelectedTag = SlotTagDisplay.Auto;

        sut.Run(MutationTestData.Library());

        explicitRow.Element.SlotTag.Should().Be(SlotTag.Subject.Identity);
        autoRow.Element.SlotTag.Should().BeNull(); // Auto ⇒ engine infers
    }

    // ---- AI (LLM) mode -------------------------------------------------------------------

    private static MutationEngineViewModel CreateSutWithAi(
        ICaptionMutationLlmService llm, IOllamaModelCatalog? ollamaCatalog = null) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator: null,
            clipboard: null,
            mutationLlm: llm,
            ollamaCatalog: ollamaCatalog);

    [Fact]
    public void IsAiMode_TogglesDeterministicOnlyVisibility()
    {
        var sut = CreateSut();
        sut.IsAiMode = false;
        sut.SelectedAxis = MutationAxis.Scene;

        sut.IsDeterministicMode.Should().BeTrue();
        sut.ShowStrength.Should().BeTrue(); // deterministic + SCENE

        sut.IsAiMode = true;

        sut.IsDeterministicMode.Should().BeFalse();
        sut.ShowStrength.Should().BeFalse("AI mode hides the deterministic placement-strength control");
    }

    [Fact]
    public void CanMutateWithAi_RequiresBothABaseAndTheService()
    {
        // No service ⇒ disabled even with a base.
        WithBase().MutateWithAiCommand.CanExecute(null).Should().BeFalse();

        var sut = CreateSutWithAi(new StubMutationLlm());
        sut.MutateWithAiCommand.CanExecute(null).Should().BeFalse("no base yet");

        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.MutateWithAiCommand.CanExecute(null).Should().BeTrue();
    }

    [Theory]
    [InlineData(ModelTier.Sonnet, "0.17", "Sonnet")]
    [InlineData(ModelTier.Opus, "0.28", "Opus")]
    public void CostEstimate_ReflectsCountAndTier(ModelTier tier, string expectedAmount, string expectedTier)
    {
        var sut = CreateSut();
        sut.Count = 10;
        sut.SelectedModelTier = tier;

        sut.CostEstimate.Should().Contain(expectedAmount).And.Contain(expectedTier);
    }

    [Fact]
    public void CostEstimate_LocalTierIsFree()
    {
        var sut = CreateSut();
        sut.SelectedModelTier = ModelTier.Local;

        sut.CostEstimate.Should().Contain("Free");
    }

    [Fact]
    public async Task MutateWithAi_FansOutOneCallPerVariant_WithDistinctIndicesAndSteer()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithAi(stub);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.Steer = "make it winter";
        sut.SelectedModelTier = ModelTier.Opus;
        sut.Count = 5;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().HaveCount(5);
        stub.MutateCalls.Select(c => c.Index).Should().BeEquivalentTo(new[] { 0, 1, 2, 3, 4 });
        stub.MutateCalls.Should().OnlyContain(c => c.Steer == "make it winter" && c.Tier == ModelTier.Opus);
        sut.StatusKind.Should().Be(StatusKind.Success); // no generator ⇒ "variants ready"
    }

    [Fact]
    public async Task MutateWithAi_PinnedStyle_RestylesTheBaseAndLocksTheStyleInTheSteer()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithAi(stub);
        await sut.LoadLibraryAsync();                 // fills the name→fragment cache from the stub library
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.SelectedStyleName = "anime";             // a saved style differing from the gouache base
        sut.Steer = "make it winter";
        sut.Count = 2;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        var anime = MutationTestData.AnimeStyle();
        stub.MutateCalls.Should().HaveCount(2);
        stub.MutateCalls.Should().OnlyContain(c => StyleMath.SameStyle(c.Base.StyleDescription, anime),
            "the chosen style is pre-applied to the base sent to the model");
        stub.MutateCalls.Should().OnlyContain(c => c.Steer.Contains("EXACTLY") && c.Steer.Contains("make it winter"),
            "the steer locks the style and still carries the user's direction");
    }

    [Fact]
    public async Task MutateWithAi_RandomStyle_LeavesTheBaseAndSteerUntouched()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithAi(stub);
        await sut.LoadLibraryAsync();
        var baseCaption = MutationTestData.BaseCaption();
        sut.SetBaseForTest(baseCaption);
        sut.IsAiMode = true;
        sut.Steer = "make it winter";                // SelectedStyleName stays at the random sentinel
        sut.Count = 1;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().ContainSingle();
        stub.MutateCalls[0].Steer.Should().Be("make it winter");
        StyleMath.SameStyle(stub.MutateCalls[0].Base.StyleDescription, baseCaption.StyleDescription)
            .Should().BeTrue("no pin ⇒ the base style is unchanged");
    }

    [Fact]
    public void SetBreedSet_FlipsToAiMode_AndSetsSummary()
    {
        var sut = CreateSutWithAi(new StubMutationLlm());

        sut.SetBreedSetForTest(new[] { MutationTestData.BaseCaption(), MutationTestData.BaseCaption() });

        sut.IsBreedMode.Should().BeTrue();
        sut.IsAiMode.Should().BeTrue("breeding is an AI-only path");
        sut.BreedSummary.Should().Contain("2 winner");
        sut.HasBase.Should().BeTrue("the first winner seeds the base so the run gate passes");
    }

    [Fact]
    public void Initialize_OrdinaryVisitAfterBreed_RestoresPrimaryAiWorkflow()
    {
        var sut = CreateSutWithAi(new StubMutationLlm());
        sut.SetBreedSetForTest(new[] { MutationTestData.BaseCaption(), MutationTestData.BaseCaption() });
        sut.IsAiMode.Should().BeTrue("breeding forced the paid LLM path on");

        // An ordinary re-entry restores the user's ordinary-session preference. AI is the product
        // default; breed's forced state still does not overwrite a later deliberate deterministic pick.
        sut.Initialize();

        sut.IsBreedMode.Should().BeFalse();
        sut.IsAiMode.Should().BeTrue("AI rewrite is the primary workflow for a fresh ordinary visit");
    }

    [Fact]
    public void Initialize_OrdinaryVisit_KeepsUsersDeliberateAiChoice()
    {
        var sut = CreateSutWithAi(new StubMutationLlm());
        sut.IsAiMode = true;            // user deliberately turns AI on (no breed hand-off)

        sut.Initialize();              // re-open the page the ordinary way

        sut.IsBreedMode.Should().BeFalse();
        sut.IsAiMode.Should().BeTrue("an ordinary visit keeps the user's deliberate AI-mode choice");
    }

    [Fact]
    public async Task BreedMode_FansOutBreedAsync_NotMutate()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithAi(stub);
        sut.SetBreedSetForTest(new[] { MutationTestData.BaseCaption(), MutationTestData.BaseCaption() });
        sut.Steer = "blend them";
        sut.SelectedModelTier = ModelTier.Sonnet;
        sut.Count = 4;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().BeEmpty("breed mode never calls MutateAsync");
        stub.BreedCalls.Should().HaveCount(4);
        stub.BreedCalls.Select(c => c.Index).Should().BeEquivalentTo(new[] { 0, 1, 2, 3 });
        stub.BreedCalls.Should().OnlyContain(c =>
            c.WinnerCount == 2 && c.Steer == "blend them" && c.Tier == ModelTier.Sonnet);
    }

    [Fact]
    public async Task LoadLibraryAsync_FillsTheAnchorPresetPickerFromTheLibrary()
    {
        var sut = CreateSut();

        await sut.LoadLibraryAsync();

        sut.AnchorPresets[0].Should().BeSameAs(MutationEngineViewModel.NoPreset, "the neutral entry leads the list");
        sut.SelectedAnchorPreset.Should().BeSameAs(MutationEngineViewModel.NoPreset, "it is the default selection");
        sut.AnchorPresets.Select(p => p.Name).Should().Contain("make it winter");

        // Idempotent for presets: a second appearance doesn't duplicate the list.
        var count = sut.AnchorPresets.Count;
        await sut.LoadLibraryAsync();
        sut.AnchorPresets.Should().HaveCount(count);
    }

    [Fact]
    public async Task SelectingTheNoPresetSentinel_LeavesTheSteerUntouched()
    {
        var sut = CreateSut();
        await sut.LoadLibraryAsync();
        sut.SelectedAnchorPreset = sut.AnchorPresets.First(p => p.Name == "make it winter");
        sut.Steer = "edited after the preset";

        sut.SelectedAnchorPreset = MutationEngineViewModel.NoPreset;

        sut.Steer.Should().Be("edited after the preset", "re-selecting the neutral entry is non-destructive");
    }

    [Fact]
    public async Task LoadLibraryAsync_FillsStyleNames_WithRandomSentinelFirst()
    {
        var sut = CreateSut();

        await sut.LoadLibraryAsync();

        sut.StyleNames[0].Should().Be(MutationEngineViewModel.RandomStyleSentinel);
        sut.StyleNames.Should().Contain(["gouache", "anime", "density"]);
        sut.SelectedStyleName.Should().Be(MutationEngineViewModel.RandomStyleSentinel);
    }

    [Fact]
    public async Task LoadLibraryAsync_RefreshesStyleNames_PreservingTheSelectionWhenStillPresent()
    {
        var sut = CreateSut();
        await sut.LoadLibraryAsync();
        sut.SelectedStyleName = "anime";

        await sut.LoadLibraryAsync(); // a return visit re-reads the store

        sut.StyleNames.Count(n => n == "anime").Should().Be(1, "the list is rebuilt, not appended");
        sut.SelectedStyleName.Should().Be("anime", "a still-present pick survives the refresh");
    }

    [Fact]
    public void ShowStylePin_InDeterministicLookAndAi_ButNotSceneOrBreed()
    {
        var sut = CreateSutWithAi(new StubMutationLlm());
        sut.IsAiMode = false;
        sut.SelectedAxis = MutationAxis.Look;
        sut.ShowStylePin.Should().BeTrue("deterministic LOOK swaps to a saved style");

        sut.SelectedAxis = MutationAxis.Scene;
        sut.ShowStylePin.Should().BeFalse("SCENE has no style swap");

        sut.IsAiMode = true;
        sut.ShowStylePin.Should().BeTrue("AI can restyle into a saved style");

        sut.SetBreedSetForTest(new[] { MutationTestData.BaseCaption(), MutationTestData.BaseCaption() });
        sut.ShowStylePin.Should().BeFalse("breed blends the winners' own looks");
    }

    [Fact]
    public async Task SelectingAnAnchorPreset_ReplacesTheSteer()
    {
        var sut = CreateSut();
        await sut.LoadLibraryAsync();
        sut.Steer = "something the user typed first";

        var preset = sut.AnchorPresets.First(p => p.Name == "make it winter");
        sut.SelectedAnchorPreset = preset;

        sut.Steer.Should().Be(preset.Steer);
    }

    [Fact]
    public void ClearingTheAnchorPreset_LeavesTheSteerUntouched()
    {
        var sut = CreateSut();
        sut.Steer = "my own steer";

        sut.SelectedAnchorPreset = null;

        sut.Steer.Should().Be("my own steer");
    }

    [Fact]
    public void BuildAiBatch_KeepsSuccessesAndPairsLabels()
    {
        var good = MutationTestData.BaseCaption();
        var results = new[]
        {
            LlmVariantResult.Ok(good, "winter"),
            LlmVariantResult.Fail("nope"),
            LlmVariantResult.Ok(good, "summer"),
        };

        var (prompts, labels) = MutationEngineViewModel.BuildAiBatch(results);

        prompts.Should().HaveCount(2, "failed variants are dropped");
        labels.Should().Equal("winter", "summer");
        prompts[0].Should().Be(V4JsonPromptSerializer.Serialize(good));
    }

    [Fact]
    public void PrependBaseReference_PutsTheOriginalFirst_LabeledAsReference()
    {
        var original = MutationTestData.BaseCaption();
        var prompts = new List<string> { "variant-a", "variant-b" };
        var labels = new List<string> { "winter", "summer" };

        var (outPrompts, outLabels) = MutationEngineViewModel.PrependBaseReference(original, prompts, labels);

        outPrompts[0].Should().Be(V4JsonPromptSerializer.Serialize(original), "the original is variant 0");
        outLabels[0].Should().Be("Original (reference)");
        outPrompts.Skip(1).Should().Equal("variant-a", "variant-b");
        outLabels.Skip(1).Should().Equal("winter", "summer");
    }

    [Fact]
    public async Task MutateWithAi_LocalTier_UnloadsTheOllamaModelOnce()
    {
        var catalog = new StubOllamaCatalog();
        var sut = CreateSutWithAi(new StubMutationLlm(), catalog);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.SelectedModelTier = ModelTier.Local;
        sut.Count = 3;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        catalog.UnloadCalls.Should().Be(1, "the local model is freed once after the whole sequential batch");
    }

    [Fact]
    public async Task MutateWithAi_CloudTier_NeverUnloadsOllama()
    {
        var catalog = new StubOllamaCatalog();
        var sut = CreateSutWithAi(new StubMutationLlm(), catalog);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.SelectedModelTier = ModelTier.Sonnet;
        sut.Count = 2;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        catalog.UnloadCalls.Should().Be(0, "cloud tiers hold no local VRAM");
    }

    // ---- GPU gate: Local mutation shares fireEngine's VRAM with ComfyUI, so it takes the gate ----

    [Fact]
    public async Task MutateWithAi_LocalTier_SameHost_AcquiresAndReleasesGpuGate()
    {
        var gate = new FakeGpuGate();
        var sut = CreateSutWithAiAndGate(
            new FailingMutationLlm(), new StubOllamaCatalog(), gate,
            BuildGenerator("http://fireEngine:8188", "http://fireengine:11434"));
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.SelectedModelTier = ModelTier.Local;
        sut.Count = 2;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        gate.Acquired.Should().Be(1, "the Local tier holds the shared GPU gate across its LLM calls");
        gate.Released.Should().Be(1, "and releases it before handing the variants off to render");
    }

    [Fact]
    public async Task MutateWithAi_CloudTier_NeverAcquiresGpuGate()
    {
        var gate = new FakeGpuGate();
        var sut = CreateSutWithAiAndGate(
            new FailingMutationLlm(), new StubOllamaCatalog(), gate,
            BuildGenerator("http://fireengine:8188", "http://fireengine:11434"));
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.SelectedModelTier = ModelTier.Sonnet;
        sut.Count = 2;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        gate.Acquired.Should().Be(0, "cloud tiers hold no local VRAM");
    }

    private static MutationEngineViewModel CreateSutWithAiAndGate(
        ICaptionMutationLlmService llm, IOllamaModelCatalog ollamaCatalog, IGpuGate gate, GeneratorViewModel generator) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator: generator,
            clipboard: null,
            mutationLlm: llm,
            ollamaCatalog: ollamaCatalog,
            gpuGate: gate);

    // A generator just so the same-host check has a ComfyUI URL to compare; all variants fail in these
    // tests so DispatchAiResultsAsync returns before it ever touches the real render batch.
    private static GeneratorViewModel BuildGenerator(string comfyUrl, string ollamaUrl)
    {
        var gen = new GeneratorViewModel(
            Mock.Of<IJobRunner>(),
            Mock.Of<IApiTokenStore>(),
            Mock.Of<IPollinationsTokenStore>(),
            Mock.Of<IComfyUiAuthStore>(),
            Mock.Of<ICivitaiTokenStore>(),
            Mock.Of<IAnthropicTokenStore>(),
            Mock.Of<ICivitaiPostingService>(),
            Mock.Of<IUiStateStore>(),
            Mock.Of<IModelCatalogCoordinator>(),
            ModelDescriptorRegistry.Default(),
            Mock.Of<IPromptBatchParser>(),
            Mock.Of<IComfyUiCheckpointService>(),
            Mock.Of<IGalleryService>(),
            Mock.Of<IFolderPicker>(),
            NullLogger<GeneratorViewModel>.Instance);
        gen.ComfyUiBaseUrl = comfyUrl;
        gen.OllamaBaseUrl = ollamaUrl;
        return gen;
    }

    private sealed class FailingMutationLlm : ICaptionMutationLlmService
    {
        public Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, ModelTier tier, CancellationToken ct = default) =>
            Task.FromResult(LlmVariantResult.Fail("test: no variant"));

        public Task<LlmVariantResult> BreedAsync(
            IReadOnlyList<V4JsonPrompt> winners, string steer, int index, ModelTier tier, CancellationToken ct = default) =>
            Task.FromResult(LlmVariantResult.Fail("test: no variant"));
    }

    private sealed class FakeGpuGate : IGpuGate
    {
        public int Acquired { get; private set; }
        public int Released { get; private set; }

        public Task<IDisposable> AcquireAsync(CancellationToken ct = default)
        {
            Acquired++;
            return Task.FromResult<IDisposable>(new Lease(() => Released++));
        }

        private sealed class Lease(Action onRelease) : IDisposable
        {
            private Action? _onRelease = onRelease;
            public void Dispose() => Interlocked.Exchange(ref _onRelease, null)?.Invoke();
        }
    }

    private sealed class StubOllamaCatalog : IOllamaModelCatalog
    {
        public int UnloadCalls { get; private set; }
        public Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<OllamaModelInfo>> ListModelInfosAsync(string baseUrl, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OllamaModelInfo>>([]);
        public Task UnloadAsync(string baseUrl, string model, CancellationToken ct = default)
        {
            UnloadCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubMutationLlm : ICaptionMutationLlmService
    {
        public List<(int Index, string Steer, ModelTier Tier, V4JsonPrompt Base)> MutateCalls { get; } = [];
        public List<(int WinnerCount, int Index, string Steer, ModelTier Tier)> BreedCalls { get; } = [];

        public Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, ModelTier tier, CancellationToken ct = default)
        {
            MutateCalls.Add((index, steer, tier, baseCaption));
            return Task.FromResult(LlmVariantResult.Ok(MutationTestData.BaseCaption(), $"v{index}"));
        }

        public Task<LlmVariantResult> BreedAsync(
            IReadOnlyList<V4JsonPrompt> winners, string steer, int index, ModelTier tier, CancellationToken ct = default)
        {
            BreedCalls.Add((winners.Count, index, steer, tier));
            return Task.FromResult(LlmVariantResult.Ok(MutationTestData.BaseCaption(), $"b{index}"));
        }
    }

    private sealed record MutationRunVariantCaptions(IReadOnlyList<string> Captions);

    // LoadAsync is only exercised by GenerateAsync (which needs a generator + Shell); the seam tests
    // call Run() directly with a library, so this stub just satisfies the constructor.
    private sealed class StubLibraryService : IMutationLibraryService
    {
        public string LibraryDirectory => "(test)";
        public Task<MutationLibrary> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(MutationTestData.Library());
        public Task SaveAsync(MutationLibrary library, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
