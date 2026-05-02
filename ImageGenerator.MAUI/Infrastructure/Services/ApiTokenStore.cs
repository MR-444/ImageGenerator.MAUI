using System.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ApiTokenStore : IApiTokenStore
{
    private const string TokenStorageKey = "imggen.api_token";
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);

    private CancellationTokenSource? _persistCts;

    public async Task<string?> LoadAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(TokenStorageKey);
        }
        catch (Exception ex)
        {
            // Secure storage read failures aren't actionable by the user — log and continue.
            Debug.WriteLine($"SecureStorage.Get failed: {ex.Message}");
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
                Debug.WriteLine($"SecureStorage.Set failed: {ex.Message}");
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
            Debug.WriteLine($"SecureStorage.Remove failed: {ex.Message}");
        }
    }
}
