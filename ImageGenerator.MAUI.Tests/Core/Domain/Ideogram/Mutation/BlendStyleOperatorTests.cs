using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class BlendStyleOperatorTests
{
    private readonly BlendStyleOperator _op = new();

    [Fact]
    public void Apply_ProducesValidatorClean_SingleBranch_Result()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();
        // gouache base is the art_style branch; the blend must stay single-branch
        result.StyleDescription!.ArtStyle.Should().NotBeNullOrEmpty();
        result.StyleDescription.Photo.Should().BeNull();
    }

    [Fact]
    public void Apply_UnionsTokens_FromBothParents_CurrentFirst()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        // current (gouache) aesthetics token survives, and an alternative's token is merged in
        result.StyleDescription!.Aesthetics.Should().Contain("deadpan-whimsical");
        result.StyleDescription.Aesthetics.Should().Contain("occult-botanical reverie");
        // current branch text leads, fragment text appended
        result.StyleDescription.ArtStyle.Should().StartWith("gouache with crisp ink linework");
    }

    [Fact]
    public void Apply_Palette_IsAccentsFirst_AndWithinCap()
    {
        var source = MutationTestData.BaseCaption();

        var palette = _op.Apply(source, new Random(2), MutationTestData.Context(source))!.StyleDescription!.ColorPalette!;

        palette.Count.Should().BeLessThanOrEqualTo(StyleDescription.MaxPaletteColors);
        var saturations = palette.Select(ColorMath.Saturation).ToList();
        saturations.Should().BeInDescendingOrder();
    }

    [Fact]
    public void Apply_DiffersFromBase()
    {
        var source = MutationTestData.BaseCaption();

        var result = _op.Apply(source, new Random(4), MutationTestData.Context(source))!;

        StyleMath.SameStyle(result.StyleDescription, source.StyleDescription).Should().BeFalse();
    }

    [Fact]
    public void Apply_NoStyleBlock_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        source.StyleDescription = null;

        _op.Apply(source, new Random(1), MutationTestData.Context(source)).Should().BeNull();
    }

    [Fact]
    public void Apply_IsDeterministic_AndPure()
    {
        var source = MutationTestData.BaseCaption();
        var before = V4JsonPromptSerializer.Serialize(source);

        var a = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(9), MutationTestData.Context(source))!);
        var b = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(9), MutationTestData.Context(source))!);

        a.Should().Be(b);
        V4JsonPromptSerializer.Serialize(source).Should().Be(before); // source untouched
    }
}
