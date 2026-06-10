using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram;

/// <summary>
/// Pins compatibility with a real, externally authored V4 json_prompt
/// (documents/V4_example.json — full photographic style block, three obj elements, one text
/// element). Notably its style_description orders "photo" BEFORE "medium"; deserialization
/// must be key-order-agnostic even though our own serializer emits a fixed order.
/// </summary>
public class V4ExampleCompatibilityTests
{
    private const string ExampleJson =
        """
        {"high_level_description":"A young blonde woman seated on the hood of a vintage reddish-brown sedan at a sun-bleached 1970s gas station, captured on fine-grain faded color film in an off-center three-quarter composition with an old red fuel pump standing at the left.","style_description":{"aesthetics":"candid, nostalgic, documentary roadside Americana, 1970s color film with faded muted tones","lighting":"high front midday sun, hard short shadows, strong contrast softened by aged film response","photo":"near eye-level with a slight upward tilt, 35mm, deep focus, fine grain","medium":"photograph","color_palette":["#D8D2C4","#C9B79C","#8B4A3A","#B5322B","#5A6B82","#E8DEC8","#3A332C","#F2EAD8"]},"compositional_deconstruction":{"background":"A sun-bleached gas station forecourt under a pale hazy sky; cracked light-grey concrete pavement spreads across the foreground and middle distance; a low weathered cream clapboard building with a flat roof stands across the lot behind the car, framing a quiet open stretch of roadside Americana.","elements":[{"type":"obj","bbox":[230,470,800,770],"desc":"Fair-skinned young woman in her early twenties with long platinum-blonde hair over her shoulders, wearing a broad red felt cowboy hat marked with a white star and a patterned band, blue denim overalls with a star patch over a red-and-white striped top; neutral expression, leaning back on one palm pressed to the hood, one boot hooked on the bumper, knees uneven.","color_palette":["#B5322B","#5A6B82","#E8DEC8","#FFFFFF","#E8B79A"]},{"type":"obj","bbox":[450,250,900,920],"desc":"Vintage 1970s reddish-brown sedan with a rounded chrome bumper, glossy slightly oxidized paint, round headlights and a wide flat hood supporting the seated figure, whitewall tires, parked across the station forecourt.","color_palette":["#8B4A3A","#C0C0C0","#3A332C"]},{"type":"obj","bbox":[350,40,850,210],"desc":"Tall vintage red fuel pump with a rounded chrome-trimmed top, a glass-fronted dial face, a worn enamel body and a black rubber hose coiled on its side, standing on the forecourt at the far left.","color_palette":["#B5322B","#C0C0C0","#2A2622"]},{"type":"text","bbox":[180,300,280,560],"text":"LIQUOR","desc":"Weathered rectangular sign mounted on the clapboard building behind, bold condensed sans-serif capitals in faded red on a cream board.","color_palette":["#B5322B","#F2EAD8"]}]}}
        """;

    [Fact]
    public void Example_Deserializes_WithAllFieldsIntact()
    {
        var model = V4JsonPromptSerializer.Deserialize(ExampleJson);

        model.HighLevelDescription.Should().StartWith("A young blonde woman");
        model.StyleDescription.Should().NotBeNull();
        var style = model.StyleDescription!;
        style.Medium.Should().Be("photograph");
        style.Photo.Should().Contain("35mm");
        style.ArtStyle.Should().BeNull();
        style.ColorPalette.Should().HaveCount(8);

        var elements = model.CompositionalDeconstruction.Elements;
        elements.Should().HaveCount(4);
        elements.Take(3).Should().OnlyContain(e => e.Type == "obj" && e.Text == null);
        elements[3].Type.Should().Be("text");
        elements[3].Text.Should().Be("LIQUOR");
        elements[3].Bbox.Should().Equal(180, 300, 280, 560);
        elements[0].ColorPalette.Should().HaveCount(5);
    }

    [Fact]
    public void Example_PassesValidation()
    {
        var model = V4JsonPromptSerializer.Deserialize(ExampleJson);

        V4JsonPromptValidator.Validate(model).Should().BeEmpty();
    }

    // Our serializer emits style keys in a fixed declaration order (medium before photo),
    // so re-serializing this example is not byte-identical to the input — but it must be
    // STABLE: one normalization pass, then every further round-trip reproduces it exactly.
    [Fact]
    public void Example_RoundTrip_IsStableAfterOneNormalizationPass()
    {
        var normalized = V4JsonPromptSerializer.Serialize(V4JsonPromptSerializer.Deserialize(ExampleJson));

        var secondPass = V4JsonPromptSerializer.Serialize(V4JsonPromptSerializer.Deserialize(normalized));

        secondPass.Should().Be(normalized);
        normalized.Should().Contain("\"photo\":\"near eye-level");
        normalized.Should().Contain("\"text\":\"LIQUOR\"");
    }
}
