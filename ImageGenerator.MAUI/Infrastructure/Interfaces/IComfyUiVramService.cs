namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Asks the user's ComfyUI server to unload models and free GPU memory (its <c>POST /free</c>) once
/// rendering is idle, so local Ollama prompt/AI work (and the OS) get the VRAM back. Best-effort: never
/// throws — freeing memory is an optimization, not part of the render outcome.
/// </summary>
public interface IComfyUiVramService
{
    /// <summary>Send <c>POST /free</c> (<c>unload_models</c> + <c>free_memory</c>) to the configured
    /// ComfyUI server. Resolves the base URL + auth header the same way the generation service does.</summary>
    Task TryFreeAsync(CancellationToken ct = default);
}
