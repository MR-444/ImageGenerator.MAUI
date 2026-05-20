using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class PollinationsTokenStoreTests
{
    // Pin the storage key. PollinationsTokenStore and ApiTokenStore must NOT
    // share a key — that would cross-wire the two providers' tokens.
    private const string TokenKey = "imggen.pollinations_token";

    private static readonly int PersistWaitMs = 700;

    private readonly StubSecureStorage _storage = new();
    private readonly PollinationsTokenStore _sut;

    public PollinationsTokenStoreTests()
    {
        _sut = new PollinationsTokenStore(NullLogger<PollinationsTokenStore>.Instance, _storage);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_ReturnsValue()
    {
        _storage.Seed(TokenKey, "poll_abcd1234");

        var result = await _sut.LoadAsync();

        result.Should().Be("poll_abcd1234");
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
        _storage.Seed(TokenKey, "poll_abcd1234");

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
        _sut.Persist("poll_persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().Be("poll_persisted");
    }

    [Fact]
    public async Task Persist_DoesNotTouchApiTokenKey()
    {
        // Cross-wire guard: even if both stores share an injected SecureStorage,
        // Pollinations writes must NOT leak into the Replicate token slot.
        _sut.Persist("poll_persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek("imggen.api_token").Should().BeNull();
    }
}
