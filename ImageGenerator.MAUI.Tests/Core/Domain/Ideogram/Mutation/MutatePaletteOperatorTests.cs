using System.Text.RegularExpressions;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class MutatePaletteOperatorTests
{
    private static readonly Regex HexRegex = new("^#[0-9A-F]{6}$");
    private readonly MutatePaletteOperator _op = new();

    [Fact]
    public void Apply_ProducesValidatorCleanResult_WithUppercaseSwatches()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();

        foreach (var element in result.CompositionalDeconstruction.Elements)
        foreach (var swatch in element.ColorPalette ?? [])
            HexRegex.IsMatch(swatch).Should().BeTrue($"'{swatch}' must be uppercase #RRGGBB");
    }

    [Fact]
    public void Apply_TouchesExactlyOneElementPalette_AndLeavesStylePaletteAlone()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(2), MutationTestData.Context(source))!;

        var sourceElements = source.CompositionalDeconstruction.Elements;
        var resultElements = result.CompositionalDeconstruction.Elements;

        var changed = Enumerable.Range(0, sourceElements.Count)
            .Count(i => !PaletteEquals(sourceElements[i].ColorPalette, resultElements[i].ColorPalette));
        changed.Should().Be(1);

        result.StyleDescription!.ColorPalette.Should().Equal(source.StyleDescription!.ColorPalette);
    }

    [Fact]
    public void Apply_KeepsPaletteLength_WithinCap()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(4), MutationTestData.Context(source))!;

        var sourceElements = source.CompositionalDeconstruction.Elements;
        var resultElements = result.CompositionalDeconstruction.Elements;
        for (var i = 0; i < sourceElements.Count; i++)
        {
            (resultElements[i].ColorPalette?.Count ?? 0).Should().Be(sourceElements[i].ColorPalette?.Count ?? 0);
            (resultElements[i].ColorPalette?.Count ?? 0).Should().BeLessThanOrEqualTo(Element.MaxPaletteColors);
        }
    }

    [Fact]
    public void Apply_LeavesBboxesAndDescsUntouched()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(6), MutationTestData.Context(source))!;

        var sourceElements = source.CompositionalDeconstruction.Elements;
        var resultElements = result.CompositionalDeconstruction.Elements;
        for (var i = 0; i < sourceElements.Count; i++)
        {
            resultElements[i].Bbox.Should().Equal(sourceElements[i].Bbox);
            resultElements[i].Desc.Should().Be(sourceElements[i].Desc);
        }
    }

    [Fact]
    public void Apply_IsDeterministicPerSeed_AndVariesAcrossSeeds()
    {
        var source = MutationTestData.BaseCaption();

        var a = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        var b = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        a.Should().Be(b);

        var distinct = Enumerable.Range(0, 30)
            .Select(s => _op.Apply(source, new Random(s), MutationTestData.Context(source)))
            .Where(r => r is not null)
            .Select(r => V4JsonPromptSerializer.Serialize(r!))
            .Distinct()
            .Count();
        distinct.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Apply_IsPure_SourceUnchanged()
    {
        var source = MutationTestData.BaseCaption();
        var before = V4JsonPromptSerializer.Serialize(source);

        _op.Apply(source, new Random(3), MutationTestData.Context(source));

        V4JsonPromptSerializer.Serialize(source).Should().Be(before);
    }

    [Fact]
    public void Apply_NoElementPalettes_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        foreach (var element in source.CompositionalDeconstruction.Elements)
            element.ColorPalette = null;

        var context = MutationTestData.Context(source);
        for (var seed = 0; seed < 20; seed++)
            _op.Apply(source, new Random(seed), context).Should().BeNull();
    }

    private static bool PaletteEquals(List<string>? a, List<string>? b) =>
        (a ?? []).SequenceEqual(b ?? [], StringComparer.Ordinal);
}
