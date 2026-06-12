using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class CivitaiTokenStoreTests
{
    // Pin the storage key. CivitaiTokenStore must NOT share a key with any other
    // provider store — that would cross-wire the tokens.
    private const string TokenKey = "imggen.civitai_token";

    private static readonly int PersistWaitMs = 700;

    private readonly StubSecureStorage _storage = new();
    private readonly CivitaiTokenStore _sut;

    public CivitaiTokenStoreTests()
    {
        _sut = new CivitaiTokenStore(NullLogger<CivitaiTokenStore>.Instance, _storage);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_ReturnsValue()
    {
        _storage.Seed(TokenKey, "civitai_abcd1234");

        var result = await _sut.LoadAsync();

        result.Should().Be("civitai_abcd1234");
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
        _storage.Seed(TokenKey, "civitai_abcd1234");

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
        _sut.Persist("civitai_persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().Be("civitai_persisted");
    }

    [Fact]
    public async Task Persist_DoesNotTouchOtherProviderKeys()
    {
        // Cross-wire guard: even if all stores share an injected SecureStorage,
        // CivitAI writes must NOT leak into the other providers' token slots.
        _sut.Persist("civitai_persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek("imggen.api_token").Should().BeNull();
        _storage.Peek("imggen.pollinations_token").Should().BeNull();
    }
}
