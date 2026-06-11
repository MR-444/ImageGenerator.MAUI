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
    /// <summary>
    /// Resolution is persisted per option-format FAMILY, derived from the model id: ComfyUI
    /// models use megapixel presets ("2.0 MP") under their own key, everything else shares the
    /// legacy key (Ideogram "WxH" strings etc.). Switching model families therefore restores
    /// that family's last pick instead of slamming to the first option.
    /// </summary>
    string? LoadResolution(string? modelId);
    /// <summary>False when never persisted — the toggle is opt-in per session by default.</summary>
    bool LoadUseJsonPrompt();
    /// <summary>Null when never persisted — callers fall back to ModelConstants.ComfyUi.DefaultBaseUrl.</summary>
    string? LoadComfyUiBaseUrl();
    void PersistPrompt(string value);
    void PersistModel(string value);
    /// <inheritdoc cref="LoadResolution"/>
    void PersistResolution(string value, string? modelId);
    void PersistUseJsonPrompt(bool value);
    void PersistComfyUiBaseUrl(string value);
    /// <summary>Writes a still-pending debounced prompt immediately. Call on app shutdown.</summary>
    void FlushPendingWrites();
}
