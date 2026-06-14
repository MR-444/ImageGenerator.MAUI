namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// Friendly, human-readable names for the <see cref="SlotTag"/> vocabulary, used by the mutation
/// page's per-element slot picker. The engine speaks the dotted raw ids (<c>subject.identity</c>, …);
/// the UI shows these labels and maps back at run time. "Auto" maps to a <c>null</c> tag — the engine
/// then infers the slot from the element's keywords.
/// </summary>
public static class SlotTagDisplay
{
    /// <summary>Picker entry meaning "leave it to the app to infer from keywords" (writes a null tag).</summary>
    public const string Auto = "Auto (let the app guess)";

    private static readonly (string Raw, string Friendly)[] Map =
    [
        (SlotTag.Subject.Identity, "Subject – face/identity (locked)"),
        (SlotTag.Subject.Garment,  "Clothing / garment"),
        (SlotTag.Subject.Props,    "Subject's props"),
        (SlotTag.Prop.Charms,      "Charms / ornaments"),
        (SlotTag.Prop.Instrument,  "Instrument / tool"),
        (SlotTag.Scene.Flora,      "Foliage / plants"),
        (SlotTag.Text.Headline,    "Headline text"),
    ];

    /// <summary>Picker options in display order: Auto first, then each tag's friendly label.</summary>
    public static IReadOnlyList<string> Options { get; } = [Auto, .. Map.Select(m => m.Friendly)];

    /// <summary>Friendly label for a raw tag; <see cref="Auto"/> for null/unknown.</summary>
    public static string ToFriendly(string? raw)
    {
        if (raw is null) return Auto;
        foreach (var (r, f) in Map)
            if (r == raw) return f;
        return Auto;
    }

    /// <summary>Raw tag for a friendly label; <c>null</c> for Auto/unknown (engine infers).</summary>
    public static string? ToRaw(string? friendly)
    {
        if (string.IsNullOrEmpty(friendly) || friendly == Auto) return null;
        foreach (var (r, f) in Map)
            if (f == friendly) return r;
        return null;
    }
}
