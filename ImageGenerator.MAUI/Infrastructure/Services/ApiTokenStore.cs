using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ApiTokenStore : IApiTokenStore
{
    private const string TokenStorageKey = "imggen.api_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<ApiTokenStore> _logger;
    private readonly DebouncedSecureStorageWriter _writer;

    public ApiTokenStore(ILogger<ApiTokenStore> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _writer = new DebouncedSecureStorageWriter(TokenStorageKey, DebounceDelay, _logger);
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

    public void Persist(string value) => _writer.Schedule(value);

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
