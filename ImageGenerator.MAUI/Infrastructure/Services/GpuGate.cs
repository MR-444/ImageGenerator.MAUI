using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Default <see cref="IGpuGate"/>: a single <see cref="SemaphoreSlim"/>(1,1). Registered as a singleton so
/// every caller (ComfyUI renders, the local Ollama mutation tier) shares one permit and the local GPU runs
/// one workload at a time. Logs at Debug when a caller actually has to wait, so app.log shows the
/// serialization (a mutation queued behind an in-flight render, or vice-versa).
/// </summary>
public sealed class GpuGate : IGpuGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<GpuGate> _logger;

    public GpuGate(ILogger<GpuGate> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        if (!await _semaphore.WaitAsync(0, ct))
        {
            _logger.LogDebug("GPU gate busy — waiting for the in-flight GPU workload to finish");
            await _semaphore.WaitAsync(ct);
        }

        return new Releaser(_semaphore);
    }

    /// <summary>Releases the permit exactly once, even if disposed twice.</summary>
    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
