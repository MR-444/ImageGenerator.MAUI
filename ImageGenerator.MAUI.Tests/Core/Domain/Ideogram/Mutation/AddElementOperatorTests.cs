using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class AddElementOperatorTests
{
    private readonly AddElementOperator _op = new();

    [Fact]
    public void Apply_AddsExactlyOneElement_AndIsValidatorClean()
    {
        var source = MutationTestData.BaseCaption();
        var before = source.CompositionalDeconstruction.Elements.Count;

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        result.CompositionalDeconstruction.Elements.Count.Should().Be(before + 1);
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();
    }

    [Fact]
    public void Apply_NeverOverlapsSubject()
    {
        var source = MutationTestData.BaseCaption();
        var subjectBox = source.CompositionalDeconstruction.Elements[0].Bbox!;  // subject.garment
        var context = MutationTestData.Context(source);

        for (var seed = 0; seed < 200; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            var added = result.CompositionalDeconstruction.Elements[^1].Bbox;
            if (added is { Length: 4 })
                BboxMath.IoU(added, subjectBox).Should().BeLessThanOrEqualTo(0.4 + 1e-9);
        }
    }

    [Fact]
    public void Apply_EmptySceneElements_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        var context = MutationTestData.Context(source, MutationLibrary.Empty);

        for (var seed = 0; seed < 20; seed++)
            _op.Apply(source, new Random(seed), context).Should().BeNull();
    }

    [Fact]
    public void Apply_IsDeterministicPerSeed_AndVariesAcrossSeeds()
    {
        var source = MutationTestData.BaseCaption();

        var a = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        var b = V4JsonPromptSerializer.Serialize(_op.Apply(source, new Random(5), MutationTestData.Context(source))!);
        a.Should().Be(b);

        var distinct = Enumerable.Range(0, 40)
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
}
