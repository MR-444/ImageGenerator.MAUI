using System.Text;
using System.Text.Json;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate;

/// <summary>
/// Turns Replicate's input-validation error body into one readable sentence.
///
/// Why this exists: a rejected payload arrives as HTTP 422 with the field name sitting inside a
/// JSON body, and <c>ReplicateImageGenerationService</c> used to surface that body verbatim in the
/// status label — the information was present and unreadable at the same time. For a model the app
/// has no descriptor for (Phase 3), this message is the whole diagnosis, so it has to read like
/// one.
///
/// Two shapes occur, both handled:
/// <code>
/// {"detail":"- input.aspect_ratio: aspect_ratio must be one of the following: \"1:1\", …\n",
///  "status":422,"title":"Input validation failed",
///  "invalid_fields":[{"type":"enum","field":"input.aspect_ratio","description":"…"}]}
/// </code>
/// (the model-proxy shape; <c>detail</c> is a STRING here, and it repeats <c>invalid_fields</c>) and
/// the FastAPI/validation-list shape
/// <c>{"detail":[{"loc":["body","input","aspect_ratio"],"msg":"…","type":"value_error.enum"}]}</c>.
///
/// Deliberately tolerant of everything else: anything unrecognised returns false and the caller
/// keeps its existing raw-body behaviour. This class may make a message shorter; it must never be
/// able to lose one.
/// </summary>
public static class ReplicateValidationErrors
{
    // A status label is one or two lines. Replicate happily returns a full enum listing per field,
    // which is the useful part — so the cap is generous, and the raw body still reaches the log.
    private const int MaxSurfacedLength = 400;

    /// <summary>One entry of either body shape. <see cref="Description"/> is the part that names
    /// the accepted values; <see cref="Type"/> ("enum", "integer") is a last-resort label.</summary>
    private readonly record struct InvalidField(string? Field, string? Description, string? Type);

    /// <summary>
    /// Returns false (and leaves <paramref name="message"/> null) for anything that isn't a
    /// validation body this class understands: empty, non-JSON, JSON without field information.
    /// </summary>
    public static bool TrySummarize(string? body, out string? message)
    {
        message = null;
        var trimmed = body?.TrimStart();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed[0] != '{') return false;

        // A body that isn't JSON is a server-proxy failure page, not a validation body: report
        // "no summary" and let the caller show it raw, exactly as before this class existed.
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;

            List<InvalidField>? fields = ReadInvalidFields(root) ?? ReadDetailArray(root);
            if (fields is null or { Count: 0 }) return false;

            var title = ReadString(root, "title");

            // `detail` is a string in the proxy shape and a list in the validation-list shape. As a
            // string it normally repeats invalid_fields, so it is used only when NO field carried a
            // description — the shape where the accepted values exist nowhere else.
            var detailSentence = NormalizeDetail(ReadString(root, "detail"), title);
            var anyDescription = fields.Any(f => f.Description is { Length: > 0 });

            var parts = fields
                .Select(f => Render(f, anyDescription ? null : detailSentence))
                .Where(s => s.Length > 0)
                .ToList();
            if (parts.Count == 0) return false;

            var text = new StringBuilder();
            if (title is { Length: > 0 }) text.Append(title).Append(": ");
            text.Append(string.Join("; ", parts));

            var surfaced = text.ToString();
            message = surfaced.Length > MaxSurfacedLength ? TruncateAtBoundary(surfaced) : surfaced;
            return message.Length > 0;
        }
    }

    /// <summary>
    /// Cuts at the last separator that still fits, so the label ends on a whole value instead of
    /// inside one. These bodies are long comma lists (must be one of the following: "1:1", ...); a
    /// label ending in 1: is not something the user can act on, and that is where a flat character
    /// cut lands.
    /// </summary>
    private static string TruncateAtBoundary(string surfaced)
    {
        // Cutting further back than this leaves nothing recognisable, which is worse than a cut
        // mid-value — the field name and the start of the list are the useful half.
        const int shortestUsefulCut = MaxSurfacedLength / 2;
        var cut = surfaced.LastIndexOfAny([',', ';', ' '], MaxSurfacedLength);
        if (cut < shortestUsefulCut) cut = MaxSurfacedLength;
        return string.Concat(surfaced[..cut].TrimEnd(' ', ',', ';'), "…");
    }

    /// <summary>Field name first, then the most specific reason available.</summary>
    private static string Render(InvalidField field, string? detailSentence)
    {
        var name = field.Field;
        var reason = field.Description ?? detailSentence ?? field.Type;

        // The detail sentence repeats the field it describes ("aspect_ratio must be one of …"
        // after "aspect_ratio — "); drop the redundant lead-in when it is there.
        if (reason is not null && name is not null
            && reason.StartsWith($"{name} ", StringComparison.Ordinal))
            reason = reason[$"{name} ".Length..];

        if (name is null) return reason ?? string.Empty;
        return reason is null ? name : $"{name} — {reason}";
    }

    /// <summary>
    /// <c>"- input.aspect_ratio: must be one of …"</c> → <c>"must be one of …"</c>: Replicate's own
    /// leading dash, its internal <c>input.</c> path marker, and the field name it repeats before
    /// the colon are all noise in front of the sentence the user needs.
    /// </summary>
    private static string? NormalizeDetail(string? detail, string? title)
    {
        if (detail is not { Length: > 0 } || detail.Equals(title, StringComparison.Ordinal)) return null;

        var sentence = detail.Trim().TrimStart('-').TrimStart();
        if (sentence.StartsWith("input.", StringComparison.Ordinal)) sentence = sentence["input.".Length..];

        var colon = sentence.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0) sentence = sentence[(colon + 2)..];
        return sentence.Length > 0 ? sentence : null;
    }

    /// <summary>The model-proxy shape: <c>invalid_fields:[{field, type, description}]</c>.</summary>
    private static List<InvalidField>? ReadInvalidFields(JsonElement root)
    {
        if (!root.TryGetProperty("invalid_fields", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<InvalidField>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var field = ShortenField(ReadString(item, "field"));
            var description = ReadString(item, "description");
            var type = ReadString(item, "type");
            if (field is null && description is null && type is null) continue;
            parts.Add(new InvalidField(field, description, type));
        }

        return parts.Count > 0 ? parts : null;
    }

    /// <summary>The validation-list shape: <c>detail:[{loc:[…], msg, type}]</c>.</summary>
    private static List<InvalidField>? ReadDetailArray(JsonElement root)
    {
        if (!root.TryGetProperty("detail", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var parts = new List<InvalidField>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var field = item.TryGetProperty("loc", out var loc) && loc.ValueKind == JsonValueKind.Array
                ? ShortenField(string.Join(".", loc.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrEmpty(s) && !s!.Equals("body", StringComparison.Ordinal))))
                : null;
            var msg = ReadString(item, "msg");
            var type = ReadString(item, "type");
            if (field is null && msg is null) continue;
            parts.Add(new InvalidField(field, msg, type));
        }

        return parts.Count > 0 ? parts : null;
    }

    /// <summary>
    /// <c>"input.aspect_ratio"</c> → <c>"aspect_ratio"</c>: the <c>input.</c> prefix is Replicate's
    /// internal path to the field, not a name the app ever shows.
    /// </summary>
    private static string? ShortenField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field)) return null;
        var text = field.Trim();
        return text.StartsWith("input.", StringComparison.Ordinal) ? text["input.".Length..] : text;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
