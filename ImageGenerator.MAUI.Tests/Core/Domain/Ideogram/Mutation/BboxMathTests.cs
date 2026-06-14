using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class BboxMathTests
{
    [Theory]
    [InlineData(MutationStrength.Subtle, 20.0)]
    [InlineData(MutationStrength.Moderate, 50.0)]
    [InlineData(MutationStrength.Bold, 100.0)]
    public void SigmaFor_MapsStrengthsTo_2_5_10_Percent(MutationStrength strength, double expected) =>
        BboxMath.SigmaFor(strength).Should().Be(expected);

    [Fact]
    public void NextGaussian_IsDeterministic_PerSeed()
    {
        var rngA = new Random(7);
        var rngB = new Random(7);
        var a = Enumerable.Range(0, 100).Select(_ => BboxMath.NextGaussian(rngA)).ToList();
        var b = Enumerable.Range(0, 100).Select(_ => BboxMath.NextGaussian(rngB)).ToList();

        a.Should().Equal(b);                          // same seed ⇒ identical sequence
        a.Distinct().Count().Should().BeGreaterThan(1); // a real varying sequence, not a constant
    }

    [Fact]
    public void NextGaussian_IsStateless_AcrossInstances()
    {
        // A textbook Box-Muller caches the 2nd variate in a static field; that would make these differ.
        var first = BboxMath.NextGaussian(new Random(123));
        var second = BboxMath.NextGaussian(new Random(123));
        second.Should().Be(first);
    }

    [Fact]
    public void NextGaussian_ApproximatesStandardNormal()
    {
        var rng = new Random(42);
        var samples = Enumerable.Range(0, 10_000).Select(_ => BboxMath.NextGaussian(rng)).ToList();

        var mean = samples.Average();
        var variance = samples.Select(x => (x - mean) * (x - mean)).Average();
        mean.Should().BeApproximately(0.0, 0.1);
        Math.Sqrt(variance).Should().BeApproximately(1.0, 0.1);
    }

    [Fact]
    public void Translate_PreservesWidthAndHeight()
    {
        int[] box = [100, 200, 400, 500];
        var moved = BboxMath.Translate(box, 30, -40);

        BboxMath.Width(moved).Should().Be(BboxMath.Width(box));
        BboxMath.Height(moved).Should().Be(BboxMath.Height(box));
        moved.Should().Equal(130, 160, 430, 460);
    }

    [Fact]
    public void Scale_PreservesCenter()
    {
        int[] box = [100, 200, 400, 600];   // center (250, 400)
        var scaled = BboxMath.Scale(box, 1.5, 0.5);

        var (cy, cx) = BboxMath.Center(scaled);
        cy.Should().BeApproximately(250, 0.5);
        cx.Should().BeApproximately(400, 0.5);
    }

    [Theory]
    [InlineData(new[] { 0, 0, 100, 100 }, new[] { 0, 0, 100, 100 }, 1.0)]      // identical
    [InlineData(new[] { 0, 0, 100, 100 }, new[] { 200, 200, 300, 300 }, 0.0)]  // disjoint
    [InlineData(new[] { 0, 0, 100, 100 }, new[] { 0, 0, 100, 200 }, 0.5)]      // half overlap
    public void IoU_KnownPairs(int[] a, int[] b, double expected) =>
        BboxMath.IoU(a, b).Should().BeApproximately(expected, 1e-9);

    [Theory]
    [InlineData(1600, 900)]   // wide → narrower x-span
    [InlineData(1000, 1000)]  // square → 1.0
    [InlineData(900, 1600)]   // tall → wider x-span
    public void GridAspectForSquareObject_FollowsFrame(int w, int h)
    {
        var aspect = BboxMath.GridAspectForSquareObject(w, h);
        aspect.Should().BeApproximately((double)h / w, 1e-9);
        if (w > h) aspect.Should().BeLessThan(1.0);
        else if (w < h) aspect.Should().BeGreaterThan(1.0);
        else aspect.Should().Be(1.0);
    }

    [Fact]
    public void IsDegenerate_FlagsCollapsedSpans()
    {
        BboxMath.IsDegenerate([100, 100, 110, 300], 15).Should().BeTrue();   // height 10 < 15
        BboxMath.IsDegenerate([100, 100, 300, 300], 15).Should().BeFalse();
    }
}
