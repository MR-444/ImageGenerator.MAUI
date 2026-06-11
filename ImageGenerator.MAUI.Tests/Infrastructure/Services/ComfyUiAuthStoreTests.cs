using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class ComfyUiAuthStoreTests
{
    // Pin the storage key. The ComfyUI auth header must NOT share a key with the
    // Replicate or Pollinations token stores — that would cross-wire providers.
    private const string TokenKey = "imggen.comfyui_auth_header";

    private static readonly int PersistWaitMs = 700;

    private readonly StubSecureStorage _storage = new();
    private readonly ComfyUiAuthStore _sut;

    public ComfyUiAuthStoreTests()
    {
        _sut = new ComfyUiAuthStore(NullLogger<ComfyUiAuthStore>.Instance, _storage);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_ReturnsValue()
    {
        _storage.Seed(TokenKey, "Bearer abc123");

        var result = await _sut.LoadAsync();

        result.Should().Be("Bearer abc123");
    }

    [Fact]
    public async Task LoadAsync_NoStoredValue_ReturnsNull()
    {
        var result = await _sut.LoadAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_GetThrows_SwallowedAndReturnsNull()
    {
        _storage.ThrowOnGet = new InvalidOperationException("dpapi failure");

        var result = await _sut.LoadAsync();

        result.Should().BeNull();
    }

    [Fact]
    public void Forget_RemovesStoredValue()
    {
        _storage.Seed(TokenKey, "Bearer abc123");

        _sut.Forget();

        _storage.Peek(TokenKey).Should().BeNull();
    }

    [Fact]
    public void Forget_RemoveThrows_SwallowedSilently()
    {
        _storage.ThrowOnRemove = new InvalidOperationException("dpapi failure");

        var act = () => _sut.Forget();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Persist_AfterDebounceWindow_WritesValueToStorage()
    {
        _sut.Persist("Bearer persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().Be("Bearer persisted");
    }

    [Fact]
    public async Task Persist_DoesNotTouchOtherTokenKeys()
    {
        // Cross-wire guard: even if all stores share an injected SecureStorage,
        // ComfyUI auth writes must NOT leak into the provider token slots.
        _sut.Persist("Bearer persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek("imggen.api_token").Should().BeNull();
        _storage.Peek("imggen.pollinations_token").Should().BeNull();
    }
}
