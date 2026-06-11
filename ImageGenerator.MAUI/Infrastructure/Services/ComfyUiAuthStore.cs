using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ComfyUiAuthStore : IComfyUiAuthStore
{
    private const string TokenStorageKey = "imggen.comfyui_auth_header";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<ComfyUiAuthStore> _logger;
    private readonly ISecureStorage _secureStorage;
    private readonly DebouncedSecureStorageWriter _writer;

    public ComfyUiAuthStore(ILogger<ComfyUiAuthStore> logger, ISecureStorage? secureStorage = null)
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
            _logger.LogWarning(ex, "ComfyUI auth SecureStorage.{Op} failed", "Get");
            return null;
        }
    }

    public void Persist(string value) => _writer.Schedule(value);

    public void Forget()
    {
        // Cancel any pending debounced write first — otherwise it (or a shutdown flush)
        // would re-persist the header the user just forgot.
        _writer.Schedule(string.Empty);
        try
        {
            _secureStorage.Remove(TokenStorageKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUI auth SecureStorage.{Op} failed", "Remove");
        }
    }

    public void FlushPendingWrites() => _writer.Flush();
}
