using System.Collections.Concurrent;
using Microsoft.Maui.Storage;

namespace ImageGenerator.MAUI.Tests.TestSupport;

// In-memory ISecureStorage stub for tests. The token-store SUTs reach
// SecureStorage.Default by default; in unit-test runtimes that throws on the
// Windows DPAPI backend. Tests inject this stub via the new optional ctor
// parameter to exercise load/persist/forget without touching real storage.
//
// Throw-on-X fields let a test simulate a backend failure (the SUTs catch and
// log; tests assert the swallow). ConcurrentDictionary handles the cross-thread
// access from the debounced writer's background Task.
internal sealed class StubSecureStorage : ISecureStorage
{
    private readonly ConcurrentDictionary<string, string> _store = new();

    public Exception? ThrowOnGet { get; set; }
    public Exception? ThrowOnSet { get; set; }
    public Exception? ThrowOnRemove { get; set; }

    public string? Peek(string key) => _store.TryGetValue(key, out var v) ? v : null;

    public void Seed(string key, string value) => _store[key] = value;

    public Task<string?> GetAsync(string key)
    {
        if (ThrowOnGet is { } e) throw e;
        return Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);
    }

    public Task SetAsync(string key, string value)
    {
        if (ThrowOnSet is { } e) throw e;
        _store[key] = value;
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        if (ThrowOnRemove is { } e) throw e;
        return _store.TryRemove(key, out _);
    }

    public void RemoveAll() => _store.Clear();
}
