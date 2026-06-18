namespace ImageGenerator.MAUI.Shared.Constants;

/// <summary>
/// Decides whether the ComfyUI server and the local Ollama tier sit on the same physical box, so the GPU
/// gate only serializes when they actually share VRAM. A null/blank URL falls back to that subsystem's
/// configured default (both default to the user's fireEngine box). Host comparison is case-insensitive —
/// the persisted values differ in case in practice (e.g. "fireEngine" vs the "fireengine" default).
/// </summary>
public static class GpuColocation
{
    /// <summary>True when both URLs resolve to the same host. Unparseable URLs are treated as "not the
    /// same host" so an odd value never forces gating that would otherwise hang on a dead box.</summary>
    public static bool SameHost(string? comfyUrl, string? ollamaUrl)
    {
        var comfy = comfyUrl is { Length: > 0 } ? comfyUrl : ModelConstants.ComfyUi.DefaultBaseUrl;
        var ollama = ollamaUrl is { Length: > 0 } ? ollamaUrl : ModelConstants.Ollama.DefaultBaseUrl;

        if (!Uri.TryCreate(comfy, UriKind.Absolute, out var comfyUri)
            || !Uri.TryCreate(ollama, UriKind.Absolute, out var ollamaUri))
        {
            return false;
        }

        return string.Equals(comfyUri.Host, ollamaUri.Host, StringComparison.OrdinalIgnoreCase);
    }
}
