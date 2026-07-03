namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Lists the models installed on a local Ollama server, for model pickers in Settings and tools.</summary>
public interface IOllamaModelCatalog
{
    /// <summary>
    /// The model tags installed on the Ollama server at <paramref name="baseUrl"/> (its <c>/api/tags</c>),
    /// e.g. "qwen2.5", "qwen3.5:27b". Throws on an unreachable server or a non-2xx response so the caller
    /// can surface why the list is empty.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Rich model tags from <c>/api/tags</c>, including Ollama's advertised capabilities such as
    /// <c>completion</c>, <c>vision</c>, <c>tools</c>, and <c>thinking</c>. Callers can filter without
    /// cold-loading every model.
    /// </summary>
    Task<IReadOnlyList<OllamaModelInfo>> ListModelInfosAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>
    /// Asks the Ollama server to unload <paramref name="model"/> from (V)RAM immediately — a
    /// <c>POST /api/generate</c> with <c>keep_alive: 0</c>. Best-effort: never throws, so a caller can fire
    /// it after a Local-tier mutation batch to free the GPU for the imminent ComfyUI render.
    /// </summary>
    Task UnloadAsync(string baseUrl, string model, CancellationToken ct = default);
}

public sealed record OllamaModelInfo(
    string Name,
    IReadOnlyList<string> Capabilities)
{
    public bool SupportsCompletion => Capabilities.Any(c => string.Equals(c, "completion", StringComparison.OrdinalIgnoreCase));
    public bool SupportsVision => Capabilities.Any(c => string.Equals(c, "vision", StringComparison.OrdinalIgnoreCase));
}
