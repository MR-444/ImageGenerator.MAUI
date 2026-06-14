using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class SwapElementDescOperatorTests
{
    private readonly SwapElementDescOperator _op = new();

    [Fact]
    public void Apply_SwapsExactlyOneDesc_ToASameSlotLibraryAlternate()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();

        var sourceElements = source.CompositionalDeconstruction.Elements;
        var resultElements = result.CompositionalDeconstruction.Elements;

        var changed = Enumerable.Range(0, sourceElements.Count)
            .Where(i => sourceElements[i].Desc != resultElements[i].Desc)
            .ToList();
        changed.Should().HaveCount(1);

        var newDesc = resultElements[changed[0]].Desc;
        MutationTestData.SceneElements().Select(t => t.Desc).Should().Contain(newDesc);
    }

    [Fact]
    public void Apply_NeverRewritesSubjectIdentity()
    {
        var source = MutationTestData.BaseCaption();
        source.CompositionalDeconstruction.Elements[0].SlotTag = SlotTag.Subject.Identity;
        var identityDesc = source.CompositionalDeconstruction.Elements[0].Desc;
        var context = MutationTestData.Context(source);

        for (var seed = 0; seed < 60; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            result.CompositionalDeconstruction.Elements[0].Desc.Should().Be(identityDesc);
        }
    }

    [Fact]
    public void Apply_KeepsSwappedDesc_WithinWordBudget()
    {
        var source = MutationTestData.BaseCaption();
        var context = MutationTestData.Context(source);

        for (var seed = 0; seed < 40; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            foreach (var element in result.CompositionalDeconstruction.Elements)
                DescBudget.CountWords(element.Desc).Should().BeLessThanOrEqualTo(DescBudget.MaxWords);
        }
    }

    [Fact]
    public void Apply_NoAlternate_ReturnsNull()
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
}
