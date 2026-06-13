using System.Text.Json;

namespace ImageGenerator.MAUI.Core.Domain.Services;

/// <summary>
/// Derives a short CivitAI post title from a generation prompt. Plain prompts are collapsed and
/// trimmed to ~60 chars; structured JSON prompts (Ideogram4/ComfyUI) use their description field
/// instead of leaking the raw brace blob. Empty result means "no usable title" — callers omit it
/// rather than send "". Pure string math, shared by generation-time and Gallery posting.
/// </summary>
public static class CivitaiTitleBuilder
{
    public static string Build(string prompt)
    {
        var text = prompt.TrimStart().StartsWith('{') ? ExtractJsonDescription(prompt) : prompt;

        const int maxLength = 60;
        var collapsed = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length <= maxLength) return collapsed;

        var cut = collapsed.LastIndexOf(' ', maxLength);
        return collapsed[..(cut > 0 ? cut : maxLength)] + "…";
    }

    private static string? ExtractJsonDescription(string jsonPrompt)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonPrompt);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            // Priority order matches the prompt schemas in use: the Ideogram4 workflows carry
            // high_level_description; the rest are generic fallbacks for other JSON shapes.
            foreach (var key in (string[])["high_level_description", "description", "caption", "prompt", "title"])
            {
                if (doc.RootElement.TryGetProperty(key, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not valid JSON after all — no title is better than a brace blob.
        }
        return null;
    }
}
