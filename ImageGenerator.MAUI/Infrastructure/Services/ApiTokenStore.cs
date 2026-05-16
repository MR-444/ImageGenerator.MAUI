using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ApiTokenStore : IApiTokenStore
{
    private const string TokenStorageKey = "imggen.api_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<ApiTokenStore> _logger;
    private CancellationTokenSource? _persistCts;

    public ApiTokenStore(ILogger<ApiTokenStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> LoadAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(TokenStorageKey);
        }
        catch (Exception ex)
        {
            // Secure storage read failures aren't actionable by the user — log and continue.
            _logger.LogWarning(ex, "SecureStorage.{Op} failed", "Get");
            return null;
        }
    }

    public void Persist(string value)
    {
        // Called on every keystroke into the API-token Entry. Debounce so a 50-char paste
        // collapses into a single SecureStorage write instead of 50 racing fire-and-forgets.
        _persistCts?.Cancel();
        _persistCts?.Dispose();

        if (string.IsNullOrEmpty(value))
        {
            _persistCts = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _persistCts = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                await SecureStorage.Default.SetAsync(TokenStorageKey, value);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a later keystroke — drop silently.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SecureStorage.{Op} failed", "Set");
            }
        }, token);
    }

    public void Forget()
    {
        try
        {
            SecureStorage.Default.Remove(TokenStorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SecureStorage.{Op} failed", "Remove");
        }
    }
}
