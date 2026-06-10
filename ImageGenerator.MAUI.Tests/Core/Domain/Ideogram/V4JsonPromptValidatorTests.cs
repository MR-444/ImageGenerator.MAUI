using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram;

public class V4JsonPromptValidatorTests
{
    private static V4JsonPrompt ValidModel() => new()
    {
        HighLevelDescription = "A poster",
        StyleDescription = new StyleDescription { Medium = "poster", ArtStyle = "art deco" },
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = "navy sky",
            Elements =
            [
                new Element { Type = Element.ObjType, Bbox = [100, 200, 300, 400], Desc = "a lighthouse" },
                new Element { Type = Element.TextType, Text = "BEACON", Desc = "headline" }
            ]
        }
    };

    [Fact]
    public void Validate_ValidModel_ReturnsNoErrors()
    {
        V4JsonPromptValidator.Validate(ValidModel()).Should().BeEmpty();
    }

    [Fact]
    public void Validate_MissingHighLevelDescription_Fails()
    {
        var model = ValidModel();
        model.HighLevelDescription = "  ";

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("High-level description"));
    }

    [Fact]
    public void Validate_MissingBackground_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Background = "";

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("Background"));
    }

    [Fact]
    public void Validate_StyleWithoutMedium_Fails()
    {
        var model = ValidModel();
        model.StyleDescription!.Medium = null;

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("medium is required"));
    }

    [Fact]
    public void Validate_ArtStyleAndPhotoBothSet_Fails()
    {
        var model = ValidModel();
        model.StyleDescription!.Photo = "35mm film";

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("mutually exclusive"));
    }

    [Fact]
    public void Validate_NeitherArtStyleNorPhoto_Fails()
    {
        var model = ValidModel();
        model.StyleDescription!.ArtStyle = null;

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("either art_style"));
    }

    [Fact]
    public void Validate_PhotoOnly_IsValid()
    {
        var model = ValidModel();
        model.StyleDescription!.ArtStyle = null;
        model.StyleDescription.Photo = "35mm film";

        V4JsonPromptValidator.Validate(model).Should().BeEmpty();
    }

    [Fact]
    public void Validate_NoStyleDescription_IsValid()
    {
        var model = ValidModel();
        model.StyleDescription = null;

        V4JsonPromptValidator.Validate(model).Should().BeEmpty();
    }

    [Theory]
    [InlineData("#GGGGGG")]   // not hex
    [InlineData("112233")]    // missing #
    [InlineData("#aabbcc")]   // lowercase
    [InlineData("#1234")]     // wrong length
    public void Validate_BadHexColor_Fails(string color)
    {
        var model = ValidModel();
        model.StyleDescription!.ColorPalette = [color];

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains(color));
    }

    [Fact]
    public void Validate_StylePaletteOverSixteen_Fails()
    {
        var model = ValidModel();
        model.StyleDescription!.ColorPalette = Enumerable.Repeat("#112233", 17).ToList();

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("at most 16"));
    }

    [Fact]
    public void Validate_ElementPaletteOverFive_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[0].ColorPalette = Enumerable.Repeat("#112233", 6).ToList();

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("at most 5"));
    }

    [Fact]
    public void Validate_ElementWithoutDesc_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[0].Desc = null;

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("description is required"));
    }

    [Fact]
    public void Validate_TextElementWithoutText_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[1].Text = " ";

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("text content is required"));
    }

    [Fact]
    public void Validate_BboxWrongLength_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[0].Bbox = [1, 2, 3];

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("exactly 4"));
    }

    [Fact]
    public void Validate_BboxOutOfRange_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[0].Bbox = [0, -1, 500, 1001];

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("between 0 and 1000"));
    }

    [Fact]
    public void Validate_BboxMinGreaterThanMax_Fails()
    {
        var model = ValidModel();
        model.CompositionalDeconstruction.Elements[0].Bbox = [600, 100, 400, 200];

        V4JsonPromptValidator.Validate(model).Should().ContainSingle(e => e.Contains("y_min ≤ y_max"));
    }

    [Fact]
    public void ClampBbox_ClampsOntoGrid_AndCanonicalizesCorners()
    {
        V4JsonPromptValidator.ClampBbox([-50, 1200, 500, 100])
            .Should().Equal(0, 100, 500, 1000);
    }

    [Fact]
    public void ClampBbox_AlreadyCanonical_IsUnchanged()
    {
        V4JsonPromptValidator.ClampBbox([100, 200, 300, 400])
            .Should().Equal(100, 200, 300, 400);
    }

    [Fact]
    public void ClampBbox_WrongLength_Throws()
    {
        var act = () => V4JsonPromptValidator.ClampBbox([1, 2, 3]);

        act.Should().Throw<ArgumentException>();
    }
}
