using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class ApplyOrnamentKitOperatorTests
{
    private readonly ApplyOrnamentKitOperator _op = new();

    [Fact]
    public void Apply_InjectsPhrases_IntoTaggedElements_WithinBudget()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();

        // garment (element 0): 51-word base + 10-word StyleMarker would overflow, so the 5-word
        // SecondaryFrameDevice is what fits — exercising the budget's greedy skip.
        result.CompositionalDeconstruction.Elements[0].Desc.Should().Contain("embroidered");
        // charms (element 2): both phrases fit
        result.CompositionalDeconstruction.Elements[2].Desc.Should().Contain("lockets").And.Contain("medallions");

        foreach (var element in result.CompositionalDeconstruction.Elements)
            DescBudget.CountWords(element.Desc).Should().BeLessThanOrEqualTo(DescBudget.MaxWords);
    }

    [Fact]
    public void Apply_PreservesProtectedBaseSpans()
    {
        var source = MutationTestData.BaseCaption();

        var garment = _op.Apply(source, new Random(1), MutationTestData.Context(source))!
            .CompositionalDeconstruction.Elements[0].Desc!;

        garment.Should().Contain("botanist").And.Contain("tunic"); // identity/garment base survives
    }

    [Fact]
    public void Apply_LeavesStyleAndElementCountUntouched()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.StyleDescription.Should().BeEquivalentTo(source.StyleDescription);
        result.CompositionalDeconstruction.Elements.Should().HaveSameCount(source.CompositionalDeconstruction.Elements);
    }

    [Fact]
    public void Apply_IsDeterministic_AndPure()
    {
        var source = MutationTestData.BaseCaption();
        var before = V4JsonPromptSerializer.Serialize(source);

        var a = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(2), MutationTestData.Context(source))!);
        var b = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(2), MutationTestData.Context(source))!);

        a.Should().Be(b);
        V4JsonPromptSerializer.Serialize(source).Should().Be(before);
    }

    [Fact]
    public void Apply_NoKitsInLibrary_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        var libraryWithoutKits = new MutationLibrary(MutationTestData.Library().StyleFragments, []);
        var ctx = new MutationContext(1000, 1000, SlotTagger.Resolve(source), libraryWithoutKits);

        _op.Apply(source, new Random(1), ctx).Should().BeNull();
    }
}
