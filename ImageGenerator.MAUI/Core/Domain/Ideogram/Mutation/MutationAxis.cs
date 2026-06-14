namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;

/// <summary>
/// The two orthogonal mutation axes. A run pins one axis and mutates the other so any visible
/// difference between a variant and the base is attributable to a single cause.
/// <list type="bullet">
/// <item><see cref="Look"/> — style + ornament (style_description, palettes, element ornament phrases).</item>
/// <item><see cref="Scene"/> — background + composition (elements, bboxes, per-element desc/palette).</item>
/// </list>
/// </summary>
public enum MutationAxis
{
    Look,
    Scene
}
