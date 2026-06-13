using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Core.Domain.Services;

/// <summary>
/// Maps a generation-parameters snapshot to the loose meta object CivitAI's
/// post.createWithImages accepts per image (imageMetaSchema). Core keys (prompt, seed,
/// sampler) drive the site's generation-data panel; CivitAI's own gpt-image generations
/// put the model name in `sampler`, so we mirror that. Extra keys render under
/// "Other metadata". Pure string/dictionary math — no HTTP — so it belongs in Domain.
/// </summary>
public static class CivitaiMetaBuilder
{
    public static IReadOnlyDictionary<string, object> Build(ImageGenerationParameters parameters)
    {
        var modelShort = parameters.Model.Contains('/')
            ? parameters.Model[(parameters.Model.LastIndexOf('/') + 1)..]
            : parameters.Model;

        var meta = new Dictionary<string, object>
        {
            ["prompt"] = parameters.Prompt,
            ["seed"] = parameters.Seed,
            ["sampler"] = modelShort,
            ["Model"] = parameters.Model,
        };

        if (!string.IsNullOrWhiteSpace(parameters.AspectRatio))
            meta["Aspect ratio"] = parameters.AspectRatio;

        return meta;
    }

    /// <summary>
    /// Builds the same CivitAI meta object from the key/value block embedded in a saved image
    /// (the "Prompt: …", "ModelName: …", "Seed: …", "AspectRatio: …" lines written by
    /// ImageFileService and read back by GalleryService.ReadMetadataAsync) — the Gallery batch
    /// path, where no live ImageGenerationParameters exists. Returns null when there's no usable
    /// prompt, so a bare image posts without an empty meta object.
    /// </summary>
    public static IReadOnlyDictionary<string, object>? BuildFromFileMetadata(
        IReadOnlyDictionary<string, string>? fileMeta)
    {
        if (fileMeta is null) return null;

        var prompt = Get(fileMeta, "Prompt");
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        var meta = new Dictionary<string, object> { ["prompt"] = prompt };

        if (Get(fileMeta, "Seed") is { } seedText && !string.IsNullOrWhiteSpace(seedText))
            meta["seed"] = long.TryParse(seedText, out var seed) ? seed : seedText;

        if (Get(fileMeta, "ModelName") is { } model && !string.IsNullOrWhiteSpace(model))
        {
            meta["Model"] = model;
            meta["sampler"] = model.Contains('/') ? model[(model.LastIndexOf('/') + 1)..] : model;
        }

        if (Get(fileMeta, "AspectRatio") is { } aspect && !string.IsNullOrWhiteSpace(aspect))
            meta["Aspect ratio"] = aspect;

        return meta;
    }

    // ReadMetadataAsync builds the dictionary with StringComparer.Ordinal, so keys are
    // case-sensitive — match the exact casing ImageFileService writes.
    private static string? Get(IReadOnlyDictionary<string, string> meta, string key)
        => meta.TryGetValue(key, out var value) ? value : null;
}
