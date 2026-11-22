using System.Text.Json;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Services;

/// <summary>
/// The rules for whether a set of parameters can start a run: the Generate button, its
/// click-time message and the batch runner all ask here, so they cannot disagree.
/// </summary>
public static class GenerationReadiness
{
    /// <summary>Pollinations works anonymously and ComfyUI is the user's own server; Replicate needs a token.</summary>
    public static bool IsTokenless(string? model) =>
        ModelConstants.Pollinations.IsId(model) || ModelConstants.ComfyUi.IsId(model);

    public static bool HasRequiredToken(ImageGenerationParameters parameters) =>
        IsTokenless(parameters.Model) || !string.IsNullOrWhiteSpace(parameters.ApiToken);

    /// <summary>The user-facing names of the required fields that are empty, in display order.</summary>
    public static IReadOnlyList<string> MissingFields(ImageGenerationParameters parameters)
    {
        var missing = new List<string>(2);
        if (!HasRequiredToken(parameters)) missing.Add("API Token");
        if (string.IsNullOrWhiteSpace(parameters.Prompt)) missing.Add("Prompt");
        return missing;
    }

    /// <summary>Structured-JSON mode needs both the toggle and a model with a JSON prompt field.</summary>
    public static bool IsJsonModeActive(ImageGenerationParameters parameters, bool modelHasJsonPrompt) =>
        modelHasJsonPrompt && parameters.UseJsonPrompt;

    /// <summary>
    /// Null for valid JSON, else a short reason with line and position, so the user can find the
    /// break in a long single-line blob.
    /// </summary>
    public static string? JsonErrorDetail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "prompt is empty";
        try
        {
            JsonDocument.Parse(text).Dispose();
            return null;
        }
        catch (JsonException ex)
        {
            // Drop the " LineNumber: … | BytePositionInLine: …" tail; the suffix below says it better.
            var msg = ex.Message;
            var cut = msg.IndexOf(" LineNumber:", StringComparison.Ordinal);
            if (cut > 0) msg = msg[..cut].TrimEnd();
            return ex is { LineNumber: { } line, BytePositionInLine: { } pos }
                ? $"{msg} (line {line + 1}, pos {pos})"
                : msg;
        }
    }
}
