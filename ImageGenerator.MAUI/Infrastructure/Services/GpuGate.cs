using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Default <see cref="IGpuGate"/>: a single <see cref="SemaphoreSlim"/>(1,1). Registered as a singleton so
/// every caller (ComfyUI renders, local Ollama prompt/AI work) shares one permit and the local GPU runs
/// one workload at a time. Logs at Debug when a caller actually has to wait, so app.log shows the
/// serialization (a mutation queued behind an in-flight render, or vice-versa).
/// </summary>
public sealed class GpuGate : IGpuGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly ILogger<GpuGate> _logger;
    private int _held;

    public GpuGate(ILogger<GpuGate> logger) =>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public bool IsBusy => Volatile.Read(ref _held) != 0;

    public event EventHandler? BusyChanged;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        if (!await _semaphore.WaitAsync(0, ct))
        {
            _logger.LogDebug("GPU gate busy — waiting for the in-flight GPU workload to finish");
            await _semaphore.WaitAsync(ct);
        }

        Volatile.Write(ref _held, 1);
        BusyChanged?.Invoke(this, EventArgs.Empty);
        return new Releaser(this);
    }

    // Marks the gate free and raises the event before releasing the permit, so the next holder's
    // "busy" always comes after this "free".
    private void Release()
    {
        Volatile.Write(ref _held, 0);
        BusyChanged?.Invoke(this, EventArgs.Empty);
        _semaphore.Release();
    }

    /// <summary>Releases the permit exactly once, even if disposed twice.</summary>
    private sealed class Releaser : IDisposable
    {
        private GpuGate? _gate;

        public Releaser(GpuGate gate) => _gate = gate;

        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
