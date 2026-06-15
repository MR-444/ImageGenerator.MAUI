using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class SwapStyleOperatorTests
{
    private readonly SwapStyleOperator _op = new();

    [Fact]
    public void Apply_ProducesValidatorCleanResult_OnDifferentSingleBranchStyle()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();
        // single branch preserved
        (string.IsNullOrEmpty(result.StyleDescription!.ArtStyle) ^ string.IsNullOrEmpty(result.StyleDescription.Photo))
            .Should().BeTrue();
    }

    [Fact]
    public void Apply_NeverReturnsCurrentStyle()
    {
        var source = MutationTestData.BaseCaption();

        for (var seed = 0; seed < 30; seed++)
        {
            var result = _op.Apply(source, new Random(seed), MutationTestData.Context(source));
            StyleMath.SameStyle(result!.StyleDescription, source.StyleDescription).Should().BeFalse();
        }
    }

    [Fact]
    public void Apply_LeavesCompositionUntouched()
    {
        var source = MutationTestData.BaseCaption();
        var before = V4JsonPromptSerializer.Serialize(source);

        var result = _op.Apply(source, new Random(3), MutationTestData.Context(source))!;

        // source not mutated
        V4JsonPromptSerializer.Serialize(source).Should().Be(before);
        // only the style block differs
        result.CompositionalDeconstruction.Should().BeEquivalentTo(source.CompositionalDeconstruction);
    }

    [Fact]
    public void Apply_IsDeterministicPerSeed_AndVariesAcrossSeeds()
    {
        var source = MutationTestData.BaseCaption();

        var a = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        var b = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        a.Should().Be(b);

        var distinct = Enumerable.Range(0, 30)
            .Select(s => V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(s), MutationTestData.Context(source))!))
            .Distinct()
            .Count();
        distinct.Should().BeGreaterThan(1); // both alternatives get picked
    }

    [Fact]
    public void Apply_EmptyLibrary_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        var ctx = new MutationContext(1000, 1000, SlotTagger.Resolve(source), MutationLibrary.Empty);

        _op.Apply(source, new Random(1), ctx).Should().BeNull();
    }

    private static MutationContext PinnedContext(V4JsonPrompt source, string pinned) =>
        new(1000, 1000, SlotTagger.Resolve(source), MutationTestData.Library(),
            MutationStrength.Moderate, pinned);

    [Fact]
    public void Apply_Pinned_SwapsToExactlyThatFragment_RegardlessOfSeed()
    {
        var source = MutationTestData.BaseCaption(); // current style = gouache
        var anime = MutationTestData.AnimeStyle();

        for (var seed = 0; seed < 10; seed++)
        {
            var result = _op.Apply(source, new Random(seed), PinnedContext(source, "anime"))!;
            result.Should().NotBeNull();
            StyleMath.SameStyle(result.StyleDescription, anime).Should().BeTrue();
        }
    }

    [Fact]
    public void Apply_PinnedToTheCurrentStyle_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption(); // current style IS "gouache"

        _op.Apply(source, new Random(1), PinnedContext(source, "gouache")).Should().BeNull();
    }

    [Fact]
    public void Apply_PinnedToAnUnknownName_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();

        _op.Apply(source, new Random(1), PinnedContext(source, "does_not_exist")).Should().BeNull();
    }
}
