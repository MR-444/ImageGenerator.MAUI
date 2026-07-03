using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class OpenRouterTokenStore : IOpenRouterTokenStore
{
    private const string TokenStorageKey = "imggen.openrouter_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<OpenRouterTokenStore> _logger;
    private readonly ISecureStorage _secureStorage;
    private readonly DebouncedSecureStorageWriter _writer;

    public OpenRouterTokenStore(ILogger<OpenRouterTokenStore> logger, ISecureStorage? secureStorage = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _secureStorage = secureStorage ?? SecureStorage.Default;
        _writer = new DebouncedSecureStorageWriter(
            TokenStorageKey, DebounceDelay, _logger,
            (k, v) => _secureStorage.SetAsync(k, v));
    }

    public async Task<string?> LoadAsync()
    {
        try
        {
            return await _secureStorage.GetAsync(TokenStorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenRouter SecureStorage.{Op} failed", "Get");
            return null;
        }
    }

    public void Persist(string value) => _writer.Schedule(value);

    public void Forget()
    {
        _writer.Schedule(string.Empty);
        try
        {
            _secureStorage.Remove(TokenStorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenRouter SecureStorage.{Op} failed", "Remove");
        }
    }

    public void FlushPendingWrites() => _writer.Flush();
}
