using System.Collections.Concurrent;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Tests.TestSupport;

// In-memory IPreferences stub. UiStateStore reaches Preferences.Default by
// default; that's a static MAUI handler that throws outside an app-container
// context. Tests inject this stub via the new optional ctor parameter to
// exercise the SafeGet/SafeSet swallow paths.
internal sealed class StubPreferences : IPreferences
{
    private readonly ConcurrentDictionary<string, object?> _store = new();

    public Exception? ThrowOnContainsKey { get; set; }
    public Exception? ThrowOnGet { get; set; }
    public Exception? ThrowOnSet { get; set; }

    /// <summary>Actual writes that reached the store — pins UiStateStore's skip-identical rule.</summary>
    public int SetCallCount { get; private set; }

    public void Seed(string key, object? value) => _store[key] = value;

    public bool ContainsKey(string key, string? sharedName = null)
    {
        if (ThrowOnContainsKey is { } e) throw e;
        return _store.ContainsKey(key);
    }

    public void Remove(string key, string? sharedName = null) => _store.TryRemove(key, out _);

    public void Clear(string? sharedName = null) => _store.Clear();

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        if (ThrowOnSet is { } e) throw e;
        SetCallCount++;
        _store[key] = value;
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        if (ThrowOnGet is { } e) throw e;
        return _store.TryGetValue(key, out var v) ? (T)v! : defaultValue;
    }
}
