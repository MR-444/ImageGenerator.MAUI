using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate (as the VM does).
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

/// <summary>
/// CaptionDiff.Describe surfaces the SINGLE change a one-operator mutation made, OPERATOR-LED, so a
/// variant job card reads as a plain-English action ("Style → anime", "Moved: botanist (right)").
/// One assertion per change category, in the helper's priority order.
/// </summary>
public class CaptionDiffTests
{
    private static V4JsonPrompt Base() => new()
    {
        HighLevelDescription = "a scene",
        StyleDescription = new StyleDescription { ArtStyle = "gouache", Medium = "painting" },
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = "a meadow",
            Elements =
            [
                new Element { Type = Element.ObjType, Desc = "a botanist", Bbox = [100, 100, 500, 400] },
                new Element { Type = Element.ObjType, Desc = "a satchel", Bbox = [600, 600, 800, 800] }
            ]
        }
    };

    [Fact]
    public void Describe_ArtStyleChange_LeadsWithStyleArrowAndNewValue()
    {
        var v = Base();
        v.StyleDescription!.ArtStyle = "anime";
        CaptionDiff.Describe(Base(), v, "SwapStyle").Should().Be("Style → anime");
    }

    [Fact]
    public void Describe_MediumChange_WhenArtStyleEqual_StillReadsAsStyle()
    {
        var v = Base();
        v.StyleDescription!.Medium = "watercolor";
        CaptionDiff.Describe(Base(), v, "SwapStyle").Should().Be("Style → watercolor");
    }

    [Fact]
    public void Describe_BlendStyle_ShowsTheAddedWords()
    {
        var v = Base();
        v.StyleDescription!.ArtStyle = "gouache anime ink"; // base "gouache" + two new words
        CaptionDiff.Describe(Base(), v, "BlendStyle").Should().Be("Style blended (+ anime ink)");
    }

    [Fact]
    public void Describe_BlendStyle_AggregatesNewWordsAcrossStyleFields()
    {
        var v = Base();
        // art_style unchanged; the blend's fresh words live in aesthetics + lighting. The label must
        // still surface them (two blends differing only here used to read identically).
        v.StyleDescription!.Aesthetics = "moody";
        v.StyleDescription!.Lighting = "rim light";
        CaptionDiff.Describe(Base(), v, "BlendStyle").Should().Be("Style blended (+ moody rim light)");
    }

    [Fact]
    public void Describe_BackgroundChange_LeadsWithBackgroundArrow()
    {
        var v = Base();
        v.CompositionalDeconstruction.Background = "a desert at dusk";
        CaptionDiff.Describe(Base(), v, "SwapElementDesc").Should().Be("Background → desert at dusk");
    }

    [Fact]
    public void Describe_AddedElement_NamesIt()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements.Add(new Element { Type = Element.ObjType, Desc = "a luna moth" });
        CaptionDiff.Describe(Base(), v, "AddElement").Should().Be("Added: luna moth");
    }

    [Fact]
    public void Describe_RemovedElement_NamesIt()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements.RemoveAt(1);
        CaptionDiff.Describe(Base(), v, "RemoveElement").Should().Be("Removed: satchel");
    }

    [Fact]
    public void Describe_ElementDescChange_ReadsAsReworded()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Desc = "a weary botanist";
        CaptionDiff.Describe(Base(), v, "SwapElementDesc").Should().Be("Reworded: botanist");
    }

    [Fact]
    public void Describe_OrnamentKit_DescChange_ShowsAddedOrnamentWords()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Desc = "a botanist with brass charms";
        // "with" is a stop-word; only the fresh content words surface.
        CaptionDiff.Describe(Base(), v, "ApplyOrnamentKit").Should().Be("Ornament added: brass charms");
    }

    [Fact]
    public void Describe_ElementBboxChange_NamesElementAndDirection()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Bbox = [100, 400, 500, 700]; // shifted right in x
        CaptionDiff.Describe(Base(), v, "MutateBbox").Should().Be("Moved: botanist (right)");
    }

    [Fact]
    public void Describe_ElementPaletteChange_ReadsAsRecolored()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].ColorPalette = ["#112233"];
        CaptionDiff.Describe(Base(), v, "MutatePalette").Should().Be("Recolored: botanist");
    }

    [Fact]
    public void Describe_NoStructuralChange_FallsBackToFriendlyOperatorName()
    {
        CaptionDiff.Describe(Base(), Base(), "SwapStyle").Should().Be("Style changed");
        CaptionDiff.Describe(Base(), Base(), null).Should().Be("Changed");
    }

    [Fact]
    public void Describe_GistsLongNewValuesWithEllipsis_NotAHeadTruncatedPrefix()
    {
        var v = Base();
        v.StyleDescription!.ArtStyle = "anime cel-shaded illustration with bold ink outlines and flat color";
        var label = CaptionDiff.Describe(Base(), v, "SwapStyle");
        label.Should().StartWith("Style → ").And.EndWith("…");
    }

    [Fact]
    public void FriendlyOperator_MapsEachOperatorToPlainEnglish()
    {
        CaptionDiff.FriendlyOperator("MutatePalette").Should().Be("Recolored an element");
        CaptionDiff.FriendlyOperator("ApplyOrnamentKit").Should().Be("Ornament added");
        CaptionDiff.FriendlyOperator(null).Should().Be("Changed");
    }
}
