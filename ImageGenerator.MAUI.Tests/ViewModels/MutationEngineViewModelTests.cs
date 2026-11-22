using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.TestSupport;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// Phase-5 VM seam: the page only configures a run and hands the engine's variants to the existing
/// batch. These tests pin the Shell-free seam — base resolution, slot-tag write-through, axis→operator
/// mapping, Count clamp, and determinism — without touching Shell. Only the last two tests hand the
/// engine a generator, and they need it because that is where the render model lives.
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
                [.. sut.Run(MutationTestData.Library()).Variants.Select(v => v.Caption)]);
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

    // A page that calls a model has a host generator (whose settings own the model lists) and a store
    // (which remembers the pick). Both are required here, so no test runs the AI path with no model
    // available at all — a state the app cannot reach.
    private static MutationEngineViewModel CreateSutWithAi(
        ICaptionMutationLlmService llm, IOllamaModelCatalog? ollamaCatalog = null) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator: BuildBareGenerator(),
            clipboard: null,
            mutationLlm: llm,
            ollamaCatalog: ollamaCatalog,
            uiStateStore: Mock.Of<IUiStateStore>());

    /// <summary>The page itself, with a model choice it can be pointed at.</summary>
    private static MutationEngineViewModel CreateSutWithModel() =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator: BuildBareGenerator(),
            mutationLlm: new StubMutationLlm(),
            uiStateStore: Mock.Of<IUiStateStore>());

    /// <summary>A page that can mutate but cannot see any model settings — no host generator to read them
    /// from, which no page in the app can be.</summary>
    private static MutationEngineViewModel CreateSutWithLlmOnly(ICaptionMutationLlmService llm) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            mutationLlm: llm);

    /// <summary>Choose the way the picker does. The store is a mock, so nothing was remembered from a
    /// previous run and the model has to be handed over the way a user types it.</summary>
    private static void Pick(MutationEngineViewModel sut, ModelProvider provider, string? model = null)
    {
        sut.Model!.SelectedProvider = provider;
        sut.Model.SelectedModel = model;
    }

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

    // What running costs, in the only units this app can state honestly. The test this replaces asserted
    // "$0.17 for 10 on Sonnet" — a hard-coded per-call rate times a call count, never a token count, and
    // meaningless the moment the picker can name any model the provider exposes.
    [Theory]
    [InlineData(ModelProvider.Anthropic, "billed by Claude")]
    [InlineData(ModelProvider.OpenRouter, "billed by OpenRouter")]
    public void TheCostHint_SaysWhoGetsBilledWithoutInventingAfigure(ModelProvider provider, string expected)
    {
        var sut = CreateSutWithModel();
        Pick(sut, provider);
        sut.Count = 10;

        sut.RunCostHint.Should().Contain("10 calls").And.Contain(expected);
        sut.RunCostHint.Should().NotContain("$", "no token count is known before the call, so no figure is honest");
    }

    [Fact]
    public void TheCostHint_CallsLocalFree_AndStillCountsTheCalls()
    {
        var sut = CreateSutWithModel();
        Pick(sut, ModelProvider.Local);
        sut.Count = 1;

        sut.RunCostHint.Should().Contain("Free").And.Contain("1 call")
            .And.NotContain("calls", "one call is not calls");
    }

    [Fact]
    public async Task MutateWithAi_FansOutOneCallPerVariant_WithDistinctIndicesAndSteer()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithAi(stub);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.Steer = "make it winter";
        // Picked the way the page does: the provider, then the model to call on it. The fan-out has to
        // carry that same target on every variant, which is what the assertion below reads.
        Pick(sut, ModelProvider.Anthropic, AnthropicModels.Opus);
        sut.Count = 5;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().HaveCount(5);
        stub.MutateCalls.Select(c => c.Index).Should().BeEquivalentTo([0, 1, 2, 3, 4]);
        stub.MutateCalls.Should().OnlyContain(c => c.Steer == "make it winter"
            && c.Target.Provider == ModelProvider.Anthropic && c.Target.ModelId == AnthropicModels.Opus,
            "every fan-out call carries the same target the page was left on");
    }

    /// <summary>
    /// A page with nothing to pick from refuses, and says so, rather than calling a hard-coded model.
    /// The alternative is a silent call to a model the user never chose, billed to a key they may not
    /// even have.
    /// </summary>
    [Fact]
    public async Task WithoutAChoiceToRead_ItRefusesInsteadOfCallingAGuessedModel()
    {
        var stub = new StubMutationLlm();
        var sut = CreateSutWithLlmOnly(stub);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        sut.Count = 2;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().BeEmpty("nothing was picked, and guessing is what the picker replaced");
        sut.StatusKind.Should().Be(StatusKind.Error);
        sut.StatusMessage.Should().Contain("Model settings");
    }

    // Which provider is picked changes how the calls may be issued: a local model shares one GPU and
    // answers one at a time, while hosted calls are network-bound and worth fanning out. Neither rule is
    // visible in a result — only in how many calls were open at once — so this stub counts that.
    [Theory]
    [InlineData(ModelProvider.Local, 1, "one local model on one GPU queues anyway, and a queue the client can see is a timeout it can avoid")]
    [InlineData(ModelProvider.Anthropic, 2, "hosted calls are network-bound; four at a time is the whole point of a paid call")]
    public async Task TheProviderDecidesHowManyCallsMayBeOpenAtOnce(
        ModelProvider provider, int expectedAtMost, string because)
    {
        var slow = new SlowMutationLlm();
        var sut = CreateSutWithAi(slow);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        Pick(sut, provider, AnthropicModels.Sonnet);
        sut.Count = 4;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        slow.CallCount.Should().Be(4);
        if (expectedAtMost == 1)
            slow.MaxConcurrent.Should().Be(1, because);
        else
            slow.MaxConcurrent.Should().BeGreaterThan(1, because);
    }

    /// <summary>
    /// A run resolves its model once and keeps it. The user is free to move the picker while four calls are
    /// in flight; what must not happen is variants 1 and 2 answering from Claude and 3 and 4 from Ollama,
    /// because the grid cannot say which of them cost money.
    /// </summary>
    [Fact]
    public async Task ARunKeepsTheModelItStartedWith()
    {
        var slow = new SlowMutationLlm();
        var sut = CreateSutWithAi(slow);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        // Started on the serial provider deliberately: three hosted calls all leave within a millisecond,
        // and a run that re-decided per variant would have nothing left to re-decide. Serial calls start
        // after the picker has moved, which is the only way to tell the two implementations apart.
        Pick(sut, ModelProvider.Local, "llama-started-on");
        sut.Count = 3;

        var run = sut.MutateWithAiCommand.ExecuteAsync(null);
        await Task.Delay(10);
        Pick(sut, ModelProvider.Anthropic, "moved-midway");
        await run;

        slow.CallCount.Should().Be(3);
        slow.Targets.Should().OnlyContain(t => t.Provider == ModelProvider.Local && t.ModelId == "llama-started-on",
            "the picker may move, but the calls already asked for are not the picker's to re-decide");
    }

    /// <summary>
    /// The defect this whole change set was written to kill: several pickers saving into one value. The
    /// state object is role-scoped, and this pins that the page hands it <em>its</em> role — a page wired
    /// with another role's key still works, still saves, and quietly overwrites a different screen's pick.
    /// </summary>
    [Fact]
    public void ThePagesChoiceIsRememberedUnderItsOwnRole()
    {
        var store = new Mock<IUiStateStore>();
        var sut = new MutationEngineViewModel(
            new StubLibraryService(), new CaptionMutationEngine(), NullLogger<MutationEngineViewModel>.Instance,
            generator: BuildBareGenerator(), mutationLlm: new StubMutationLlm(), uiStateStore: store.Object);

        sut.Model!.SelectedProvider = ModelProvider.OpenRouter;
        sut.Model.SelectedModel = "deepseek/deepseek-chat";

        store.Verify(s => s.PersistLlmProvider(ModelRole.Mutate, ModelProvider.OpenRouter), Times.Once);
        store.Verify(s => s.PersistLlmModel(ModelRole.Mutate, ModelProvider.OpenRouter, "deepseek/deepseek-chat"), Times.Once);
        store.Verify(s => s.PersistLlmProvider(It.Is<ModelRole>(r => r != ModelRole.Mutate), It.IsAny<ModelProvider>()),
            Times.Never, "mutating a caption is not the idea page's decision to overwrite");
        store.Verify(s => s.PersistLlmModel(It.Is<ModelRole>(r => r != ModelRole.Mutate), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never, "nor is the model it typed");
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

        sut.SetBreedSetForTest([MutationTestData.BaseCaption(), MutationTestData.BaseCaption()]);

        sut.IsBreedMode.Should().BeTrue();
        sut.IsAiMode.Should().BeTrue("breeding is an AI-only path");
        sut.BreedSummary.Should().Contain("2 winner");
        sut.HasBase.Should().BeTrue("the first winner seeds the base so the run gate passes");
    }

    [Fact]
    public void Initialize_OrdinaryVisitAfterBreed_RestoresPrimaryAiWorkflow()
    {
        var sut = CreateSutWithAi(new StubMutationLlm());
        sut.SetBreedSetForTest([MutationTestData.BaseCaption(), MutationTestData.BaseCaption()]);
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
        sut.SetBreedSetForTest([MutationTestData.BaseCaption(), MutationTestData.BaseCaption()]);
        sut.Steer = "blend them";
        Pick(sut, ModelProvider.Anthropic, AnthropicModels.Sonnet);
        sut.Count = 4;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        stub.MutateCalls.Should().BeEmpty("breed mode never calls MutateAsync");
        stub.BreedCalls.Should().HaveCount(4);
        stub.BreedCalls.Select(c => c.Index).Should().BeEquivalentTo([0, 1, 2, 3]);
        stub.BreedCalls.Should().OnlyContain(c =>
            c.WinnerCount == 2 && c.Steer == "blend them"
            && c.Target.Provider == ModelProvider.Anthropic && c.Target.ModelId == AnthropicModels.Sonnet);
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

        sut.SetBreedSetForTest([MutationTestData.BaseCaption(), MutationTestData.BaseCaption()]);
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
        Pick(sut, ModelProvider.Local);
        sut.Count = 3;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        catalog.UnloadCalls.Should().Be(1, "the local model is freed once after the whole sequential batch");
    }

    // The unload is where the resolved endpoint actually leaves the process, so the two strings are
    // the contract worth pinning: the generator's Ollama URL — never its ComfyUI URL — and THIS
    // page's own model, not the image model the generate button uses. One shared reader owns the
    // fallbacks, and these two tests keep it honest.
    [Fact]
    public async Task MutateWithAi_LocalTier_UnloadsAtTheGeneratorsEndpointAndModel()
    {
        var catalog = new StubOllamaCatalog();
        var generator = BuildGenerator("http://comfy.example:8188", "http://ollama.example:11434");
        var sut = CreateSutWithAiAndGate(
            new FailingMutationLlm(), catalog, new FakeGpuGate(), generator);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        Pick(sut, ModelProvider.Local, "qwen2.5-vl:7b");
        sut.Count = 1;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        catalog.Unloads.Should().HaveCount(1);
        catalog.Unloads[0].Url.Should().Be("http://ollama.example:11434",
            "the model is freed at the endpoint the Local tier talked to, not at the ComfyUI one");
        catalog.Unloads[0].Model.Should().Be("qwen2.5-vl:7b",
            "and the model freed is the one this page called, not the image model sharing the server");
    }

    // A blank setting is not an endpoint: the default host has to reach the unload too, or the model
    // stays resident on the box the Local tier really used.
    [Fact]
    public async Task MutateWithAi_LocalTier_BlankOllamaUrl_UnloadsAtTheDefaultEndpoint()
    {
        var catalog = new StubOllamaCatalog();
        var generator = BuildGenerator("http://comfy.example:8188", "");
        var sut = CreateSutWithAiAndGate(
            new FailingMutationLlm(), catalog, new FakeGpuGate(), generator);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        Pick(sut, ModelProvider.Local, "qwen2.5-vl:7b");
        sut.Count = 1;

        await sut.MutateWithAiCommand.ExecuteAsync(null);

        catalog.Unloads.Should().HaveCount(1);
        catalog.Unloads[0].Url.Should().Be(ModelConstants.Ollama.DefaultBaseUrl,
            "a blank Ollama URL falls back to the configured default, same as the gate check does");
        catalog.Unloads[0].Model.Should().Be("qwen2.5-vl:7b");
    }

    [Fact]
    public async Task MutateWithAi_CloudTier_NeverUnloadsOllama()
    {
        var catalog = new StubOllamaCatalog();
        var sut = CreateSutWithAi(new StubMutationLlm(), catalog);
        sut.SetBaseForTest(MutationTestData.BaseCaption());
        sut.IsAiMode = true;
        Pick(sut, ModelProvider.Anthropic);
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
        Pick(sut, ModelProvider.Local);
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
        Pick(sut, ModelProvider.Anthropic);
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
            gpuGate: gate,
            uiStateStore: Mock.Of<IUiStateStore>());

    // A generator just so the same-host check has a ComfyUI URL to compare; all variants fail in these
    // tests so DispatchAiResultsAsync returns before it ever touches the real render batch.
    private static GeneratorViewModel BuildGenerator(string comfyUrl, string ollamaUrl)
    {
        var generator = BareGenerator.New();
        generator.Settings.ComfyUiBaseUrl = comfyUrl;
        generator.Settings.OllamaBaseUrl = ollamaUrl;
        return generator;
    }

    private sealed class FailingMutationLlm : ICaptionMutationLlmService
    {
        public Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, LlmTarget target, CancellationToken ct = default) =>
            Task.FromResult(LlmVariantResult.Fail("test: no variant"));

        public Task<LlmVariantResult> BreedAsync(
            IReadOnlyList<V4JsonPrompt> winners, string steer, int index, LlmTarget target, CancellationToken ct = default) =>
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
        /// <summary>Every unload with the endpoint/model it was given — the calls-only count below is
        /// what the older tests assert, the list is what the endpoint tests assert.</summary>
        public List<(string Url, string Model)> Unloads { get; } = [];
        public int UnloadCalls => Unloads.Count;
        public Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
        public Task<IReadOnlyList<OllamaModelInfo>> ListModelInfosAsync(string baseUrl, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OllamaModelInfo>>([]);
        public Task UnloadAsync(string baseUrl, string model, CancellationToken ct = default)
        {
            Unloads.Add((baseUrl, model));
            return Task.CompletedTask;
        }
    }

    private sealed class StubMutationLlm : ICaptionMutationLlmService
    {
        public List<(int Index, string Steer, LlmTarget Target, V4JsonPrompt Base)> MutateCalls { get; } = [];
        public List<(int WinnerCount, int Index, string Steer, LlmTarget Target)> BreedCalls { get; } = [];

        public Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, LlmTarget target, CancellationToken ct = default)
        {
            MutateCalls.Add((index, steer, target, baseCaption));
            return Task.FromResult(LlmVariantResult.Ok(MutationTestData.BaseCaption(), $"v{index}"));
        }

        public Task<LlmVariantResult> BreedAsync(
            IReadOnlyList<V4JsonPrompt> winners, string steer, int index, LlmTarget target, CancellationToken ct = default)
        {
            BreedCalls.Add((winners.Count, index, steer, target));
            return Task.FromResult(LlmVariantResult.Ok(MutationTestData.BaseCaption(), $"b{index}"));
        }
    }

    /// <summary>A model slow enough to still be answering when the test moves the picker, that reports how
    /// many of its calls were open at the same moment.</summary>
    private sealed class SlowMutationLlm : ICaptionMutationLlmService
    {
        private readonly object _gate = new();
        private readonly List<LlmTarget> _targets = [];
        private int _inFlight;
        private int _maxInFlight;

        public int MaxConcurrent { get { lock (_gate) return _maxInFlight; } }
        public int CallCount { get { lock (_gate) return _targets.Count; } }
        public IReadOnlyList<LlmTarget> Targets { get { lock (_gate) return _targets.ToArray(); } }

        public async Task<LlmVariantResult> MutateAsync(
            V4JsonPrompt baseCaption, string steer, int index, LlmTarget target, CancellationToken ct = default)
        {
            var opened = Interlocked.Increment(ref _inFlight);
            lock (_gate)
            {
                _targets.Add(target);
                _maxInFlight = Math.Max(_maxInFlight, opened);
            }

            try { await Task.Delay(30, ct); } catch (OperationCanceledException) { }
            Interlocked.Decrement(ref _inFlight);
            return LlmVariantResult.Ok(MutationTestData.BaseCaption(), $"v{index}");
        }

        public Task<LlmVariantResult> BreedAsync(
            IReadOnlyList<V4JsonPrompt> winners, string steer, int index, LlmTarget target, CancellationToken ct = default)
            => MutateAsync(winners[0], steer, index, target, ct);
    }

        private static MutationEngineViewModel CreateSut(GeneratorViewModel generator) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator);

    /// <summary>
    /// The engine renders through the generator's batch queue, so the model it will use is whatever that
    /// VM currently holds. It has no model picker of its own — only <c>SettingsEcho</c> naming it — so a
    /// model this build refuses to call has to be caught here, or the run produces a grid of refused jobs
    /// and one error log line each. Bare mocks plus the production registry: the rule under test is the
    /// registry's answer about the selected model, and a mocked registry would test the mock.
    /// </summary>
    [Fact]
    public void GenerateCommand_RefusesAModelThisBuildCannotDrive()
    {
        var generator = BuildBareGenerator();
        var sut = CreateSut(generator);
        sut.SetBaseForTest(MutationTestData.BaseCaption());

        generator.Parameters.Model = "stability-ai/some-new-model";

        sut.GenerateCommand.CanExecute(null).Should().BeFalse();

        // And back again: the gate belongs to the model, not to the loaded base or to this page's session.
        generator.Parameters.Model = ModelConstants.OpenAI.GptImage26SunburstOnReplicate;

        sut.GenerateCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void GenerateCommand_StandAloneEngineHasNoModelToRefuse()
    {
        // Every other test here builds the VM without a generator. A null generator means the engine has
        // no opinion about the model, not an unsupported one — otherwise the run gate would be false for
        // the whole file and each of those tests would pass on a button that can never execute.
        var sut = WithBase();

        sut.GenerateCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>
    /// The paid half of the pair. <c>MutateWithAi</c> pays for the captions first and renders afterwards,
    /// so a model this build refuses to call buys N Sonnet calls (Sonnet is the default tier) and shows an
    /// empty grid. Reaching it takes no odd setup: "Mutate from this" on an Ideogram image loads a base
    /// while keeping whatever model is currently selected, and the engine opens for any model.
    /// </summary>
    [Fact]
    public void MutateWithAiCommand_DoesNotPayForCaptionsThatCannotRender()
    {
        var generator = BuildBareGenerator();
        var sut = CreateSutWithLlm(generator);
        sut.SetBaseForTest(MutationTestData.BaseCaption());

        // Asserted enabled first: the gate must come from the model, not from a condition that is simply
        // never true here, which would make the assertion below pass for the wrong reason.
        sut.MutateWithAiCommand.CanExecute(null).Should().BeTrue();

        generator.Parameters.Model = "stability-ai/some-new-model";

        sut.MutateWithAiCommand.CanExecute(null).Should().BeFalse();
    }

    private static MutationEngineViewModel CreateSutWithLlm(GeneratorViewModel generator) =>
        new(new StubLibraryService(),
            new CaptionMutationEngine(),
            NullLogger<MutationEngineViewModel>.Instance,
            generator,
            mutationLlm: new StubMutationLlm());

    private static GeneratorViewModel BuildBareGenerator() =>
        new(new Mock<IJobRunner>().Object,
            new Mock<IApiTokenStore>().Object,
            new Mock<IPollinationsTokenStore>().Object,
            new Mock<IComfyUiAuthStore>().Object,
            new Mock<ICivitaiTokenStore>().Object,
            new Mock<IAnthropicTokenStore>().Object,
            new Mock<ICivitaiPostingService>().Object,
            new Mock<IUiStateStore>().Object,
            new Mock<IModelCatalogCoordinator>().Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            new Mock<IFolderPicker>().Object,
            NullLogger<GeneratorViewModel>.Instance);

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
