namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>Lists LoRA file names exposed by the configured ComfyUI host.</summary>
public interface IComfyUiLoraService
{
    /// <summary>
    /// Returns the exact relative paths from ComfyUI's <c>GET /models/loras</c> endpoint,
    /// sorted for display. Throws an actionable exception when the host cannot be queried.
    /// </summary>
    Task<IReadOnlyList<string>> GetLoraNamesAsync(CancellationToken ct = default);
}
