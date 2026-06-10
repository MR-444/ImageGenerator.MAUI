using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGenerator.MAUI.Infrastructure.External.ComfyUi;

// Wire shapes for ComfyUI's built-in HTTP API (the same one its web UI uses).
// Drift-prone fields stay JsonElement so schema changes degrade to readable raw JSON in the
// job message instead of deserialization crashes.

/// <summary>POST /prompt success body.</summary>
public sealed class ComfyUiPromptResponse
{
    [JsonPropertyName("prompt_id")] public string? PromptId { get; set; }
    [JsonPropertyName("node_errors")] public JsonElement? NodeErrors { get; set; }
}

/// <summary>POST /prompt HTTP 400 body — node-level validation errors.</summary>
public sealed class ComfyUiErrorEnvelope
{
    [JsonPropertyName("error")] public ComfyUiErrorDetail? Error { get; set; }
    [JsonPropertyName("node_errors")] public JsonElement? NodeErrors { get; set; }
}

public sealed class ComfyUiErrorDetail
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("details")] public string? Details { get; set; }
}

/// <summary>
/// GET /history/{prompt_id} deserializes as Dictionary&lt;string, ComfyUiHistoryEntry&gt; —
/// empty until the job finished, then keyed by the prompt id.
/// </summary>
public sealed class ComfyUiHistoryEntry
{
    [JsonPropertyName("outputs")] public Dictionary<string, ComfyUiNodeOutput>? Outputs { get; set; }
    [JsonPropertyName("status")] public ComfyUiHistoryStatus? Status { get; set; }
}

public sealed class ComfyUiNodeOutput
{
    [JsonPropertyName("images")] public List<ComfyUiImageRef>? Images { get; set; }
}

public sealed class ComfyUiImageRef
{
    [JsonPropertyName("filename")] public string? Filename { get; set; }
    [JsonPropertyName("subfolder")] public string? Subfolder { get; set; }
    /// <summary>"output" (SaveImage) or "temp" (PreviewImage).</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
}

public sealed class ComfyUiHistoryStatus
{
    [JsonPropertyName("status_str")] public string? StatusStr { get; set; }
    [JsonPropertyName("completed")] public bool? Completed { get; set; }
    [JsonPropertyName("messages")] public JsonElement? Messages { get; set; }
}
