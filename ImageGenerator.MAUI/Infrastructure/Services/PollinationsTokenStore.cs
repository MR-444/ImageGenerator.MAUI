using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class PollinationsTokenStore : IPollinationsTokenStore
{
    private const string TokenStorageKey = "imggen.pollinations_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<PollinationsTokenStore> _logger;
    private CancellationTokenSource? _persistCts;

    public PollinationsTokenStore(ILogger<PollinationsTokenStore> logger)
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
            _logger.LogWarning(ex, "Pollinations SecureStorage.{Op} failed", "Get");
            return null;
        }
    }

    public void Persist(string value)
    {
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pollinations SecureStorage.{Op} failed", "Set");
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
            _logger.LogWarning(ex, "Pollinations SecureStorage.{Op} failed", "Remove");
        }
    }
}
