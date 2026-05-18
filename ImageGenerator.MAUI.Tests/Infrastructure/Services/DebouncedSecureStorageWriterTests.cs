using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Concurrent;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class DebouncedSecureStorageWriterTests
{
    // 50 ms keeps the test fast while still being a real (non-mocked) debounce
    // window. The waits below add comfortable slack so a slow CI runner doesn't
    // race the await.
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(50);
    private const int WaitPastDelayMs = 250;

    [Fact]
    public async Task Schedule_SingleValue_WritesAfterDebounceWindow()
    {
        var writes = new ConcurrentBag<(string Key, string Value)>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, NullLogger.Instance,
            (k, v) => { writes.Add((k, v)); return Task.CompletedTask; });

        sut.Schedule("hello");
        await Task.Delay(WaitPastDelayMs);

        writes.Should().ContainSingle()
            .Which.Should().Be(("k", "hello"));
    }

    [Fact]
    public async Task Schedule_RapidBurst_OnlyLastValueIsWritten()
    {
        // Debounce contract: a 10-keystroke paste must collapse to a single write of
        // the last value. If the cancellation chain ever breaks, this fails because
        // multiple intermediate writes will land.
        var writes = new ConcurrentBag<(string Key, string Value)>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, NullLogger.Instance,
            (k, v) => { writes.Add((k, v)); return Task.CompletedTask; });

        for (var i = 0; i < 10; i++)
        {
            sut.Schedule($"value-{i}");
        }
        await Task.Delay(WaitPastDelayMs);

        writes.Should().ContainSingle()
            .Which.Should().Be(("k", "value-9"));
    }

    [Fact]
    public async Task Schedule_EmptyString_DoesNotWrite()
    {
        // Empty value is the "user cleared the field" path; the original stores
        // explicitly returned without scheduling a write. Preserve that.
        var writes = new ConcurrentBag<(string Key, string Value)>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, NullLogger.Instance,
            (k, v) => { writes.Add((k, v)); return Task.CompletedTask; });

        sut.Schedule(string.Empty);
        await Task.Delay(WaitPastDelayMs);

        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_EmptyAfterPendingValue_CancelsPendingWrite()
    {
        // A user typing then clearing should not leave the typed value queued.
        var writes = new ConcurrentBag<(string Key, string Value)>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, NullLogger.Instance,
            (k, v) => { writes.Add((k, v)); return Task.CompletedTask; });

        sut.Schedule("typed");
        sut.Schedule(string.Empty);
        await Task.Delay(WaitPastDelayMs);

        writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Schedule_BurstFromManyThreads_NoExceptionAndOneFinalWrite()
    {
        // The race fix the lock guards against: 200 concurrent Schedule() calls
        // from a Parallel.For. Without the lock, two threads can observe a
        // disposed-but-not-yet-replaced CTS and trigger ObjectDisposedException
        // from Cancel() — which currently escapes nowhere (no outer try/catch in
        // Persist call-sites). With the lock, exactly one final task survives.
        var writes = new ConcurrentBag<(string Key, string Value)>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, NullLogger.Instance,
            (k, v) => { writes.Add((k, v)); return Task.CompletedTask; });

        var exceptions = new ConcurrentBag<Exception>();
        Parallel.For(0, 200, i =>
        {
            try { sut.Schedule($"v{i}"); }
            catch (Exception ex) { exceptions.Add(ex); }
        });
        await Task.Delay(WaitPastDelayMs);

        exceptions.Should().BeEmpty("the lock must serialize the CTS swap");
        writes.Count.Should().BeLessThanOrEqualTo(1,
            "debounce should collapse the entire burst to at most one final write");
    }

    [Fact]
    public async Task Schedule_WriterThrows_LoggerReceivesWarning()
    {
        // SecureStorage failures are non-actionable; the contract is "log + carry on".
        var logger = new Mock<ILogger>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, logger.Object,
            (_, _) => throw new InvalidOperationException("boom"));

        sut.Schedule("trigger");
        await Task.Delay(WaitPastDelayMs);

        logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.Is<Exception>(ex => ex is InvalidOperationException),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task Schedule_SupersededValue_DoesNotLogCancellation()
    {
        // OCE on the inner Task.Delay is the normal debounce path on every keystroke
        // after the first. The catch must swallow it silently — logging here would
        // flood the log with one warning per keystroke.
        var logger = new Mock<ILogger>();
        var sut = new DebouncedSecureStorageWriter(
            "k", DebounceDelay, logger.Object,
            (_, _) => Task.CompletedTask);

        sut.Schedule("first");
        sut.Schedule("second");
        await Task.Delay(WaitPastDelayMs);

        logger.Verify(
            l => l.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Never);
    }
}
