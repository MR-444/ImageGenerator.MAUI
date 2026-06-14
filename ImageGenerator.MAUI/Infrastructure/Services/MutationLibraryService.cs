using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// File-backed <see cref="IMutationLibraryService"/>. The library is three JSON arrays on disk —
/// <c>style-fragments.json</c>, <c>ornament-kits.json</c>, <c>scene-elements.json</c> — each a list of the
/// matching domain record. Missing stores are seeded once from the bundled <c>Resources/Raw/MutationDefaults</c>
/// copies, so a fresh install loads a working library; thereafter the files are the user's to hand-edit.
/// A corrupt store degrades to empty (logged) so it never sinks the whole load.
/// </summary>
public sealed class MutationLibraryService : IMutationLibraryService
{
    private const string StyleFragmentsFile = "style-fragments.json";
    private const string OrnamentKitsFile = "ornament-kits.json";
    private const string SceneElementsFile = "scene-elements.json";
    private const string DefaultsAssetFolder = "MutationDefaults";   // logical name under Resources/Raw

    // Web defaults give camelCase wrapper keys + case-insensitive reads; the relaxed encoder keeps non-ASCII
    // and '#' literal for hand-editing, and string enum names make DescBudgetCategory readable on disk.
    private static readonly JsonSerializerOptions LibraryJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,    // omit the unused style branch, empty text, etc.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<MutationLibraryService> _logger;
    private readonly Func<string, Task<Stream>> _assetOpener;

    public MutationLibraryService(
        ILogger<MutationLibraryService> logger,
        string? libraryDirectoryOverride = null,
        Func<string, Task<Stream>>? assetOpener = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        LibraryDirectory = string.IsNullOrWhiteSpace(libraryDirectoryOverride)
            ? OutputPaths.MutationLibraryDirectory
            : libraryDirectoryOverride;
        _assetOpener = assetOpener ?? FileSystem.OpenAppPackageFileAsync;
    }

    public string LibraryDirectory { get; }

    public async Task<MutationLibrary> LoadAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(LibraryDirectory);

        await SeedIfMissingAsync(StyleFragmentsFile, ct);
        await SeedIfMissingAsync(OrnamentKitsFile, ct);
        await SeedIfMissingAsync(SceneElementsFile, ct);

        var fragments = await ReadStoreAsync<StyleFragment>(StyleFragmentsFile, ct);
        var kits = await ReadStoreAsync<OrnamentKit>(OrnamentKitsFile, ct);
        var elements = await ReadStoreAsync<SceneElement>(SceneElementsFile, ct);

        return new MutationLibrary(fragments, kits, elements);
    }

    public async Task SaveAsync(MutationLibrary library, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(library);
        Directory.CreateDirectory(LibraryDirectory);

        await WriteStoreAsync(StyleFragmentsFile, library.StyleFragments, ct);
        await WriteStoreAsync(OrnamentKitsFile, library.OrnamentKits, ct);
        await WriteStoreAsync(SceneElementsFile, library.SceneElements, ct);
    }

    // Copies the bundled default for a store into the library folder when the user has none yet. A missing
    // bundled asset is logged and skipped — the store then simply loads empty.
    private async Task SeedIfMissingAsync(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(LibraryDirectory, fileName);
        if (File.Exists(path))
            return;

        try
        {
            await using var source = await _assetOpener($"{DefaultsAssetFolder}/{fileName}");
            await using var destination = File.Create(path);
            await source.CopyToAsync(destination, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mutation library seed skipped File={File}", fileName);
        }
    }

    private async Task<List<T>> ReadStoreAsync<T>(string fileName, CancellationToken ct)
    {
        var path = Path.Combine(LibraryDirectory, fileName);
        if (!File.Exists(path))
            return [];

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, LibraryJson, ct) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mutation library store unreadable, treating as empty File={File}", fileName);
            return [];
        }
    }

    private async Task WriteStoreAsync<T>(string fileName, IReadOnlyList<T> items, CancellationToken ct)
    {
        var path = Path.Combine(LibraryDirectory, fileName);
        var tempPath = path + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, items, LibraryJson, ct);
        }

        // File.Move(overwrite: true) is atomic on NTFS — same convention as ComfyUiCheckpointService.
        File.Move(tempPath, path, overwrite: true);
    }
}
