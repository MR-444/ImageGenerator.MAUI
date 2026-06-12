using System.Text.RegularExpressions;

namespace ImageGenerator.MAUI.Core.Domain.Services;

/// <summary>
/// Extracts a CivitAI model VERSION id from whatever the user pastes: the model page URL or
/// the "create post for this model" URL (both carry <c>modelVersionId=NNN</c> as a query
/// parameter), or the bare number itself. The version id — not the model id — is what
/// post.createWithImages needs to land a post in the model's gallery. Pure string math.
/// </summary>
public static partial class CivitaiModelReference
{
    [GeneratedRegex(@"modelVersionId=(\d+)")]
    private static partial Regex VersionIdParam();

    /// <summary>Null when the text is empty or carries no recognizable version id.</summary>
    public static int? ParseVersionId(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();

        var match = VersionIdParam().Match(trimmed);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var fromUrl))
            return fromUrl;

        return int.TryParse(trimmed, out var bare) ? bare : null;
    }
}
