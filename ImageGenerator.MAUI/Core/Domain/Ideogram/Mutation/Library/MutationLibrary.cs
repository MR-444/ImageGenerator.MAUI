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

    public MutationLibrary(
        IReadOnlyList<StyleFragment> styleFragments,
        IReadOnlyList<OrnamentKit> ornamentKits)
    {
        StyleFragments = styleFragments;
        OrnamentKits = ornamentKits;
        _fragmentsByName = styleFragments.ToDictionary(f => f.Name, StringComparer.Ordinal);
        _kitsByName = ornamentKits.ToDictionary(k => k.Name, StringComparer.Ordinal);
    }

    /// <summary>An empty library — operators that need entries return <c>null</c> against it.</summary>
    public static MutationLibrary Empty { get; } = new([], []);

    public IReadOnlyList<StyleFragment> StyleFragments { get; }

    public IReadOnlyList<OrnamentKit> OrnamentKits { get; }

    public StyleFragment? FragmentByName(string name) =>
        _fragmentsByName.GetValueOrDefault(name);

    public OrnamentKit? KitByName(string name) =>
        _kitsByName.GetValueOrDefault(name);
}
