using System.Reflection;
using ImageGenerator.MAUI.Shared.Constants;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace ImageGenerator.MAUI.Infrastructure.Diagnostics;

/// <summary>
/// Process-wide event/crash logger. The body delegates to NLog so that DI-injected
/// <see cref="Microsoft.Extensions.Logging.ILogger{T}"/> calls and these static fallbacks
/// share one physical log file. The static API is preserved for pre-DI / static callers
/// (e.g. <c>MauiProgram.CreateMauiApp</c> catch) that have no DI context.
/// </summary>
public static class CrashLogger
{
    private const string LogFileName = "app.log";
    // ${processid} because several app instances may write to the same physical app.log
    // (KeepFileOpen=false + cross-process locking) — without it their lines are
    // indistinguishable, which buried the 2026-06-10 "phantom second generation" analysis.
    private const string LayoutFormat =
        "${longdate}|${level:uppercase=true}|${processid}|${logger}|${message} ${exception:format=ToString}";

    private static readonly Logger Logger = LogManager.GetLogger("CrashLogger");
    private static string? _logPath;
    private static bool _hooksInstalled;

    /// <summary>
    /// Installs NLog targets at the user's <c>Pictures\ImageGenerator.MAUI</c> folder
    /// (fallback: <c>FileSystem.AppDataDirectory</c>) and registers process-wide
    /// unhandled-exception hooks. Writes a "startup OK" line so an empty file unambiguously
    /// means "the logger never ran" rather than "nothing crashed".
    /// </summary>
    public static void Install() => Install(rootDir: null);

    /// <summary>
    /// Test/diagnostic overload that pins the log root to a specific directory without
    /// depending on MAUI's <c>FileSystem</c>. Safe to call multiple times — reconfigures
    /// NLog each time; unhandled-exception hooks are registered exactly once per process.
    /// </summary>
    public static void Install(string? rootDir)
    {
        ConfigureNLog(rootDir);
        InstallUnhandledExceptionHooks();
        WriteStartupLine();
    }

    /// <summary>Public entry-point for explicit catches in static / pre-DI contexts.</summary>
    public static void Log(string source, Exception ex) => Logger.Error(ex, source);

    /// <summary>
    /// For non-exception operational events worth logging where there's no Exception to attach.
    /// </summary>
    public static void Log(string source, string message)
        => Logger.Error("{Source}\n{Message}", source, message);

    private static void ConfigureNLog(string? rootDir)
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
                // FileSystem isn't available in pure-test contexts without a rootDir override.
                _logPath = null;
            }
        }

        if (_logPath is null) return;

        // KeepFileOpen=false matches the original File.AppendAllText semantics — each write
        // opens/appends/closes — so external observers (and the CrashLoggerTests' direct
        // File.ReadAllText) see writes immediately without a flush. NLog 6 dropped the
        // ConcurrentWrites flag; the new default handles intra/cross-process locking safely.
        var fileTarget = new FileTarget("file")
        {
            FileName = _logPath,
            Layout = LayoutFormat,
            ArchiveAboveSize = 5_000_000,
            MaxArchiveFiles = 5,
            KeepFileOpen = false,
        };
        var debugTarget = new DebuggerTarget("debugger") { Layout = LayoutFormat };

        var config = new LoggingConfiguration();
        config.AddTarget(fileTarget);
        config.AddTarget(debugTarget);

        // Blackhole the HttpClient/Polly per-request chatter below Warning BEFORE any other
        // rule: a single ComfyUI run polls /history every 2 s and each poll emits 5-6 INFO
        // lines (LogicalHandler start/end, ClientHandler send/receive, Polly attempt) —
        // ~900 lines of noise per generation that drowned the lines that matter. Warnings
        // and errors (timeouts, retries-exhausted, non-2xx) still flow to the file. A final
        // rule with no targets is NLog's suppression idiom.
        foreach (var noisy in new[] { "System.Net.Http.*", "Polly*" })
        {
            // NOTE: LoggingRule's string ctor sets RuleName, not the pattern.
            var suppress = new LoggingRule { LoggerNamePattern = noisy, Final = true };
            suppress.SetLoggingLevels(NLog.LogLevel.Trace, NLog.LogLevel.Info);
            config.LoggingRules.Add(suppress);
        }

        // Default rule: Info+ to file + debugger.
        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, fileTarget, "*");
        config.AddRule(NLog.LogLevel.Info, NLog.LogLevel.Fatal, debugTarget, "*");

        // Bump Infrastructure.External.* (HTTP request/response details) to Debug so
        // wire-level diagnostics surface without flipping the global level.
        config.AddRule(
            NLog.LogLevel.Debug, NLog.LogLevel.Info,
            fileTarget,
            "ImageGenerator.MAUI.Infrastructure.External.*",
            final: false);

        // Same for the persistence stores (UiStateStore Load/Persist sequences) and the
        // ViewModels — together these trace who writes Parameters.Resolution in what order,
        // which is the diagnostic for the saved-resolution-resets-on-restart class of bug.
        config.AddRule(
            NLog.LogLevel.Debug, NLog.LogLevel.Info,
            fileTarget,
            "ImageGenerator.MAUI.Infrastructure.Services.*",
            final: false);
        config.AddRule(
            NLog.LogLevel.Debug, NLog.LogLevel.Info,
            fileTarget,
            "ImageGenerator.MAUI.Presentation.ViewModels.*",
            final: false);
        // Views carry the Generate-button activation forensics (GotFocus is Debug).
        config.AddRule(
            NLog.LogLevel.Debug, NLog.LogLevel.Info,
            fileTarget,
            "ImageGenerator.MAUI.Presentation.Views.*",
            final: false);

        LogManager.Configuration = config;
    }

    private static void InstallUnhandledExceptionHooks()
    {
        if (_hooksInstalled) return;
        _hooksInstalled = true;

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Logger.Error(e.ExceptionObject as Exception, "AppDomain.UnhandledException");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Logger.Error(e.Exception, "TaskScheduler.UnobservedTaskException");
            e.SetObserved();
        };

#if WINDOWS
        // WinUI 3 surfaces XAML parse errors and dispatcher exceptions through
        // Microsoft.UI.Xaml.Application.UnhandledException — a separate channel from
        // AppDomain.UnhandledException — so we hook it explicitly to keep the log useful
        // when a navigation push fails while constructing a page.
        try
        {
            var app = Microsoft.UI.Xaml.Application.Current;
            if (app is not null)
            {
                app.UnhandledException += (_, e) =>
                {
                    Logger.Error(e.Exception, $"WinUI Application.UnhandledException (Message=\"{e.Message}\")");
                    // Mark as handled so the WER dialog doesn't replace our log entry with
                    // a hard crash before the flush completes. Tracing the bug matters more
                    // than failing fast here.
                    e.Handled = true;
                };
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "CrashLogger.Install (WinUI hook failed)");
        }
#endif
    }

    private static void WriteStartupLine()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "unknown";
        Logger.Info("startup OK pid={Pid} version={Version}", Environment.ProcessId, version);
    }

    /// <summary>
    /// Counterpart to the "startup OK" line, written from Window.Destroying so app.log shows
    /// which instances were alive when (several instances share one log file). A hard kill or
    /// crash skips it — crashes are covered by the unhandled-exception hooks instead.
    /// Flushes and shuts NLog down; only call when the process is exiting (or re-Install after).
    /// </summary>
    public static void WriteShutdownLine()
    {
        Logger.Info("shutdown pid={Pid}", Environment.ProcessId);
        try { LogManager.Shutdown(); } catch { /* never block window teardown */ }
    }
}
