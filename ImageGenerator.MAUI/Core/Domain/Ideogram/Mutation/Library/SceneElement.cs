namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// A named, placeable scene-element template — the SCENE-axis analogue of <see cref="StyleFragment"/>.
/// The AddElement operator draws one to insert a new element; the SwapElementDesc operator uses templates
/// sharing a slot tag as alternate descriptions. Pure data; the loading service is a later phase.
/// </summary>
/// <param name="Name">Stable identifier, recorded in provenance.</param>
/// <param name="Type">Element type to emit (<c>obj</c> or <c>text</c>).</param>
/// <param name="SlotTag">Semantic slot this template targets (e.g. <c>scene.flora</c>, <c>prop.charms</c>).</param>
/// <param name="Desc">The element description (≤60 words, identity-first, schema-clean).</param>
/// <param name="Text">Literal text to render when <paramref name="Type"/> is <c>text</c>; otherwise null.</param>
/// <param name="ColorPalette">Optional per-element palette (≤5 uppercase #RRGGBB).</param>
/// <param name="PreferredBbox">Suggested [y_min,x_min,y_max,x_max] placement on the 0–1000 grid; null = unplaced.</param>
public sealed record SceneElement(
    string Name,
    string Type,
    string SlotTag,
    string Desc,
    string? Text,
    IReadOnlyList<string>? ColorPalette,
    int[]? PreferredBbox);
