using System.Diagnostics;

namespace ImageGenerator.MAUI.Infrastructure.Diagnostics;

public static class CrashLogger
{
    private static string? _logPath;

    public static void Install()
    {
        _logPath = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
        Debug.WriteLine($"[CrashLogger] Writing to {_logPath}");

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        };
    }

    private static void Write(string source, Exception? ex)
    {
        if (_logPath is null) return;
        try
        {
            File.AppendAllText(
                _logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch
        {
            // Logging must never throw.
        }
    }
}
