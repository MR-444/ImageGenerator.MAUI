using System.Globalization;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class UiStateStore : IUiStateStore
{
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";
    private const string UseJsonPromptKey = "imggen.use_json_prompt";
    private const string FreeVramAfterRenderingKey = "imggen.free_vram_after_rendering";
    private const string AppThemeKey = "imggen.app_theme";
    private const string ResolutionKey = "imggen.last_resolution";
    private const string ComfyUiResolutionKey = "imggen.last_resolution.comfyui";
    private const string AspectRatioKey = "imggen.last_aspect_ratio";
    private const string ComfyUiAspectRatioKey = "imggen.last_aspect_ratio.comfyui";
    private const string ComfyUiBaseUrlKey = "imggen.comfyui_base_url";
    private const string OllamaBaseUrlKey = "imggen.ollama_base_url";
    private const string OllamaModelKey = "imggen.ollama_model";
    private const string OllamaVisionModelKey = "imggen.ollama_vision_model";
    private const string OpenRouterVisionModelKey = "imggen.openrouter_vision_model";
    private const string OpenRouterVisionFreeOnlyKey = "imggen.openrouter_vision_free_only";
    private const string CivitaiModelRefKey = "imggen.civitai_model_ref";
    private const string OutputFolderKey = "imggen.output_folder";
    private const string WindowBoundsKey = "imggen.window_bounds";

    private static readonly TimeSpan PromptDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<UiStateStore> _logger;
    private readonly IPreferences _preferences;
    private readonly DebouncedSecureStorageWriter _promptWriter;

    public UiStateStore(
        ILogger<UiStateStore> logger,
        IPreferences? preferences = null,
        TimeSpan? promptDebounceDelay = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferences = preferences ?? Preferences.Default;

        // The prompt is persisted on every Parameters.Prompt change — once per KEYSTROKE
        // when typing by hand. Same debounce as the token stores: only the last value in
        // the window reaches Preferences. The skip-identical rule still applies at write
        // time, so the startup load-echo stays a no-op.
        _promptWriter = new DebouncedSecureStorageWriter(
            PromptKey, promptDebounceDelay ?? PromptDebounceDelay, _logger,
            (k, v) =>
            {
                if (SafeSet(k, v))
                    _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(v));
                return Task.CompletedTask;
            });
    }

    public string? LoadPrompt()
    {
        var v = SafeGet(PromptKey);
        _logger.LogDebug("UiStateStore.LoadPrompt -> {Value}", Quote(v));
        return v;
    }

    public string? LoadModel()
    {
        var v = SafeGet(ModelKey);
        _logger.LogDebug("UiStateStore.LoadModel -> {Value}", Quote(v));
        return v;
    }

    public void PersistPrompt(string value)
    {
        // Schedule() also cancels any pending stale write. It deliberately DROPS empties
        // (the token stores' clear semantics) — but a cleared prompt must still persist,
        // and an empty can't storm the store, so write that one immediately.
        _promptWriter.Schedule(value);
        if (string.IsNullOrEmpty(value) && SafeSet(PromptKey, value))
            _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(value));
    }

    public void FlushPendingWrites() => _promptWriter.Flush();

    public void PersistModel(string value)
    {
        if (SafeSet(ModelKey, value))
            _logger.LogDebug("UiStateStore.PersistModel({Value})", Quote(value));
    }

    public string? LoadResolution(string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        var v = SafeGet(key);
        _logger.LogDebug("UiStateStore.LoadResolution[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistResolution(string value, string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        if (SafeSet(key, value))
            _logger.LogDebug("UiStateStore.PersistResolution[{Key}]({Value})", key, Quote(value));
    }

    // ComfyUI's MP presets and the other models' "WxH"-style strings are different option
    // families — each gets its own key so model switches restore that family's last pick.
    // The legacy key stays the default family, keeping previously saved data valid.
    private static string ResolutionKeyFor(string? modelId) =>
        Shared.Constants.ModelConstants.ComfyUi.IsId(modelId) ? ComfyUiResolutionKey : ResolutionKey;

    public string? LoadAspectRatio(string? modelId)
    {
        var key = AspectRatioKeyFor(modelId);
        var v = SafeGet(key);
        _logger.LogDebug("UiStateStore.LoadAspectRatio[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistAspectRatio(string value, string? modelId)
    {
        var key = AspectRatioKeyFor(modelId);
        if (SafeSet(key, value))
            _logger.LogDebug("UiStateStore.PersistAspectRatio[{Key}]({Value})", key, Quote(value));
    }

    // Aspect-ratio option sets differ by model family (a ComfyUI workflow defines its own), so key
    // per family like resolution — a portrait pick on ComfyUI won't override a landscape pick on
    // the Replicate models, and a switch back restores that family's last choice.
    private static string AspectRatioKeyFor(string? modelId) =>
        Shared.Constants.ModelConstants.ComfyUi.IsId(modelId) ? ComfyUiAspectRatioKey : AspectRatioKey;

    public string? LoadComfyUiCheckpoint(string workflowName)
    {
        var key = ComfyUiCheckpointKeyFor(workflowName);
        var v = SafeGet(key);
        _logger.LogDebug("UiStateStore.LoadComfyUiCheckpoint[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistComfyUiCheckpoint(string value, string workflowName)
    {
        var key = ComfyUiCheckpointKeyFor(workflowName);
        if (SafeSet(key, value))
            _logger.LogDebug("UiStateStore.PersistComfyUiCheckpoint[{Key}]({Value})", key, Quote(value));
    }

    // Per-workflow: checkpoints are architecture-bound (an SDXL checkpoint hard-fails in a
    // Flux workflow), so one workflow's pick must never leak into another.
    private static string ComfyUiCheckpointKeyFor(string workflowName) =>
        $"imggen.comfyui_checkpoint.{workflowName}";

    public string? LoadComfyUiPreset(string workflowName)
    {
        var key = ComfyUiPresetKeyFor(workflowName);
        var v = SafeGet(key);
        _logger.LogDebug("UiStateStore.LoadComfyUiPreset[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistComfyUiPreset(string value, string workflowName)
    {
        var key = ComfyUiPresetKeyFor(workflowName);
        if (SafeSet(key, value))
            _logger.LogDebug("UiStateStore.PersistComfyUiPreset[{Key}]({Value})", key, Quote(value));
    }

    // Per-workflow like checkpoints: the labels are the workflow's own CustomCombo options.
    private static string ComfyUiPresetKeyFor(string workflowName) =>
        $"imggen.comfyui_preset.{workflowName}";

    public (double Width, double Height, double X, double Y)? LoadWindowBounds()
    {
        var raw = SafeGet(WindowBoundsKey);
        _logger.LogDebug("UiStateStore.LoadWindowBounds -> {Value}", Quote(raw));
        if (raw is null) return null;

        // "w;h;x;y" in invariant culture. Anything malformed (old format, manual edit,
        // culture drift) degrades to "never persisted" rather than throwing at startup.
        var parts = raw.Split(';');
        if (parts.Length != 4) return null;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !double.IsFinite(values[i]))
                return null;
        }
        return (values[0], values[1], values[2], values[3]);
    }

    public void PersistWindowBounds(double width, double height, double x, double y)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{width};{height};{x};{y}");
        if (SafeSet(WindowBoundsKey, value))
            _logger.LogDebug("UiStateStore.PersistWindowBounds({Value})", value);
    }

    public string? LoadComfyUiBaseUrl()
    {
        var v = SafeGet(ComfyUiBaseUrlKey);
        _logger.LogDebug("UiStateStore.LoadComfyUiBaseUrl -> {Value}", Quote(v));
        return v;
    }

    public void PersistComfyUiBaseUrl(string value)
    {
        if (SafeSet(ComfyUiBaseUrlKey, value))
            _logger.LogDebug("UiStateStore.PersistComfyUiBaseUrl({Value})", Quote(value));
    }

    public string? LoadOllamaBaseUrl()
    {
        var v = SafeGet(OllamaBaseUrlKey);
        _logger.LogDebug("UiStateStore.LoadOllamaBaseUrl -> {Value}", Quote(v));
        return v;
    }

    public void PersistOllamaBaseUrl(string value)
    {
        if (SafeSet(OllamaBaseUrlKey, value))
            _logger.LogDebug("UiStateStore.PersistOllamaBaseUrl({Value})", Quote(value));
    }

    public string? LoadOllamaModel()
    {
        var v = SafeGet(OllamaModelKey);
        _logger.LogDebug("UiStateStore.LoadOllamaModel -> {Value}", Quote(v));
        return v;
    }

    public void PersistOllamaModel(string value)
    {
        if (SafeSet(OllamaModelKey, value))
            _logger.LogDebug("UiStateStore.PersistOllamaModel({Value})", Quote(value));
    }

    public string? LoadOllamaVisionModel()
    {
        var v = SafeGet(OllamaVisionModelKey);
        _logger.LogDebug("UiStateStore.LoadOllamaVisionModel -> {Value}", Quote(v));
        return v;
    }

    public void PersistOllamaVisionModel(string value)
    {
        if (SafeSet(OllamaVisionModelKey, value))
            _logger.LogDebug("UiStateStore.PersistOllamaVisionModel({Value})", Quote(value));
    }

    public string? LoadOpenRouterVisionModel()
    {
        var v = SafeGet(OpenRouterVisionModelKey);
        _logger.LogDebug("UiStateStore.LoadOpenRouterVisionModel -> {Value}", Quote(v));
        return v;
    }

    public void PersistOpenRouterVisionModel(string value)
    {
        if (SafeSet(OpenRouterVisionModelKey, value))
            _logger.LogDebug("UiStateStore.PersistOpenRouterVisionModel({Value})", Quote(value));
    }

    public bool LoadOpenRouterVisionFreeOnly()
    {
        try
        {
            var value = _preferences.Get(OpenRouterVisionFreeOnlyKey, true);
            _logger.LogDebug("UiStateStore.LoadOpenRouterVisionFreeOnly -> {Value}", value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", OpenRouterVisionFreeOnlyKey);
            return true;
        }
    }

    public void PersistOpenRouterVisionFreeOnly(bool value)
    {
        try
        {
            if (_preferences.ContainsKey(OpenRouterVisionFreeOnlyKey)
                && _preferences.Get(OpenRouterVisionFreeOnlyKey, true) == value) return;
            _preferences.Set(OpenRouterVisionFreeOnlyKey, value);
            _logger.LogDebug("UiStateStore.PersistOpenRouterVisionFreeOnly({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", OpenRouterVisionFreeOnlyKey);
        }
    }

    public string? LoadOutputFolder()
    {
        var v = SafeGet(OutputFolderKey);
        _logger.LogDebug("UiStateStore.LoadOutputFolder -> {Value}", Quote(v));
        return v;
    }

    public void PersistOutputFolder(string value)
    {
        if (SafeSet(OutputFolderKey, value))
            _logger.LogDebug("UiStateStore.PersistOutputFolder({Value})", Quote(value));
    }

    public string? LoadCivitaiModelRef()
    {
        var v = SafeGet(CivitaiModelRefKey);
        _logger.LogDebug("UiStateStore.LoadCivitaiModelRef -> {Value}", Quote(v));
        return v;
    }

    public void PersistCivitaiModelRef(string value)
    {
        if (SafeSet(CivitaiModelRefKey, value))
            _logger.LogDebug("UiStateStore.PersistCivitaiModelRef({Value})", Quote(value));
    }

    public bool LoadUseJsonPrompt()
    {
        try
        {
            var value = _preferences.Get(UseJsonPromptKey, false);
            _logger.LogDebug("UiStateStore.LoadUseJsonPrompt -> {Value}", value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", UseJsonPromptKey);
            return false;
        }
    }

    public void PersistUseJsonPrompt(bool value)
    {
        try
        {
            // Same skip-identical rule as SafeSet (bool overload of Get).
            if (_preferences.ContainsKey(UseJsonPromptKey)
                && _preferences.Get(UseJsonPromptKey, false) == value) return;
            _preferences.Set(UseJsonPromptKey, value);
            _logger.LogDebug("UiStateStore.PersistUseJsonPrompt({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", UseJsonPromptKey);
        }
    }

    public bool LoadFreeVramAfterRendering()
    {
        try
        {
            // Default TRUE — free GPU memory unless the user opted out.
            var value = _preferences.Get(FreeVramAfterRenderingKey, true);
            _logger.LogDebug("UiStateStore.LoadFreeVramAfterRendering -> {Value}", value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", FreeVramAfterRenderingKey);
            return true;
        }
    }

    public void PersistFreeVramAfterRendering(bool value)
    {
        try
        {
            if (_preferences.ContainsKey(FreeVramAfterRenderingKey)
                && _preferences.Get(FreeVramAfterRenderingKey, true) == value) return;
            _preferences.Set(FreeVramAfterRenderingKey, value);
            _logger.LogDebug("UiStateStore.PersistFreeVramAfterRendering({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", FreeVramAfterRenderingKey);
        }
    }

    public AppTheme LoadAppTheme()
    {
        try
        {
            // Stored as the AppTheme enum int. Default Unspecified = "System" (follow OS), the app's
            // original behavior. Validate the stored int: a corrupt/foreign value degrades to
            // Unspecified rather than feeding an undefined enum into UserAppTheme at startup.
            var stored = _preferences.Get(AppThemeKey, (int)AppTheme.Unspecified);
            var theme = stored switch
            {
                (int)AppTheme.Light => AppTheme.Light,
                (int)AppTheme.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
            _logger.LogDebug("UiStateStore.LoadAppTheme -> {Value}", theme);
            return theme;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", AppThemeKey);
            return AppTheme.Unspecified;
        }
    }

    public void PersistAppTheme(AppTheme value)
    {
        try
        {
            if (_preferences.ContainsKey(AppThemeKey)
                && _preferences.Get(AppThemeKey, (int)AppTheme.Unspecified) == (int)value) return;
            _preferences.Set(AppThemeKey, (int)value);
            _logger.LogDebug("UiStateStore.PersistAppTheme({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", AppThemeKey);
        }
    }

    // Truncated: the JSON prompt runs to several KB and used to be dumped verbatim into
    // app.log on every load AND persist — the value's head plus its length is enough to
    // identify it.
    private static string Quote(string? v) =>
        v is null ? "<null>"
        : v.Length <= 100 ? $"\"{v}\""
        : $"\"{v[..100]}…\" ({v.Length} chars)";

    private string? SafeGet(string key)
    {
        try
        {
            // ContainsKey-then-Get avoids any ambiguity around how the platform handler treats
            // a `null` defaultValue for `Get<string?>` — some implementations coerce missing
            // keys to "" instead of null, which would defeat the IsNullOrEmpty guard upstream.
            if (!_preferences.ContainsKey(key)) return null;
            var value = _preferences.Get(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", key);
            return null;
        }
    }

    /// <summary>True when the value was written; false when skipped (already stored) or failed.</summary>
    private bool SafeSet(string key, string value)
    {
        try
        {
            // Skip identical rewrites: every startup Load echoes straight back through the
            // VM's PropertyChanged persist hooks (LoadPrompt -> Parameters.Prompt ->
            // PersistPrompt with the very value just read), and bound controls re-push
            // unchanged values. Preferences.Set writes through to the backing store each
            // call, so the skip saves the I/O — and its multi-KB log line.
            if (_preferences.ContainsKey(key) && _preferences.Get(key, string.Empty) == value)
                return false;
            _preferences.Set(key, value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", key);
            return false;
        }
    }
}
