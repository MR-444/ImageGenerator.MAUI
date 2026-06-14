namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// A named, reusable <see cref="StyleDescription"/> — the text-portable replacement for a
/// reference-image moodboard. <c>SwapStyle</c> drops one in wholesale; <c>BlendStyle</c> merges one
/// into the current style.
/// </summary>
/// <param name="Name">Stable identifier (e.g. "astral_silk_garden_reverie"), recorded in provenance.</param>
/// <param name="Style">The style block this fragment carries (a full, single-branch style_description).</param>
public sealed record StyleFragment(string Name, StyleDescription Style);
