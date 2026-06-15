using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;

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

    private static MutationEngineViewModel CreateSutWithAi(ICaptionMutationLlmService llm) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator: null,
            clipboard: null,
            mutationLlm: llm);

    [Fact]
    public void IsAiMode_TogglesDeterministicOnlyVisibility()
    {
        var sut = CreateSut();
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

        sut.AnchorPresets.Select(p => p.Name).Should().Contain("make it winter");

        // Idempotent for presets: a second appearance doesn't duplicate the list.
        var count = sut.AnchorPresets.Count;
        await sut.LoadLibraryAsync();
        sut.AnchorPresets.Should().HaveCount(count);
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
    public void ShowStylePin_OnlyForDeterministicLook()
    {
        var sut = CreateSut();
        sut.SelectedAxis = MutationAxis.Look;
        sut.ShowStylePin.Should().BeTrue("deterministic LOOK");

        sut.SelectedAxis = MutationAxis.Scene;
        sut.ShowStylePin.Should().BeFalse("SCENE has no style swap");

        sut.SelectedAxis = MutationAxis.Look;
        sut.IsAiMode = true;
        sut.ShowStylePin.Should().BeFalse("AI mode hides the deterministic style pin");
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

    private sealed class StubMutationLlm : ICaptionMutationLlmService
    {
        public List<(int Index, string Steer, ModelTier Tier)> MutateCalls { get; } = [];
        public List<(int WinnerCount, int Index, string Steer, ModelTier Tier)> BreedCalls { get; } = [];

        public Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, ModelTier tier, CancellationToken ct = default)
        {
            MutateCalls.Add((index, steer, tier));
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
