using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class UiStateStore : IUiStateStore
{
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";
    private const string UseJsonPromptKey = "imggen.use_json_prompt";
    private const string ResolutionKey = "imggen.last_resolution";
    private const string ComfyUiResolutionKey = "imggen.last_resolution.comfyui";
    private const string ComfyUiBaseUrlKey = "imggen.comfyui_base_url";

    private readonly ILogger<UiStateStore> _logger;
    private readonly IPreferences _preferences;

    public UiStateStore(ILogger<UiStateStore> logger, IPreferences? preferences = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferences = preferences ?? Preferences.Default;
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
        if (SafeSet(PromptKey, value))
            _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(value));
    }

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
