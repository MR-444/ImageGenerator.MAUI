namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Process-wide serialization gate for work that targets the user's single local GPU box ("fireEngine").
/// ComfyUI rendering and local Ollama prompt/AI work share the same VRAM, so loading both models at
/// once thrashes/OOMs and the Ollama call hangs to its timeout. Both paths acquire this gate so only one
/// GPU workload runs at a time. Cloud tiers and non-local providers never acquire it.
/// </summary>
public interface IGpuGate
{
    /// <summary>
    /// Wait for exclusive access to the local GPU. Await the returned token's disposal to release —
    /// <c>using var _ = await gate.AcquireAsync(ct);</c>. Honors <paramref name="ct"/> while waiting,
    /// so a queued caller can be cancelled before it ever runs.
    /// </summary>
    Task<IDisposable> AcquireAsync(CancellationToken ct = default);
}
