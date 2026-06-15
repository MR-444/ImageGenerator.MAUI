namespace ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

/// <summary>
/// In-memory aggregate of the user-editable mutation library: the style fragments and ornament kits
/// operators draw from. Pure data — loading/saving from the JSON folder is a later phase
/// (<c>MutationLibraryService</c>); this type is what operators and tests consume.
/// </summary>
public sealed class MutationLibrary
{
    private readonly Dictionary<string, StyleFragment> _fragmentsByName;
    private readonly Dictionary<string, OrnamentKit> _kitsByName;
    private readonly Dictionary<string, SceneElement> _sceneElementsByName;
    private readonly Dictionary<string, AnchorPreset> _presetsByName;

    public MutationLibrary(
        IReadOnlyList<StyleFragment> styleFragments,
        IReadOnlyList<OrnamentKit> ornamentKits,
        IReadOnlyList<SceneElement>? sceneElements = null,
        IReadOnlyList<AnchorPreset>? anchorPresets = null)
    {
        StyleFragments = styleFragments;
        OrnamentKits = ornamentKits;
        SceneElements = sceneElements ?? [];
        AnchorPresets = anchorPresets ?? [];
        _fragmentsByName = styleFragments.ToDictionary(f => f.Name, StringComparer.Ordinal);
        _kitsByName = ornamentKits.ToDictionary(k => k.Name, StringComparer.Ordinal);
        _sceneElementsByName = SceneElements.ToDictionary(e => e.Name, StringComparer.Ordinal);
        _presetsByName = AnchorPresets.ToDictionary(p => p.Name, StringComparer.Ordinal);
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

    public StyleFragment? FragmentByName(string name) =>
        _fragmentsByName.GetValueOrDefault(name);

    public OrnamentKit? KitByName(string name) =>
        _kitsByName.GetValueOrDefault(name);

    public SceneElement? SceneElementByName(string name) =>
        _sceneElementsByName.GetValueOrDefault(name);

    public AnchorPreset? PresetByName(string name) =>
        _presetsByName.GetValueOrDefault(name);
}
