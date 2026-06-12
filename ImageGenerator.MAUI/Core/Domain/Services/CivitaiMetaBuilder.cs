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
}
