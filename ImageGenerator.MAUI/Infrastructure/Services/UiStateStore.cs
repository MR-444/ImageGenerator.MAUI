using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class UiStateStore : IUiStateStore
{
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";

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
        _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(value));
        SafeSet(PromptKey, value);
    }

    public void PersistModel(string value)
    {
        _logger.LogDebug("UiStateStore.PersistModel({Value})", Quote(value));
        SafeSet(ModelKey, value);
    }

    private static string Quote(string? v) => v is null ? "<null>" : $"\"{v}\"";

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

    private void SafeSet(string key, string value)
    {
        try
        {
            _preferences.Set(key, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", key);
        }
    }
}
