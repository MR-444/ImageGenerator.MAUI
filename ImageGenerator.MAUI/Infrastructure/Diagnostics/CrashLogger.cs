using System.Diagnostics;
using System.Reflection;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.Diagnostics;

public static class CrashLogger
{
    private static string? _logPath;

    // File.AppendAllText opens-and-closes per call, but two threads racing can still produce
    // interleaved bytes. The lock is cheap and the only contention point — gallery doesn't
    // log on hot paths.
    private static readonly object WriteGate = new();

    // The file holds startup markers + any caught exception, so it's an app event log,
    // not just a crash dump. Keeping the class name (CrashLogger) for source-level callers
    // — its job is still "capture anything that should never have happened".
    private const string LogFileName = "app.log";

    /// <summary>
    /// Installs the event/crash logger using the user's Pictures\ImageGenerator.MAUI folder
    /// as the log destination, falling back to FileSystem.AppDataDirectory if Pictures is
    /// unavailable. Writes a "startup OK" line so an empty file is unambiguously
    /// distinguishable from "the logger never ran".
    /// </summary>
    public static void Install() => Install(rootDir: null);

    /// <summary>
    /// Test/diagnostic overload that lets callers pin the log root to a specific directory
    /// (e.g. a temp dir in a unit test) without depending on MAUI's Essentials.
    /// </summary>
    public static void Install(string? rootDir)
    {
        try
        {
            var preferred = rootDir ?? OutputPaths.GeneratedImagesDirectory;
            Directory.CreateDirectory(preferred);
            _logPath = Path.Combine(preferred, LogFileName);
        }
        catch
        {
            // Pictures path unavailable (perms, redirected user folder). Fall back to the
            // MAUI-managed app data dir; better something written somewhere than nothing.
            try
            {
                _logPath = Path.Combine(FileSystem.AppDataDirectory, LogFileName);
            }
            catch
            {
                // FileSystem isn't available in pure-test contexts. Bail; subsequent Log
                // calls become no-ops, which is the right behaviour.
                _logPath = null;
            }
        }
        Debug.WriteLine($"[CrashLogger] Writing to {_logPath}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };

#if WINDOWS
        // WinUI 3 surfaces XAML parse errors and dispatcher exceptions through
        // Microsoft.UI.Xaml.Application.UnhandledException, which is *not* the same channel
        // as AppDomain.UnhandledException — registering separately here keeps the crash log
        // useful when a navigation push fails while constructing a page.
        try
        {
            var app = Microsoft.UI.Xaml.Application.Current;
            if (app is not null)
            {
                app.UnhandledException += (_, e) =>
                {
                    Write($"WinUI Application.UnhandledException (Message=\"{e.Message}\")", e.Exception);
                    // Mark as handled so the WER dialog doesn't replace our log entry with a
                    // hard crash before the file flush completes. Tracing the bug is more
                    // important than failing fast.
                    e.Handled = true;
                };
            }
        }
        catch (Exception ex)
        {
            Write("CrashLogger.Install (WinUI hook failed)", ex);
        }
#endif

        // Confirms end-to-end: hooks registered AND the log file is writable. An empty
        // crash.log on a subsequent run means Install never ran — distinct from "ran but
        // nothing crashed".
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "unknown";
        WriteRaw($"[{Now()}] startup OK pid={Environment.ProcessId} version={version}");
    }

    /// <summary>
    /// Public entry-point for explicit catches in VMs / pages. Always non-throwing.
    /// </summary>
    public static void Log(string source, Exception ex) => Write(source, ex);

    /// <summary>
    /// For non-exception operational events worth logging (HTTP 4xx/5xx with a body,
    /// dropped-on-the-floor service responses, etc.) where there's no Exception to attach.
    /// Always non-throwing.
    /// </summary>
    public static void Log(string source, string message)
    {
        if (_logPath is null) return;
        try
        {
            WriteRaw($"[{Now()}] {source}\n{message}\n");
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private static void Write(string source, Exception? ex)
    {
        if (_logPath is null) return;
        try
        {
            WriteRaw($"[{Now()}] {source}\n{ex}\n");
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private static void WriteRaw(string line)
    {
        if (_logPath is null) return;
        try
        {
            lock (WriteGate)
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never throw.
        }
    }

    private static string Now() => DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
}
