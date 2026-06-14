using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class SlotTaggerTests
{
    [Fact]
    public void Resolve_InfersTagsFromDesc_OnBotanistGolden()
    {
        var caption = MutationTestData.BaseCaption();
        var elements = caption.CompositionalDeconstruction.Elements;

        var tags = SlotTagger.Resolve(caption);

        tags[elements[0]].Should().Be(SlotTag.Subject.Garment);   // "work tunic", "high-collared"
        tags[elements[1]].Should().Be(SlotTag.Scene.Flora);       // "bell-shaped flower", "petals"
        tags[elements[2]].Should().Be(SlotTag.Prop.Charms);       // "charms" wins over "vine"
        tags[elements[3]].Should().Be(SlotTag.Scene.Flora);       // "flowers"; "silver fork" is NOT "tuning fork"
        tags[elements[4]].Should().Be(SlotTag.Prop.Instrument);   // "tuning fork"
    }

    [Fact]
    public void Resolve_ExplicitSlotTag_BeatsInference()
    {
        var caption = MutationTestData.BaseCaption();
        var floraElement = caption.CompositionalDeconstruction.Elements[1];
        floraElement.SlotTag = SlotTag.Subject.Identity;

        SlotTagger.Resolve(caption)[floraElement].Should().Be(SlotTag.Subject.Identity);
    }

    [Fact]
    public void Resolve_CallerMap_BeatsInference_ButLosesToExplicit()
    {
        var caption = MutationTestData.BaseCaption();
        var elements = caption.CompositionalDeconstruction.Elements;
        elements[0].SlotTag = SlotTag.Subject.Identity; // explicit on element 0
        var callerTags = new Dictionary<Element, string>
        {
            [elements[0]] = SlotTag.Prop.Charms,        // ignored: explicit wins
            [elements[1]] = SlotTag.Prop.Instrument,    // beats flora inference
        };

        var tags = SlotTagger.Resolve(caption, callerTags);

        tags[elements[0]].Should().Be(SlotTag.Subject.Identity);
        tags[elements[1]].Should().Be(SlotTag.Prop.Instrument);
    }

    [Fact]
    public void Resolve_TextElement_TagsHeadline()
    {
        var caption = new V4JsonPrompt
        {
            HighLevelDescription = "x",
            CompositionalDeconstruction = new CompositionalDeconstruction
            {
                Background = "y",
                Elements = [new Element { Type = Element.TextType, Text = "BEACON", Desc = "a bold sign" }]
            }
        };

        var element = caption.CompositionalDeconstruction.Elements[0];
        SlotTagger.Resolve(caption)[element].Should().Be(SlotTag.Text.Headline);
    }

    [Fact]
    public void Resolve_UnmatchedElement_IsAbsentFromMap()
    {
        var caption = new V4JsonPrompt
        {
            HighLevelDescription = "x",
            CompositionalDeconstruction = new CompositionalDeconstruction
            {
                Background = "y",
                Elements = [new Element { Type = Element.ObjType, Desc = "a plain grey cube on a table" }]
            }
        };

        SlotTagger.Resolve(caption).Should().BeEmpty();
    }
}
