namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Posts a saved image to CivitAI as a PUBLISHED post (one-step, no manual publish on the
/// site), optionally attaching structured generation metadata. Uses CivitAI's MCP server
/// for the upload + whoami and the underlying tRPC endpoint for post creation (the only
/// surface that accepts meta).
/// Both are unversioned/internal-ish (MCP self-reports v0.1.0), so implementations must
/// fail soft: server-side failures come back as an unsuccessful result with a user-facing
/// message, never as an exception.
/// </summary>
public interface ICivitaiPostingService
{
    /// <summary>
    /// Uploads the file at <paramref name="filePath"/> byte-identical (no re-encode, no
    /// metadata injection) and creates + publishes a post around it. An empty
    /// <paramref name="title"/> is omitted from the post. <paramref name="meta"/>,
    /// when non-null, is sent as the image's structured generation metadata (prompt, seed,
    /// sampler, …) — CivitAI never parses metadata out of API-uploaded files, so this is
    /// the only way generation data reaches the post. <paramref name="modelVersionId"/>,
    /// when non-null, associates the post (and its image) with that CivitAI model version,
    /// landing it in the model's gallery.
    /// </summary>
    Task<CivitaiPostResult> PostImageAsync(
        string filePath,
        string title,
        IReadOnlyDictionary<string, object>? meta,
        int? modelVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads every image in <paramref name="images"/> byte-identical and creates ONE post
    /// containing all of them (a single CivitAI gallery card, swipeable) — used by the
    /// Gallery's batch posting. Each image carries its own optional <see cref="CivitaiImagePost.Meta"/>.
    /// <paramref name="publish"/> false creates a DRAFT (the Gallery flow, so the user reviews
    /// and publishes on the site); true publishes immediately. Same fail-soft contract as
    /// <see cref="PostImageAsync"/>: server-side failures return an unsuccessful result.
    /// </summary>
    Task<CivitaiPostResult> PostImagesAsync(
        IReadOnlyList<CivitaiImagePost> images,
        string title,
        int? modelVersionId,
        bool publish,
        CancellationToken cancellationToken = default);

    /// <summary>whoami — validates the stored API key for the Settings "Test connection" button.</summary>
    Task<CivitaiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>One image in a multi-image post: its file path and optional structured meta.</summary>
public sealed record CivitaiImagePost(string FilePath, IReadOnlyDictionary<string, object>? Meta);

public sealed record CivitaiPostResult(bool Success, int? PostId, string? PostUrl, string Message);

public sealed record CivitaiConnectionResult(bool Success, string Message);
