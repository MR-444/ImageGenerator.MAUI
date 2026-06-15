namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// A named, reusable steer direction for the AI mutation path. Picking one on the Mutation page fills
/// the free-text steer with a starting point the user can then edit — common directions ("make it
/// winter", "1970s film look") are one pick away. Bundled defaults seed a fresh install; thereafter
/// the JSON store is the user's to hand-edit, exactly like the style/ornament/scene stores.
/// </summary>
/// <param name="Name">Short label shown in the picker (e.g. "winter", "film_1970s").</param>
/// <param name="Steer">The steer text dropped into the field when this preset is chosen.</param>
/// <param name="Description">Optional one-line hint (not shown in the picker; for the JSON reader).</param>
public sealed record AnchorPreset(string Name, string Steer, string? Description = null);
