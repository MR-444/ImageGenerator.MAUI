using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Operators;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class MutateBboxOperatorTests
{
    private readonly MutateBboxOperator _op = new();

    [Fact]
    public void Apply_ProducesValidatorCleanResult()
    {
        var source = MutationTestData.BaseCaption();
        var result = _op.Apply(source, new Random(1), MutationTestData.Context(source))!;

        result.Should().NotBeNull();
        V4JsonPromptValidator.Validate(result).Should().BeEmpty();
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
    public void Apply_NeverMutatesIdentityElement()
    {
        var source = MutationTestData.BaseCaption();
        source.CompositionalDeconstruction.Elements[0].SlotTag = SlotTag.Subject.Identity;
        var identityBox = (int[])source.CompositionalDeconstruction.Elements[0].Bbox!.Clone();
        var context = MutationTestData.Context(source);

        for (var seed = 0; seed < 60; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            result.CompositionalDeconstruction.Elements[0].Bbox.Should().Equal(identityBox);
        }
    }

    [Fact]
    public void Apply_NoBoxedNonIdentityElement_ReturnsNull()
    {
        var source = MutationTestData.BaseCaption();
        // Collapse to a single, identity-tagged element — the only candidate is excluded.
        var subject = source.CompositionalDeconstruction.Elements[0];
        subject.SlotTag = SlotTag.Subject.Identity;
        source.CompositionalDeconstruction.Elements.Clear();
        source.CompositionalDeconstruction.Elements.Add(subject);

        var context = MutationTestData.Context(source);
        for (var seed = 0; seed < 20; seed++)
            _op.Apply(source, new Random(seed), context).Should().BeNull();
    }

    [Theory]
    [InlineData(MutationStrength.Subtle, MutationStrength.Moderate)]
    [InlineData(MutationStrength.Moderate, MutationStrength.Bold)]
    public void Apply_RespectsStrength_LargerStepsAtHigherStrength(MutationStrength lower, MutationStrength higher)
    {
        var source = MutationTestData.BaseCaption();
        MeanCoordShift(source, lower).Should().BeLessThan(MeanCoordShift(source, higher));
    }

    [Fact]
    public void Apply_AspectAware_NarrowsBoxesOnWideFramesVersusTall()
    {
        var source = MutationTestData.BaseCaption();

        var wide = MeanChangedAspect(source, 1600, 900);
        var tall = MeanChangedAspect(source, 900, 1600);

        wide.Should().BeLessThan(tall);
    }

    [Fact]
    public void Apply_NeverLeavesSubjectOverlappingDuplicate()
    {
        // Two same-size boxes that start fully overlapping: one subject, one movable scene element.
        var source = TwoBoxCaption();
        var context = MutationTestData.Context(source, MutationStrength.Bold);

        var produced = 0;
        for (var seed = 0; seed < 300; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            produced++;
            var a = result.CompositionalDeconstruction.Elements[0].Bbox!;
            var b = result.CompositionalDeconstruction.Elements[1].Bbox!;
            BboxMath.IoU(a, b).Should().BeLessThanOrEqualTo(0.6 + 1e-9);
        }

        produced.Should().BeGreaterThan(0);
    }

    private double MeanCoordShift(V4JsonPrompt source, MutationStrength strength)
    {
        var context = MutationTestData.Context(source, strength);
        var shifts = new List<double>();
        for (var seed = 0; seed < 200; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            shifts.Add(TotalCoordDelta(source, result));
        }

        return shifts.Average();
    }

    private double MeanChangedAspect(V4JsonPrompt source, int width, int height)
    {
        var context = MutationTestData.Context(source, width, height);
        var aspects = new List<double>();
        for (var seed = 0; seed < 400; seed++)
        {
            var result = _op.Apply(source, new Random(seed), context);
            if (result is null)
                continue;
            var idx = ChangedIndex(source, result);
            if (idx < 0)
                continue;
            var box = result.CompositionalDeconstruction.Elements[idx].Bbox!;
            var h = BboxMath.Height(box);
            if (h > 0)
                aspects.Add((double)BboxMath.Width(box) / h);
        }

        return aspects.Average();
    }

    private static double TotalCoordDelta(V4JsonPrompt a, V4JsonPrompt b)
    {
        double total = 0;
        var ea = a.CompositionalDeconstruction.Elements;
        var eb = b.CompositionalDeconstruction.Elements;
        for (var i = 0; i < ea.Count; i++)
        {
            if (ea[i].Bbox is not { } ba || eb[i].Bbox is not { } bb)
                continue;
            for (var c = 0; c < 4; c++)
                total += Math.Abs(ba[c] - bb[c]);
        }

        return total;
    }

    private static int ChangedIndex(V4JsonPrompt a, V4JsonPrompt b)
    {
        var ea = a.CompositionalDeconstruction.Elements;
        var eb = b.CompositionalDeconstruction.Elements;
        for (var i = 0; i < ea.Count; i++)
        {
            if (ea[i].Bbox is { } ba && eb[i].Bbox is { } bb && !ba.SequenceEqual(bb))
                return i;
        }

        return -1;
    }

    private static V4JsonPrompt TwoBoxCaption()
    {
        var caption = new V4JsonPrompt
        {
            HighLevelDescription = "A figure beside an object in a dim room.",
            CompositionalDeconstruction = new CompositionalDeconstruction
            {
                Background = "A plain dim grey room with soft even light and no furniture.",
                Elements =
                [
                    new Element { Type = Element.ObjType, Bbox = [300, 300, 700, 700], Desc = "A standing figure in a plain tunic.", SlotTag = SlotTag.Subject.Garment },
                    new Element { Type = Element.ObjType, Bbox = [300, 300, 700, 700], Desc = "A small potted fern on a stand.", SlotTag = SlotTag.Scene.Flora }
                ]
            }
        };

        return caption;
    }
}
