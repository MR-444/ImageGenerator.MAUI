using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

/// <summary>
/// De-risks the whole slot-tag approach: a transient <see cref="Element.SlotTag"/> must never reach
/// any serialization (compact OR indented), and the canonical (de)serialize round-trip is the
/// tag-stripping boundary the engine's clone helper relies on. Written before the engine exists.
/// </summary>
public class ElementSlotTagSerializationTests
{
    private static V4JsonPrompt TaggedModel() => new()
    {
        HighLevelDescription = "A botanist in a garden",
        StyleDescription = new StyleDescription
        {
            Medium = "gouache",
            ArtStyle = "storybook illustration",
            ColorPalette = ["#2E5A3C"]
        },
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = "moonlit greenhouse",
            Elements =
            [
                new Element
                {
                    Type = Element.ObjType,
                    Bbox = [100, 200, 800, 600],
                    Desc = "a botanist in an embroidered jacket",
                    SlotTag = SlotTag.Subject.Garment
                },
                new Element
                {
                    Type = Element.ObjType,
                    Bbox = [400, 500, 600, 700],
                    Desc = "brass charms shaped as leaves",
                    SlotTag = SlotTag.Prop.Charms
                }
            ]
        }
    };

    [Fact]
    public void Serialize_Compact_NeverEmitsSlotTag()
    {
        var json = V4JsonPromptSerializer.Serialize(TaggedModel());

        json.Should().NotContain("slot");
        json.Should().NotContain("subject.garment");
        json.Should().NotContain("prop.charms");
    }

    [Fact]
    public void Serialize_Indented_NeverEmitsSlotTag()
    {
        var json = V4JsonPromptSerializer.Serialize(TaggedModel(), indented: true);

        json.Should().NotContain("slot");
        json.Should().NotContain("subject.garment");
        json.Should().NotContain("prop.charms");
    }

    [Fact]
    public void RoundTrip_StripsSlotTag()
    {
        var reparsed = V4JsonPromptSerializer.Deserialize(V4JsonPromptSerializer.Serialize(TaggedModel()));

        reparsed.CompositionalDeconstruction.Elements
            .Should().OnlyContain(e => e.SlotTag == null);
    }

    [Fact]
    public void SlotTag_DoesNotAffectCanonicalBytes()
    {
        var tagged = TaggedModel();
        var untagged = TaggedModel();
        foreach (var element in untagged.CompositionalDeconstruction.Elements)
            element.SlotTag = null;

        V4JsonPromptSerializer.Serialize(tagged)
            .Should().Be(V4JsonPromptSerializer.Serialize(untagged));
    }
}
