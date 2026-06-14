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
    /// <summary>
    /// Last aspect-ratio pick, persisted per model family (ComfyUI vs the rest) like resolution.
    /// Null when never persisted — callers keep the model's default first option. "custom" is
    /// never persisted (its width/height aren't), so this never returns it.
    /// </summary>
    string? LoadAspectRatio(string? modelId);
    /// <summary>False when never persisted — the toggle is opt-in per session by default.</summary>
    bool LoadUseJsonPrompt();
    /// <summary>Null when never persisted — callers fall back to ModelConstants.ComfyUi.DefaultBaseUrl.</summary>
    string? LoadComfyUiBaseUrl();
    /// <summary>
    /// The user's configured output folder for generated images. Null when never set — callers
    /// fall back to <c>OutputPaths.DefaultGeneratedImagesDirectory</c>. Only the images and their
    /// json-prompt exports follow this; the ComfyUI workflow templates and app.log stay anchored
    /// at the default location.
    /// </summary>
    string? LoadOutputFolder();
    /// <summary>
    /// The raw CivitAI model reference text (URL or version id) for gallery-targeted posting.
    /// Persisted across sessions — unlike the post checkboxes — because the target model rarely
    /// changes; the off-by-default checkbox still gates any actual upload. Null when never set.
    /// </summary>
    string? LoadCivitaiModelRef();
    /// <summary>
    /// Per-workflow: checkpoints are architecture-bound, so one workflow's pick must never
    /// leak into another. Null when the user never explicitly picked one for this workflow.
    /// </summary>
    string? LoadComfyUiCheckpoint(string workflowName);
    /// <summary>
    /// Per-workflow: preset labels are the workflow's own CustomCombo options, so one
    /// workflow's pick must never leak into another. Null when never explicitly picked.
    /// </summary>
    string? LoadComfyUiPreset(string workflowName);
    void PersistPrompt(string value);
    void PersistModel(string value);
    /// <inheritdoc cref="LoadResolution"/>
    void PersistResolution(string value, string? modelId);
    /// <inheritdoc cref="LoadAspectRatio"/>
    void PersistAspectRatio(string value, string? modelId);
    void PersistUseJsonPrompt(bool value);
    void PersistComfyUiBaseUrl(string value);
    /// <inheritdoc cref="LoadOutputFolder"/>
    void PersistOutputFolder(string value);
    /// <inheritdoc cref="LoadCivitaiModelRef"/>
    void PersistCivitaiModelRef(string value);
    /// <inheritdoc cref="LoadComfyUiCheckpoint"/>
    void PersistComfyUiCheckpoint(string value, string workflowName);
    /// <inheritdoc cref="LoadComfyUiPreset"/>
    void PersistComfyUiPreset(string value, string workflowName);
    /// <summary>
    /// Last window size/position in DIPs. Null when never persisted or unparseable —
    /// callers fall back to a screen-relative first-launch size. Values may describe a
    /// monitor that no longer exists; callers must clamp to the current screen.
    /// </summary>
    (double Width, double Height, double X, double Y)? LoadWindowBounds();
    /// <inheritdoc cref="LoadWindowBounds"/>
    void PersistWindowBounds(double width, double height, double x, double y);
    /// <summary>Writes a still-pending debounced prompt immediately. Call on app shutdown.</summary>
    void FlushPendingWrites();
}
