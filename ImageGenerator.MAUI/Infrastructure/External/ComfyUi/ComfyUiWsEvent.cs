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
    /// <summary>execution_start — the prompt left the queue and the render began.</summary>
    public sealed record ExecutionStart(string? PromptId) : ComfyUiWsEvent;

    /// <summary>progress — sampler (or other long node) step Value of Max.</summary>
    public sealed record Progress(string? PromptId, int Value, int Max) : ComfyUiWsEvent;

    /// <summary>executing with node:null (legacy completion signal) or execution_success.</summary>
    public sealed record Completed(string? PromptId) : ComfyUiWsEvent;

    /// <summary>execution_error or execution_interrupted — /history carries the detail.</summary>
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

            var promptId = data.TryGetProperty("prompt_id", out var pid) && pid.ValueKind == JsonValueKind.String
                ? pid.GetString()
                : null;

            switch (typeEl.GetString())
            {
                case "execution_start":
                    return new ExecutionStart(promptId);

                case "progress":
                    return data.TryGetProperty("value", out var value) && value.TryGetInt32(out var v)
                           && data.TryGetProperty("max", out var max) && max.TryGetInt32(out var m)
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
        catch (JsonException)
        {
            return null;
        }
    }
}
