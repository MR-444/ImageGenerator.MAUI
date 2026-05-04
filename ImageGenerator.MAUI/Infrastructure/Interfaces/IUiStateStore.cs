namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns Preferences-backed persistence of non-secret UI state that should survive an app
/// restart — currently the last-used prompt and model. Mirrors <see cref="IApiTokenStore"/>
/// but uses Preferences rather than SecureStorage since these values aren't credentials.
/// </summary>
public interface IUiStateStore
{
    string? LoadPrompt();
    string? LoadModel();
    void PersistPrompt(string value);
    void PersistModel(string value);
}
