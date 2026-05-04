using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;

namespace ImageGenerator.MAUI.Tests.Diagnostics;

// CrashLogger is static and process-wide, so the tests share a single root dir per-class
// fixture: parallel test runs across xUnit collections would otherwise race the file. We
// install once with a temp dir and read crash.log between tests via simple substring asserts.
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
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* swallow on Windows file-lock races */ }
        }
    }

    [Fact]
    public void Install_WritesStartupOkLine()
    {
        // Install ran in the ctor; the file must already contain the marker so an empty
        // crash.log on a real run unambiguously means "Install never ran".
        File.Exists(_logPath).Should().BeTrue();
        var content = File.ReadAllText(_logPath);
        content.Should().Contain("startup OK");
        content.Should().Contain($"pid={Environment.ProcessId}");
    }

    [Fact]
    public void Log_WritesSourceAndExceptionMessage()
    {
        CrashLogger.Log("TestSource", new InvalidOperationException("hello-from-test"));

        var content = File.ReadAllText(_logPath);
        content.Should().Contain("TestSource");
        content.Should().Contain("hello-from-test");
        content.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task Log_FromFourThreadsConcurrently_AllMessagesLand()
    {
        // Regression: without the WriteGate lock, two threads racing File.AppendAllText can
        // interleave bytes or drop a write under contention. The lock is cheap because the
        // gallery doesn't log on hot paths.
        var tasks = Enumerable.Range(0, 4)
            .Select(i => Task.Run(() => CrashLogger.Log($"Race{i}", new Exception($"msg-{i}"))))
            .ToArray();
        await Task.WhenAll(tasks);

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
