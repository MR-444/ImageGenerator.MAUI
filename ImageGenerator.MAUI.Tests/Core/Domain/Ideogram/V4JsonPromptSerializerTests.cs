using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram;

public class V4JsonPromptSerializerTests
{
    private static V4JsonPrompt FullModel() => new()
    {
        HighLevelDescription = "A poster",
        StyleDescription = new StyleDescription
        {
            Medium = "poster",
            ArtStyle = "art deco",
            ColorPalette = ["#112233"]
        },
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = "navy sky",
            Elements =
            [
                new Element { Type = Element.ObjType, Bbox = [100, 200, 300, 400], Desc = "a lighthouse" },
                new Element { Type = Element.TextType, Bbox = [0, 0, 100, 1000], Text = "BEACON", Desc = "headline", ColorPalette = ["#FFFFFF"] }
            ]
        }
    };

    // The Replicate cog takes json_prompt as a STRING; this pins byte-for-byte parity with
    // Python's json.dumps(separators=(",",":")) so nobody ever hand-rolls a minifier.
    [Fact]
    public void Serialize_Compact_MatchesExactString()
    {
        var json = V4JsonPromptSerializer.Serialize(FullModel());

        json.Should().Be(
            "{\"high_level_description\":\"A poster\"," +
            "\"style_description\":{\"medium\":\"poster\",\"art_style\":\"art deco\",\"color_palette\":[\"#112233\"]}," +
            "\"compositional_deconstruction\":{\"background\":\"navy sky\",\"elements\":[" +
            "{\"type\":\"obj\",\"bbox\":[100,200,300,400],\"desc\":\"a lighthouse\"}," +
            "{\"type\":\"text\",\"bbox\":[0,0,100,1000],\"text\":\"BEACON\",\"desc\":\"headline\",\"color_palette\":[\"#FFFFFF\"]}]}}");
    }

    [Fact]
    public void Serialize_Compact_HasNoWhitespaceOutsideValues()
    {
        var model = new V4JsonPrompt
        {
            HighLevelDescription = "x",
            CompositionalDeconstruction = new CompositionalDeconstruction { Background = "y" }
        };

        V4JsonPromptSerializer.Serialize(model).Should().NotContain(" ");
    }

    [Fact]
    public void Serialize_Indented_IsMultiLine_AndRoundTripsToSameCompactString()
    {
        var compact = V4JsonPromptSerializer.Serialize(FullModel());
        var pretty = V4JsonPromptSerializer.Serialize(FullModel(), indented: true);

        pretty.Should().Contain(Environment.NewLine);
        var reparsed = V4JsonPromptSerializer.Deserialize(pretty);
        V4JsonPromptSerializer.Serialize(reparsed).Should().Be(compact);
    }

    [Fact]
    public void RoundTrip_CompactString_IsStable()
    {
        var compact = V4JsonPromptSerializer.Serialize(FullModel());

        var reparsed = V4JsonPromptSerializer.Deserialize(compact);

        V4JsonPromptSerializer.Serialize(reparsed).Should().Be(compact);
    }

    [Fact]
    public void Serialize_ObjElement_NeverEmitsTextKey()
    {
        var model = new V4JsonPrompt
        {
            HighLevelDescription = "x",
            CompositionalDeconstruction = new CompositionalDeconstruction
            {
                Background = "y",
                // Stale Text left over from editing must not leak into an obj element.
                Elements = [new Element { Type = Element.ObjType, Desc = "thing", Text = "stale" }]
            }
        };

        V4JsonPromptSerializer.Serialize(model).Should().NotContain("\"text\"");
    }

    [Fact]
    public void Serialize_OmitsOptionalKeys_WhenNullOrEmpty()
    {
        var model = new V4JsonPrompt
        {
            HighLevelDescription = "x",
            StyleDescription = null,
            CompositionalDeconstruction = new CompositionalDeconstruction
            {
                Background = "y",
                Elements = [new Element { Type = Element.ObjType, Desc = "thing", Bbox = null, ColorPalette = [] }]
            }
        };

        var json = V4JsonPromptSerializer.Serialize(model);

        json.Should().NotContain("style_description");
        json.Should().NotContain("bbox");
        json.Should().NotContain("color_palette");
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"high_level_description\":")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_Malformed_ThrowsParseException(string input)
    {
        var act = () => V4JsonPromptSerializer.Deserialize(input);

        act.Should().Throw<V4JsonPromptParseException>();
    }

    [Fact]
    public void Deserialize_UnknownElementType_ThrowsParseException()
    {
        const string json = "{\"high_level_description\":\"x\",\"compositional_deconstruction\":{\"background\":\"y\",\"elements\":[{\"type\":\"banana\",\"desc\":\"z\"}]}}";

        var act = () => V4JsonPromptSerializer.Deserialize(json);

        act.Should().Throw<V4JsonPromptParseException>();
    }

    [Fact]
    public void Deserialize_SkipsUnknownKeys_OnDocumentAndElement()
    {
        const string json = "{\"high_level_description\":\"x\",\"future_key\":123," +
                            "\"compositional_deconstruction\":{\"background\":\"y\",\"elements\":[" +
                            "{\"type\":\"obj\",\"desc\":\"z\",\"weight\":0.5,\"tags\":[\"a\"]}]}}";

        var model = V4JsonPromptSerializer.Deserialize(json);

        model.HighLevelDescription.Should().Be("x");
        model.CompositionalDeconstruction.Elements.Should().ContainSingle()
            .Which.Desc.Should().Be("z");
    }

    [Fact]
    public void Deserialize_ReadsBboxAndPalette()
    {
        const string json = "{\"high_level_description\":\"x\",\"compositional_deconstruction\":{\"background\":\"y\",\"elements\":[" +
                            "{\"type\":\"text\",\"bbox\":[10,20,30,40],\"text\":\"HI\",\"desc\":\"z\",\"color_palette\":[\"#ABCDEF\"]}]}}";

        var element = V4JsonPromptSerializer.Deserialize(json).CompositionalDeconstruction.Elements.Single();

        element.Bbox.Should().Equal(10, 20, 30, 40);
        element.Text.Should().Be("HI");
        element.ColorPalette.Should().Equal("#ABCDEF");
    }
}
