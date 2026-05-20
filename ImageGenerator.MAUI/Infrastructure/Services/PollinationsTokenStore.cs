using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class PollinationsTokenStore : IPollinationsTokenStore
{
    private const string TokenStorageKey = "imggen.pollinations_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<PollinationsTokenStore> _logger;
    private readonly ISecureStorage _secureStorage;
    private readonly DebouncedSecureStorageWriter _writer;

    public PollinationsTokenStore(ILogger<PollinationsTokenStore> logger, ISecureStorage? secureStorage = null)
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
            _logger.LogWarning(ex, "Pollinations SecureStorage.{Op} failed", "Get");
            return null;
        }
    }

    public void Persist(string value) => _writer.Schedule(value);

    public void Forget()
    {
        try
        {
            _secureStorage.Remove(TokenStorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pollinations SecureStorage.{Op} failed", "Remove");
        }
    }
}
