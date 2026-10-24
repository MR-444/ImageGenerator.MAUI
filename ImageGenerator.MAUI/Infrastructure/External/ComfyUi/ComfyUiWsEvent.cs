using System.Text.Json;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

/// <summary>
/// The subset of ComfyUI's WebSocket protocol the app reacts to. Wire shape is
/// {"type": "...", "data": {...}}; everything else (status, execution_cached, crystools
/// monitor spam, ...) parses to null and is ignored. <see cref="TryParse"/> never throws —
/// an unparseable frame is just an ignorable frame.
/// </summary>
public abstract record ComfyUiWsEvent
{
    public sealed record ExecutionStart(string? PromptId) : ComfyUiWsEvent;
    public sealed record Progress(string? PromptId, int Value, int Max) : ComfyUiWsEvent;
    public sealed record Completed(string? PromptId) : ComfyUiWsEvent;
    public sealed record Failed(string? PromptId) : ComfyUiWsEvent;

    public static ComfyUiWsEvent? TryParse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeEl)
                || typeEl.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            // prompt_id can live inside data, or on some custom servers at root level
            var promptId = (data.TryGetProperty("prompt_id", out var pid) && pid.ValueKind == JsonValueKind.String ? pid.GetString() : null)
                        ?? (root.TryGetProperty("prompt_id", out var rpid) && rpid.ValueKind == JsonValueKind.String ? rpid.GetString() : null);

            switch (typeEl.GetString())
            {
                case "execution_start":
                    return new ExecutionStart(promptId);

                case "progress":
                    return TryReadInt(data, "value", out var v) && TryReadInt(data, "max", out var m)
                        ? new Progress(promptId, v, m)
                        : null;

                case "executing":
                    // node:null is ComfyUI's original "this prompt finished" signal.
                    return data.TryGetProperty("node", out var node) && node.ValueKind == JsonValueKind.Null
                        ? new Completed(promptId)
                        : null;

                case "execution_success":
                    return new Completed(promptId);

                case "execution_error":
                case "execution_interrupted":
                    return new Failed(promptId);

                default:
                    return null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryReadInt(JsonElement parent, string propertyName, out int result)
    {
        result = 0;
        if (!parent.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (prop.TryGetInt32(out result))
        {
            return true;
        }

        // Fallback if the server serialized an integer as float/double e.g. 5.0
        if (prop.TryGetDouble(out var d))
        {
            result = (int)d;
            return true;
        }

        return false;
    }
}
