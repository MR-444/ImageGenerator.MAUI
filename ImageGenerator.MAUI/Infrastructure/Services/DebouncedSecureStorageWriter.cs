using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Debounced single-key writer shared by the API/Pollinations token stores (SecureStorage)
/// and UiStateStore's prompt persistence (Preferences, via the injected writer).
/// Schedule() can be called freely on every keystroke; only the last value within the
/// debounce window is persisted. The CTS swap is locked so concurrent callers can't
/// observe a half-disposed source (the token-store call-sites are UI-thread today, but
/// the lock is cheap defense in depth and the multi-thread test pins the contract).
///
/// The writer is injected (defaults to SecureStorage.Default.SetAsync) so unit tests
/// can verify debounce + race semantics without touching the real platform storage.
/// </summary>
internal sealed class DebouncedSecureStorageWriter
{
    private readonly string _key;
    private readonly TimeSpan _delay;
    private readonly ILogger _logger;
    private readonly Func<string, string, Task> _writer;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private string? _pending;

    public DebouncedSecureStorageWriter(
        string key,
        TimeSpan delay,
        ILogger logger,
        Func<string, string, Task>? writer = null)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _delay = delay;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writer = writer ?? ((k, v) => SecureStorage.Default.SetAsync(k, v));
    }

    public void Schedule(string value)
    {
        CancellationToken token;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();

            if (string.IsNullOrEmpty(value))
            {
                _cts = null;
                _pending = null;
                return;
            }

            _cts = new CancellationTokenSource();
            _pending = value;
            token = _cts.Token;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, token);
                await _writer(_key, value);
                lock (_lock)
                {
                    // Only clear if no newer Schedule replaced it meanwhile.
                    if (_pending == value) _pending = null;
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later Schedule — drop silently.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SecureStorage.{Op} failed", "Set");
            }
        }, token);
    }

    /// <summary>
    /// Shutdown path: cancel the timer and write whatever is still pending, blocking until
    /// the store accepted it. Without this, a value scheduled within the debounce window of
    /// app close (paste-and-quit) never reaches storage. Racing an in-flight timer write at
    /// worst double-writes the same value — both backing stores treat that as a no-op.
    /// </summary>
    public void Flush()
    {
        string? value;
        lock (_lock)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            value = _pending;
            _pending = null;
        }
        if (value is null) return;

        try
        {
            _writer(_key, value).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage.{Op} failed", "Flush");
        }
    }
}
