using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using NLog;

namespace ImageGenerator.MAUI.Tests.Diagnostics;

// CrashLogger is static and process-wide, so the tests share a single root dir per-class
// fixture: parallel test runs across xUnit collections would otherwise race the file. We
// install once with a temp dir and read app.log between tests via simple substring asserts.
[Collection(nameof(CrashLoggerCollection))]
public class CrashLoggerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _logPath;

    public CrashLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imggen-crashlog-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        CrashLogger.Install(_tempDir);
        _logPath = Path.Combine(_tempDir, "app.log");
    }

    public void Dispose()
    {
        // Flush + drop the NLog target so its file handle (if any) releases before we
        // try to delete the temp dir, otherwise Windows holds the lock and the cleanup
        // catch below swallows a "file in use" error.
        try { LogManager.Flush(TimeSpan.FromSeconds(1)); } catch { /* best-effort */ }

        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* swallow on Windows file-lock races */ }
        }
    }

    [Fact]
    public void Install_WritesStartupOkLine()
    {
        // Install ran in the ctor; the file must already contain the marker so an empty
        // app.log on a real run unambiguously means "Install never ran".
        LogManager.Flush(TimeSpan.FromSeconds(1));
        File.Exists(_logPath).Should().BeTrue();
        var content = File.ReadAllText(_logPath);
        content.Should().Contain("startup OK");
        content.Should().Contain($"pid={Environment.ProcessId}");
    }

    [Fact]
    public void WriteShutdownLine_WritesShutdownMarkerWithPid()
    {
        // Pairs with "startup OK" so app.log shows which instances were alive when —
        // multiple app processes share one physical log file.
        CrashLogger.WriteShutdownLine();

        // WriteShutdownLine calls LogManager.Shutdown() (flushes targets), so the file is
        // complete; the next test's ctor re-Installs NLog, which is documented as safe.
        var content = File.ReadAllText(_logPath);
        content.Should().Contain("shutdown");
        content.Should().Contain($"pid={Environment.ProcessId}");
    }

    [Fact]
    public void HttpClientAndPollyInfoChatter_IsSuppressed_WarningsStillLand()
    {
        // A ComfyUI run polls every 2 s and each poll emits 5-6 INFO lines from the
        // HttpClient pipeline + Polly (~900 lines/run). The blackhole rules drop them
        // below Warning; warnings and errors must still reach the file.
        LogManager.GetLogger("System.Net.Http.HttpClient.comfyui.LogicalHandler")
            .Info("poll-noise-info");
        LogManager.GetLogger("Polly").Info("polly-noise-info");
        LogManager.GetLogger("System.Net.Http.HttpClient.comfyui.LogicalHandler")
            .Warn("http-warning-survives");
        LogManager.GetLogger("Polly").Warn("polly-warning-survives");

        LogManager.Flush(TimeSpan.FromSeconds(1));
        var content = File.ReadAllText(_logPath);
        content.Should().NotContain("poll-noise-info");
        content.Should().NotContain("polly-noise-info");
        content.Should().Contain("http-warning-survives");
        content.Should().Contain("polly-warning-survives");
    }

    [Fact]
    public void Log_WritesSourceAndExceptionMessage()
    {
        CrashLogger.Log("TestSource", new InvalidOperationException("hello-from-test"));

        LogManager.Flush(TimeSpan.FromSeconds(1));
        var content = File.ReadAllText(_logPath);
        content.Should().Contain("TestSource");
        content.Should().Contain("hello-from-test");
        content.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Log_FromFourThreadsConcurrently_AllMessagesLand()
    {
        // Regression: NLog's FileTarget with ConcurrentWrites=true + KeepFileOpen=false
        // serialises concurrent appends so bytes don't interleave and writes don't drop.
        var tasks = Enumerable.Range(0, 4)
            .Select(i => Task.Run(() => CrashLogger.Log($"Race{i}", new Exception($"msg-{i}"))))
            .ToArray();
        await Task.WhenAll(tasks);

        LogManager.Flush(TimeSpan.FromSeconds(1));
        var content = File.ReadAllText(_logPath);
        for (var i = 0; i < 4; i++)
        {
            content.Should().Contain($"Race{i}", $"thread {i}'s source line should be present");
            content.Should().Contain($"msg-{i}", $"thread {i}'s exception message should be present");
        }
    }
}

[CollectionDefinition(nameof(CrashLoggerCollection), DisableParallelization = true)]
public class CrashLoggerCollection { }
