using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class RemoveElementOperatorTests
{
    private readonly RemoveElementOperator _op = new();

    [Fact]
    public void Apply_RemovesExactlyOneElement_AndIsValidatorClean()
    {
        var source = MutationTestData.BaseCaption();
        var before = source.CompositionalDeconstruction.Elements.Count;

        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        result.CompositionalDeconstruction.Elements.Count.Should().Be(before - 1);
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();
    }

    [Fact]
    public void Apply_NeverRemovesTheSubjectElement()
    {
        var source = MutationTestData.BaseCaption();
        var subjectDesc = source.CompositionalDeconstruction.Elements[0].Desc;  // the botanist (subject.garment)
        var context = MutationTestData.Context(source);

        for (var seed = 0; seed < 60; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            result.CompositionalDeconstruction.Elements
                .Should().Contain(e => e.Desc == subjectDesc);
        }
    }

    [Fact]
    public void Apply_AtElementFloor_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        // Trim down to the floor (subject + one removable).
        var elements = source.CompositionalDeconstruction.Elements;
        while (elements.Count > 2)
            elements.RemoveAt(elements.Count - 1);

        var context = MutationTestData.Context(source);
        for (var seed = 0; seed < 20; seed++)
            _op.Apply(source, new Random(seed), context).Should().BeNull();
    }

    [Fact]
    public void Apply_NoRemovableCandidate_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        // Three subject-tagged elements: above the floor, but nothing is safe to remove.
        foreach (var element in source.CompositionalDeconstruction.Elements)
            element.SlotTag = SlotTag.Subject.Garment;
        while (source.CompositionalDeconstruction.Elements.Count > 3)
            source.CompositionalDeconstruction.Elements.RemoveAt(3);

        var context = MutationTestData.Context(source);
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
