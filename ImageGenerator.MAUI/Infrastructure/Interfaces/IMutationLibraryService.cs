using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Loads and saves the user-editable mutation library — the style fragments, ornament kits, and
/// scene-element templates the caption mutation engine draws from. The library is a folder of hand-editable
/// JSON files (one store per type) under <see cref="Shared.Constants.OutputPaths.MutationLibraryDirectory"/>;
/// missing stores are seeded from bundled defaults on first load so a fresh install has a working library.
/// </summary>
public interface IMutationLibraryService
{
    /// <summary>The folder holding the JSON store files — surfaced so the UI can reveal/open it.</summary>
    string LibraryDirectory { get; }

    /// <summary>
    /// Returns the current library, seeding any missing store from bundled defaults first. A single
    /// corrupt store degrades to empty (logged) rather than failing the whole load.
    /// </summary>
    Task<MutationLibrary> LoadAsync(CancellationToken ct = default);

    /// <summary>Writes the library back to its three store files (atomic per file).</summary>
    Task SaveAsync(MutationLibrary library, CancellationToken ct = default);
}
