namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Resolves a <see cref="SlotTag"/> for each element so ornament kits know where to inject. Resolution
/// precedence per element: an explicit <see cref="Element.SlotTag"/> set in the editor, then a
/// caller-supplied tag map, then keyword inference over the element's desc. Elements that match nothing
/// are simply absent from the map.
/// </summary>
public static class SlotTagger
{
    // Priority-ordered: the first group whose keyword appears in the desc wins. Order matters — more
    // specific roles precede broader ones (e.g. charms before flora so a charm-hung vine reads as charms;
    // "tuning fork" not bare "fork" so a flower "tipped toward the fork" isn't mistagged an instrument).
    private static readonly (string Tag, string[] Keywords)[] InferenceRules =
    [
        (SlotTag.Subject.Garment,
            ["jacket", "tunic", "coat", "garment", "harness", "embroidered", "embroidery", "collar",
             "dress", "robe", "cloak", "uniform", "gown", "vestment"]),
        (SlotTag.Prop.Charms,
            ["charm", "locket", "medallion", "pendant", "amulet", "talisman", "trinket"]),
        (SlotTag.Prop.Instrument,
            ["tuning fork", "instrument", "wand", "scalpel", "stylus", "baton"]),
        (SlotTag.Scene.Flora,
            ["vine", "foliage", "flower", "bloom", "blossom", "petal", "leaf", "fern", "moss",
             "frond", "ivy", "sprig"]),
    ];

    public static IReadOnlyDictionary<Element, string> Resolve(
        V4JsonPrompt caption,
        IReadOnlyDictionary<Element, string>? callerTags = null)
    {
        ArgumentNullException.ThrowIfNull(caption);

        var map = new Dictionary<Element, string>();
        foreach (var element in caption.CompositionalDeconstruction.Elements)
        {
            var tag = ResolveOne(element, callerTags);
            if (tag is not null)
                map[element] = tag;
        }

        return map;
    }

    private static string? ResolveOne(Element element, IReadOnlyDictionary<Element, string>? callerTags)
    {
        if (!string.IsNullOrWhiteSpace(element.SlotTag))
            return element.SlotTag;

        if (callerTags is not null && callerTags.TryGetValue(element, out var caller))
            return caller;

        if (element.Type == Element.TextType)
            return SlotTag.Text.Headline;

        return Infer(element.Desc);
    }

    private static string? Infer(string? desc)
    {
        if (string.IsNullOrWhiteSpace(desc))
            return null;

        foreach (var (tag, keywords) in InferenceRules)
        {
            foreach (var keyword in keywords)
            {
                if (desc.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return tag;
            }
        }

        return null;
    }
}
