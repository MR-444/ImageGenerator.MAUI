namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// In-memory aggregate of the user-editable mutation library: the style fragments and ornament kits
/// operators draw from. Pure data — loading/saving from the JSON folder is a later phase
/// (<c>MutationLibraryService</c>); this type is what operators and tests consume.
/// </summary>
public sealed class MutationLibrary
{
    private readonly Dictionary<string, StyleFragment> _fragmentsByName;

    public MutationLibrary(
        IReadOnlyList<StyleFragment> styleFragments,
        IReadOnlyList<OrnamentKit> ornamentKits,
        IReadOnlyList<SceneElement>? sceneElements = null,
        IReadOnlyList<AnchorPreset>? anchorPresets = null)
    {
        // Defensive copies: snapshot the caller's collections so a later mutation of the original lists
        // can't alter library state. The entries themselves are immutable records, so a shallow copy is enough.
        StyleFragments = (StyleFragment[])[.. styleFragments];
        OrnamentKits = (OrnamentKit[])[.. ornamentKits];
        SceneElements = sceneElements is null ? [] : (SceneElement[])[.. sceneElements];
        AnchorPresets = anchorPresets is null ? [] : (AnchorPreset[])[.. anchorPresets];
        _fragmentsByName = StyleFragments.ToDictionary(f => f.Name, StringComparer.Ordinal);
    }

    /// <summary>An empty library — operators that need entries return <c>null</c> against it.</summary>
    public static MutationLibrary Empty { get; } = new([], []);

    public IReadOnlyList<StyleFragment> StyleFragments { get; }

    public IReadOnlyList<OrnamentKit> OrnamentKits { get; }

    /// <summary>Placeable scene-element templates the SCENE Add / SwapElementDesc operators draw from.</summary>
    public IReadOnlyList<SceneElement> SceneElements { get; }

    /// <summary>Named steer presets that prime the AI mutation steer field (UI nicety; not used by the
    /// deterministic operators).</summary>
    public IReadOnlyList<AnchorPreset> AnchorPresets { get; }

    /// <summary>Fragments are the only entries anything looks up by name; operators iterate
    /// <see cref="OrnamentKits"/>, <see cref="SceneElements"/> and <see cref="AnchorPresets"/>.</summary>
    public StyleFragment? FragmentByName(string name) =>
        _fragmentsByName.GetValueOrDefault(name);
}
