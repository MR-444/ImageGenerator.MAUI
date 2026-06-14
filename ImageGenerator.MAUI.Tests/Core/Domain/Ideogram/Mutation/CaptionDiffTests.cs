using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate (as the VM does).
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

/// <summary>
/// CaptionDiff.Describe surfaces the SINGLE change a one-operator mutation made, so a variant job
/// card can show "what changed". One assertion per change category, in the helper's priority order.
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
    public void Describe_ArtStyleChange_ReportsStyle()
    {
        var v = Base();
        v.StyleDescription!.ArtStyle = "anime";
        CaptionDiff.Describe(Base(), v).Should().Be("art style: gouache → anime");
    }

    [Fact]
    public void Describe_MediumChange_WhenArtStyleEqual_ReportsMedium()
    {
        var v = Base();
        v.StyleDescription!.Medium = "watercolor";
        CaptionDiff.Describe(Base(), v).Should().Be("medium: painting → watercolor");
    }

    [Fact]
    public void Describe_BackgroundChange()
    {
        var v = Base();
        v.CompositionalDeconstruction.Background = "a desert";
        CaptionDiff.Describe(Base(), v).Should().Be("background changed");
    }

    [Fact]
    public void Describe_AddedElement_NamesIt()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements.Add(new Element { Type = Element.ObjType, Desc = "a luna moth" });
        CaptionDiff.Describe(Base(), v).Should().Be("added: a luna moth");
    }

    [Fact]
    public void Describe_RemovedElement_NamesIt()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements.RemoveAt(1);
        CaptionDiff.Describe(Base(), v).Should().Be("removed: a satchel");
    }

    [Fact]
    public void Describe_ElementDescChange()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Desc = "a weary botanist";
        CaptionDiff.Describe(Base(), v).Should().Be("desc: a botanist → a weary botanist");
    }

    [Fact]
    public void Describe_ElementBboxChange_ReportsPlacement()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Bbox = [120, 120, 520, 420];
        CaptionDiff.Describe(Base(), v).Should().Be("placement moved");
    }

    [Fact]
    public void Describe_ElementPaletteChange()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].ColorPalette = ["#112233"];
        CaptionDiff.Describe(Base(), v).Should().Be("element palette changed");
    }

    [Fact]
    public void Describe_NoStructuralChange_FallsBackToChanged()
    {
        CaptionDiff.Describe(Base(), Base()).Should().Be("changed");
    }

    [Fact]
    public void Describe_TruncatesLongFieldValues()
    {
        var v = Base();
        v.CompositionalDeconstruction.Elements[0].Desc =
            "a botanist wearing an extraordinarily long and elaborate ceremonial tunic";
        CaptionDiff.Describe(Base(), v).Should().Contain("→").And.Contain("…");
    }
}
