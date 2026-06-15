namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Lists the models installed on a local Ollama server, for the model picker in Settings.</summary>
public interface IOllamaModelCatalog
{
    /// <summary>
    /// The model tags installed on the Ollama server at <paramref name="baseUrl"/> (its <c>/api/tags</c>),
    /// e.g. "qwen2.5", "qwen3.5:27b". Throws on an unreachable server or a non-2xx response so the caller
    /// can surface why the list is empty.
    /// </summary>
    Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, CancellationToken ct = default);
}
