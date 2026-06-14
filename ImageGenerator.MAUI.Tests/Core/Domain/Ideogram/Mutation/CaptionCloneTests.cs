using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

public class CaptionCloneTests
{
    private static V4JsonPrompt Model() => new()
    {
        HighLevelDescription = "A botanist in a garden",
        StyleDescription = new StyleDescription { Medium = "gouache", ArtStyle = "storybook", ColorPalette = ["#2E5A3C"] },
        CompositionalDeconstruction = new CompositionalDeconstruction
        {
            Background = "moonlit greenhouse",
            Elements =
            [
                new Element { Type = Element.ObjType, Bbox = [100, 200, 800, 600], Desc = "a botanist", SlotTag = SlotTag.Subject.Garment }
            ]
        }
    };

    [Fact]
    public void Clone_IsDistinctInstance_ButByteIdentical()
    {
        var source = Model();

        var clone = CaptionClone.Clone(source);

        clone.Should().NotBeSameAs(source);
        clone.CompositionalDeconstruction.Elements[0].Should().NotBeSameAs(source.CompositionalDeconstruction.Elements[0]);
        V4JsonPromptSerializer.Serialize(clone).Should().Be(V4JsonPromptSerializer.Serialize(source));
    }

    [Fact]
    public void Clone_DropsTransientSlotTags()
    {
        var clone = CaptionClone.Clone(Model());

        clone.CompositionalDeconstruction.Elements.Should().OnlyContain(e => e.SlotTag == null);
    }

    [Fact]
    public void Clone_MutatingClone_DoesNotAffectSource()
    {
        var source = Model();

        var clone = CaptionClone.Clone(source);
        clone.CompositionalDeconstruction.Elements[0].Desc = "MUTATED";

        source.CompositionalDeconstruction.Elements[0].Desc.Should().Be("a botanist");
    }
}
