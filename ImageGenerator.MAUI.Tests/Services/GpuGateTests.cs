using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services;

/// <summary>
/// The single-permit GPU gate that serializes ComfyUI renders against the local Ollama mutation tier
/// (they share fireEngine's VRAM). These pin the primitive: only one holder at a time, cancellable waits,
/// and a release that can't leak or double-count a permit.
/// </summary>
public class GpuGateTests
{
    private static GpuGate NewGate() => new(NullLogger<GpuGate>.Instance);

    [Fact]
    public async Task AcquireAsync_SecondCaller_WaitsUntilFirstReleases()
    {
        var gate = NewGate();
        var first = await gate.AcquireAsync();

        var second = gate.AcquireAsync();
        second.IsCompleted.Should().BeFalse("the single permit is held by the first lease");

        first.Dispose();

        var secondLease = await second; // released ⇒ the waiter now completes
        secondLease.Dispose();
    }

    [Fact]
    public async Task AcquireAsync_HonorsCancellation_WhileWaiting()
    {
        var gate = NewGate();
        using var _ = await gate.AcquireAsync();

        using var cts = new CancellationTokenSource();
        var waiting = gate.AcquireAsync(cts.Token);
        cts.Cancel();

        await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IsBusy_IsSetWhileHeld_AndEachChangeIsAnnounced()
    {
        var gate = NewGate();
        var changes = 0;
        gate.BusyChanged += (_, _) => changes++;

        gate.IsBusy.Should().BeFalse();
        var lease = await gate.AcquireAsync();
        gate.IsBusy.Should().BeTrue();

        lease.Dispose();
        lease.Dispose(); // the second dispose releases nothing and announces nothing
        gate.IsBusy.Should().BeFalse();
        changes.Should().Be(2);
    }

    [Fact]
    public async Task Release_AnnouncesFreeBeforeTheNextHolderAnnouncesBusy()
    {
        var gate = NewGate();
        var first = await gate.AcquireAsync();
        var seen = new List<bool>();
        gate.BusyChanged += (_, _) => { lock (seen) seen.Add(gate.IsBusy); };

        var second = gate.AcquireAsync();
        first.Dispose();
        (await second).Dispose();

        seen.Should().Equal([false, true, false],
            "a stale \"free\" after the next holder's \"busy\" would show the GPU idle while it renders");
    }

    [Fact]
    public async Task Release_IsIdempotent_DoubleDisposeDoesNotLeakAPermit()
    {
        var gate = NewGate();
        var lease = await gate.AcquireAsync();
        lease.Dispose();
        lease.Dispose(); // must release exactly once, not bump the permit count to 2

        var a = await gate.AcquireAsync();
        var b = gate.AcquireAsync();
        b.IsCompleted.Should().BeFalse("double-dispose must not have leaked a second permit");

        a.Dispose();
        (await b).Dispose();
    }
}
