using System.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class UiStateStore : IUiStateStore
{
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";

    public string? LoadPrompt()
    {
        var v = SafeGet(PromptKey);
        Debug.WriteLine($"UiStateStore.LoadPrompt -> {Quote(v)}");
        return v;
    }

    public string? LoadModel()
    {
        var v = SafeGet(ModelKey);
        Debug.WriteLine($"UiStateStore.LoadModel -> {Quote(v)}");
        return v;
    }

    public void PersistPrompt(string value)
    {
        Debug.WriteLine($"UiStateStore.PersistPrompt({Quote(value)})");
        SafeSet(PromptKey, value);
    }

    public void PersistModel(string value)
    {
        Debug.WriteLine($"UiStateStore.PersistModel({Quote(value)})");
        SafeSet(ModelKey, value);
    }

    private static string Quote(string? v) => v is null ? "<null>" : $"\"{v}\"";

    private static string? SafeGet(string key)
    {
        try
        {
            // ContainsKey-then-Get avoids any ambiguity around how the platform handler treats
            // a `null` defaultValue for `Get<string?>` — some implementations coerce missing
            // keys to "" instead of null, which would defeat the IsNullOrEmpty guard upstream.
            if (!Preferences.Default.ContainsKey(key)) return null;
            var value = Preferences.Default.Get(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preferences.Get({key}) failed: {ex.Message}");
            return null;
        }
    }

    private static void SafeSet(string key, string value)
    {
        try
        {
            Preferences.Default.Set(key, value);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preferences.Set({key}) failed: {ex.Message}");
        }
    }
}
