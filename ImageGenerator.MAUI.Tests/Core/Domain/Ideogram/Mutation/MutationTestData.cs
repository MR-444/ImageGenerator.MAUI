using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Shared fixtures for the LOOK-operator tests: the gouache botanist golden as the mutation base, plus an
/// in-memory <see cref="MutationLibrary"/> (gouache = current style, anime + density alternatives, and a
/// density ornament kit). The library service that loads these from JSON is a later phase; here they are
/// built in-code so the operators can be unit-tested in isolation.
/// </summary>
internal static class MutationTestData
{
    // documents/mutations/botanist_caption.json — gouache / fine-art baseline (verifier-clean).
    private const string GouacheBaseJson =
        """
        {
          "high_level_description": "A gouache painting in close vertical portrait of a rabbit-eared young adult botanist touching a silver tuning fork to a glowing bell-flower inside a black-lacquer greenhouse at night, tender and quietly occult in mood.",
          "style_description": {
            "aesthetics": "tender, occult, deadpan-whimsical",
            "lighting": "cool moonlight from above and behind, low inner glow from the flowers lifting the face from beneath",
            "medium": "painting",
            "art_style": "gouache with crisp ink linework, translucent petal glazes, matte opaque skin, lost-and-found edges over cold-press paper tooth",
            "color_palette": ["#0E0F14", "#2C4A3E", "#3F6B57", "#5B6E8C", "#C8D6E0", "#E8B998", "#B5202A", "#C0C6CC"]
          },
          "compositional_deconstruction": {
            "background": "A black-lacquer greenhouse interior at night, glossy near-black framing bars rising between tall glass panes that open onto a dark sky, faint moonlight washing the upper glass and deep shadow filling the lower structure, the air dim and still.",
            "elements": [
              {
                "type": "obj",
                "bbox": [180, 220, 1000, 820],
                "desc": "A young adult botanist with fair peach skin and tall upright rabbit ears rising above soft dark hair pulled back loosely, wearing a high-collared deep-jade work tunic, standing in a calm three-quarter turn with her near forearm raised, gaze lowered to a flower in deadpan concentration, lips pressed in quiet focus.",
                "color_palette": ["#E8B998", "#2C4A3E", "#3A2E2A"]
              },
              {
                "type": "obj",
                "bbox": [60, 300, 460, 830],
                "desc": "An oversized bell-shaped flower with broad translucent pale petals flushed faintly blue at the rims, its open mouth turned outward, arcing behind the botanist's head slightly off-center like a halo, a single red thread looped through its throat, the green stem curving down to the right.",
                "color_palette": ["#C8D6E0", "#5B6E8C", "#B5202A"]
              },
              {
                "type": "obj",
                "bbox": [150, 120, 720, 340],
                "desc": "Several thin green vine strings trailing downward along the left side of the frame, each hung with tiny tarnished-brass charms shaped as bells and leaves, knotted at irregular intervals with fine red thread, the lowest charms reaching toward the botanist's shoulder.",
                "color_palette": ["#3F6B57", "#9A7B3A", "#B5202A"]
              },
              {
                "type": "obj",
                "bbox": [520, 540, 840, 900],
                "desc": "A low cluster of small bell-shaped flowers with pale petals and faintly glowing throats, gathered in the lower-right of the frame near the botanist's raised hand, several blooms tipped toward the silver fork, fine red thread tied around two of the slender stems.",
                "color_palette": ["#C8D6E0", "#3F6B57", "#B5202A"]
              },
              {
                "type": "obj",
                "bbox": [430, 470, 620, 640],
                "desc": "A slender silver tuning fork held upright between the botanist's fingers, its two narrow polished tines bright against the darker greenhouse behind, positioned mid-frame with the tips nearly touching the rim of the largest bell-flower.",
                "color_palette": ["#C0C6CC", "#0E0F14"]
              }
            ]
          }
        }
        """;

    /// <summary>The gouache botanist golden, deserialized fresh each call (callers may mutate it).</summary>
    public static V4JsonPrompt BaseCaption() => V4JsonPromptSerializer.Deserialize(GouacheBaseJson);

    public static StyleDescription AnimeStyle() => new()
    {
        Aesthetics = "occult-botanical reverie, tender and melancholic, ornate",
        Lighting = "nocturnal low-key, warm internal glow from the flowers against a deep cool-dark ground, cool moonlight rim",
        Medium = "illustration",
        ArtStyle = "high-detail anime illustration, soft cel-shaded painterly rendering, large expressive eyes, fine ornamental linework, luminous bloom-light",
        ColorPalette = ["#0B0E12", "#16231C", "#2C4A3E", "#3C5A6E", "#E8C27A", "#F2E2B0", "#B5202A", "#C8B98A"]
    };

    public static StyleDescription DensityStyle() => new()
    {
        Aesthetics = "occult-botanical reverie, tender and melancholic, densely ornate",
        Lighting = "nocturnal low-key, warm internal glow from the flowers against a deep cool-dark ground, cool moonlight rim",
        Medium = "illustration",
        ArtStyle = "polished high-detail anime illustration, smooth gradient cel-shading, glossy idealized features with large luminous eyes, crisp ornamental linework, glowing bloom-light against deep shadow",
        ColorPalette = ["#0B0E12", "#16231C", "#2C4A3E", "#3C5A6E", "#E8C27A", "#F2E2B0", "#B5202A", "#C8B98A"]
    };

    /// <summary>Density-look ornament kit: garment + charm phrases keyed by slot, mixed budget tiers.</summary>
    public static OrnamentKit DensityKit() => new(
        "density",
        new Dictionary<string, IReadOnlyList<OrnamentPhrase>>
        {
            [SlotTag.Subject.Garment] =
            [
                new OrnamentPhrase("crossed by thin leather harness straps with small brass buckles", DescBudgetCategory.StyleMarker),
                new OrnamentPhrase("embroidered with pale botanical sprigs", DescBudgetCategory.SecondaryFrameDevice),
            ],
            [SlotTag.Prop.Charms] =
            [
                new OrnamentPhrase("engraved circular brass lockets", DescBudgetCategory.StyleMarker),
                new OrnamentPhrase("small botanical medallions on fine red thread", DescBudgetCategory.SecondaryFrameDevice),
            ]
        });

    /// <summary>
    /// Library whose first fragment IS the base's current style (so "exclude current" has something to
    /// exclude), plus the anime and density alternatives and the density kit.
    /// </summary>
    public static MutationLibrary Library()
    {
        var gouache = new StyleFragment("gouache", StyleMath.Clone(BaseCaption().StyleDescription!));
        return new MutationLibrary(
            [gouache, new StyleFragment("anime", AnimeStyle()), new StyleFragment("density", DensityStyle())],
            [DensityKit()]);
    }

    /// <summary>A context over the base caption: inferred tags + the test library, square target frame.</summary>
    public static MutationContext Context(V4JsonPrompt caption, MutationLibrary? library = null) =>
        new(1000, 1000, SlotTagger.Resolve(caption), library ?? Library());
}
