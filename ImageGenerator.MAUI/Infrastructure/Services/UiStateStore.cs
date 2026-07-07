using System.Globalization;
using System.Runtime.CompilerServices;
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
    private const string PromptWriterTierKey = "imggen.prompt_writer_tier";
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
                if (SafeSet(k, v) && _logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(v));
                return Task.CompletedTask;
            });
    }

    public string? LoadPrompt()
    {
        var v = SafeGet(PromptKey);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadPrompt -> {Value}", Quote(v));
        return v;
    }

    public string? LoadModel() => LoadString(ModelKey);

    public void PersistPrompt(string value)
    {
        // Schedule() also cancels any pending stale write. It deliberately DROPS empties
        // (the token stores' clear semantics) — but a cleared prompt must still persist,
        // and an empty can't storm the store, so write that one immediately.
        _promptWriter.Schedule(value);
        if (string.IsNullOrEmpty(value) && SafeSet(PromptKey, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(value));
    }

    public void FlushPendingWrites() => _promptWriter.Flush();

    public void PersistModel(string value) => PersistString(ModelKey, value);

    public string? LoadResolution(string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        var v = SafeGet(key);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadResolution[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistResolution(string value, string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
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
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadAspectRatio[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistAspectRatio(string value, string? modelId)
    {
        var key = AspectRatioKeyFor(modelId);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
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
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadComfyUiCheckpoint[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistComfyUiCheckpoint(string value, string workflowName)
    {
        var key = ComfyUiCheckpointKeyFor(workflowName);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
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
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadComfyUiPreset[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistComfyUiPreset(string value, string workflowName)
    {
        var key = ComfyUiPresetKeyFor(workflowName);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistComfyUiPreset[{Key}]({Value})", key, Quote(value));
    }

    // Per-workflow like checkpoints: the labels are the workflow's own CustomCombo options.
    private static string ComfyUiPresetKeyFor(string workflowName) =>
        $"imggen.comfyui_preset.{workflowName}";

    public (double Width, double Height, double X, double Y)? LoadWindowBounds()
    {
        var raw = SafeGet(WindowBoundsKey);
        if (_logger.IsEnabled(LogLevel.Debug))
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
        if (SafeSet(WindowBoundsKey, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistWindowBounds({Value})", value);
    }

    public string? LoadComfyUiBaseUrl() => LoadString(ComfyUiBaseUrlKey);

    public void PersistComfyUiBaseUrl(string value) => PersistString(ComfyUiBaseUrlKey, value);

    public string? LoadOllamaBaseUrl() => LoadString(OllamaBaseUrlKey);

    public void PersistOllamaBaseUrl(string value) => PersistString(OllamaBaseUrlKey, value);

    public string? LoadOllamaModel() => LoadString(OllamaModelKey);

    public void PersistOllamaModel(string value) => PersistString(OllamaModelKey, value);

    public string? LoadOllamaVisionModel() => LoadString(OllamaVisionModelKey);

    public void PersistOllamaVisionModel(string value) => PersistString(OllamaVisionModelKey, value);

    public string? LoadOpenRouterVisionModel() => LoadString(OpenRouterVisionModelKey);

    public void PersistOpenRouterVisionModel(string value) => PersistString(OpenRouterVisionModelKey, value);

    // Default TRUE — show only free OpenRouter vision models unless the user opts in to paid ones.
    public bool LoadOpenRouterVisionFreeOnly() => LoadBool(OpenRouterVisionFreeOnlyKey, true);

    public void PersistOpenRouterVisionFreeOnly(bool value) =>
        PersistBool(OpenRouterVisionFreeOnlyKey, value, true);

    public string? LoadOutputFolder() => LoadString(OutputFolderKey);

    public void PersistOutputFolder(string value) => PersistString(OutputFolderKey, value);

    public string? LoadCivitaiModelRef() => LoadString(CivitaiModelRefKey);

    public void PersistCivitaiModelRef(string value) => PersistString(CivitaiModelRefKey, value);

    public bool LoadUseJsonPrompt() => LoadBool(UseJsonPromptKey, false);

    public void PersistUseJsonPrompt(bool value) => PersistBool(UseJsonPromptKey, value, false);

    // Default TRUE — free GPU memory after each render unless the user opted out.
    public bool LoadFreeVramAfterRendering() => LoadBool(FreeVramAfterRenderingKey, true);

    public void PersistFreeVramAfterRendering(bool value) =>
        PersistBool(FreeVramAfterRenderingKey, value, true);

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
            if (_logger.IsEnabled(LogLevel.Debug))
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
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.PersistAppTheme({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", AppThemeKey);
        }
    }

    public ModelTier? LoadPromptWriterTier()
    {
        try
        {
            // Stored as the ModelTier enum int. Sentinel -1 = never picked, so the picker keeps its
            // "Pick a prompt writer…" placeholder on first launch. A corrupt/foreign value (not a
            // defined ModelTier) also degrades to null rather than feeding an undefined enum forward.
            var stored = _preferences.Get(PromptWriterTierKey, -1);
            var tier = stored >= 0 && Enum.IsDefined(typeof(ModelTier), stored) ? (ModelTier?)stored : null;
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.LoadPromptWriterTier -> {Value}", tier);
            return tier;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", PromptWriterTierKey);
            return null;
        }
    }

    public void PersistPromptWriterTier(ModelTier value)
    {
        try
        {
            if (_preferences.ContainsKey(PromptWriterTierKey)
                && _preferences.Get(PromptWriterTierKey, -1) == (int)value) return;
            _preferences.Set(PromptWriterTierKey, (int)value);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.PersistPromptWriterTier({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", PromptWriterTierKey);
        }
    }

    // Truncated: the JSON prompt runs to several KB and used to be dumped verbatim into
    // app.log on every load AND persist — the value's head plus its length is enough to
    // identify it.
    private static string Quote(string? v) =>
        v is null ? "<null>"
        : v.Length <= 100 ? $"\"{v}\""
        // AsSpan, not v[..100]: the interpolation handler appends the ReadOnlySpan<char>
        // directly, so the truncated head never allocates a throwaway substring.
        : $"\"{v.AsSpan(0, 100)}…\" ({v.Length} chars)";

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

    // Per-accessor Load/Persist for the plain single-key settings all share one shape: read
    // (or skip-identical write) through SafeGet/SafeSet, then a debug line naming the accessor.
    // [CallerMemberName] supplies that name so the log reads "UiStateStore.LoadModel -> …" as
    // before, without each method spelling it out. (Debounced Prompt, the per-family keyed
    // settings, and the validated enum settings keep their own bodies — they don't fit this shape.)
    private string? LoadString(string key, [CallerMemberName] string caller = "")
    {
        var v = SafeGet(key);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.{Caller} -> {Value}", caller, Quote(v));
        return v;
    }

    private void PersistString(string key, string value, [CallerMemberName] string caller = "")
    {
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.{Caller}({Value})", caller, Quote(value));
    }

    private bool LoadBool(string key, bool defaultValue, [CallerMemberName] string caller = "")
    {
        try
        {
            var value = _preferences.Get(key, defaultValue);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.{Caller} -> {Value}", caller, value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", key);
            return defaultValue;
        }
    }

    private void PersistBool(string key, bool value, bool defaultValue, [CallerMemberName] string caller = "")
    {
        try
        {
            // Skip identical rewrites like SafeSet (bool overload of Get).
            if (_preferences.ContainsKey(key) && _preferences.Get(key, defaultValue) == value) return;
            _preferences.Set(key, value);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.{Caller}({Value})", caller, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", key);
        }
    }
}
