using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Tests.TestSupport;

/// <summary>
/// An <see cref="IGpuGate"/> that never waits: every acquire succeeds at once and is counted.
/// <see cref="IsBusy"/> and <see cref="BusyChanged"/> behave like the real gate's, so view models
/// that show "GPU busy" can be driven from a test.
/// </summary>
internal sealed class FakeGpuGate : IGpuGate
{
    public int Acquired { get; private set; }
    public int Released { get; private set; }

    public bool IsBusy => Acquired > Released;

    public event EventHandler? BusyChanged;

    public Task<IDisposable> AcquireAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Acquired++;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<IDisposable>(new Lease(this));
    }

    private sealed class Lease(FakeGpuGate gate) : IDisposable
    {
        private FakeGpuGate? _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _gate, null) is not { } g) return;
            g.Released++;
            g.BusyChanged?.Invoke(g, EventArgs.Empty);
        }
    }
}
