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
