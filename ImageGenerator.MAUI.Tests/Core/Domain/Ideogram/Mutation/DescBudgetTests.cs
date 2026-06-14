using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class DescBudgetTests
{
    private static string Words(int n) => string.Join(" ", Enumerable.Repeat("x", n));

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("one", 1)]
    [InlineData("one two three", 3)]
    [InlineData("  spaced   out \t tabbed \n newline ", 4)]
    public void CountWords_MatchesPythonSplit(string text, int expected)
    {
        DescBudget.CountWords(text).Should().Be(expected);
    }

    [Fact]
    public void Fit_OverBudget_DropsHighestTiersFirst()
    {
        // 50-word protected base, 10 words of headroom to 60; each candidate is 4 words, so only two fit.
        var candidates = new List<OrnamentPhrase>
        {
            new("drop micro one two", DescBudgetCategory.EnvironmentalMicroDetail),
            new("drop color one two", DescBudgetCategory.ColorBeyondHarmony),
            new("kept frame one two", DescBudgetCategory.SecondaryFrameDevice),
            new("kept marker one two", DescBudgetCategory.StyleMarker),
        };

        var result = DescBudget.Fit(Words(50), candidates);

        result.Should().NotBeNull();
        DescBudget.CountWords(result).Should().BeLessThanOrEqualTo(DescBudget.MaxWords);
        result.Should().Contain("marker").And.Contain("frame");          // lowest drop-priority kept
        result.Should().NotContain("color").And.NotContain("micro");      // higher tiers dropped first
    }

    [Fact]
    public void Fit_KeptPhrases_EmittedInAuthoredOrder()
    {
        // Both fit (50 + 4 + 4 = 58); authored order is frame-then-marker, output must preserve that.
        var candidates = new List<OrnamentPhrase>
        {
            new("frame aaa bbb ccc", DescBudgetCategory.SecondaryFrameDevice),
            new("marker ddd eee fff", DescBudgetCategory.StyleMarker),
        };

        var result = DescBudget.Fit(Words(50), candidates)!;

        result.IndexOf("frame", StringComparison.Ordinal)
            .Should().BeLessThan(result.IndexOf("marker", StringComparison.Ordinal));
    }

    [Fact]
    public void Fit_GreedilySkipsTooBigPhrase_ButTakesSmallerLaterOne()
    {
        var candidates = new List<OrnamentPhrase>
        {
            new("this phrase is far too big to fit", DescBudgetCategory.StyleMarker), // 8 words, base+8 > 60
            new("tiny", DescBudgetCategory.SecondaryFrameDevice),                     // 1 word, fits
        };

        var result = DescBudget.Fit(Words(58), candidates)!;

        result.Should().Contain("tiny");
        result.Should().NotContain("far too big");
        DescBudget.CountWords(result).Should().BeLessThanOrEqualTo(DescBudget.MaxWords);
    }

    [Fact]
    public void Fit_NoPhrasesFit_ReturnsBaseUnchanged()
    {
        var candidates = new List<OrnamentPhrase>
        {
            new("too many words to ever fit here now", DescBudgetCategory.StyleMarker),
        };

        DescBudget.Fit(Words(60), candidates).Should().Be(Words(60));
    }

    [Fact]
    public void Fit_ProtectedBaseOverBudget_ReturnsNull()
    {
        DescBudget.Fit(Words(61), [new OrnamentPhrase("x", DescBudgetCategory.StyleMarker)])
            .Should().BeNull();
    }

    [Fact]
    public void Fit_IsDeterministic()
    {
        var candidates = new List<OrnamentPhrase>
        {
            new("kept marker one two", DescBudgetCategory.StyleMarker),
            new("drop micro one two", DescBudgetCategory.EnvironmentalMicroDetail),
        };

        DescBudget.Fit(Words(50), candidates).Should().Be(DescBudget.Fit(Words(50), candidates));
    }
}
