namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns Preferences-backed persistence of non-secret UI state that should survive an app
/// restart — currently the last-used prompt, model, and the structured-JSON toggle. Mirrors
/// <see cref="IApiTokenStore"/> but uses Preferences rather than SecureStorage since these
/// values aren't credentials.
/// </summary>
public interface IUiStateStore
{
    string? LoadPrompt();
    string? LoadModel();
    string? LoadResolution();
    /// <summary>False when never persisted — the toggle is opt-in per session by default.</summary>
    bool LoadUseJsonPrompt();
    void PersistPrompt(string value);
    void PersistModel(string value);
    void PersistResolution(string value);
    void PersistUseJsonPrompt(bool value);
}
