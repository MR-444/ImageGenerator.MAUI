using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class AnthropicTokenStoreTests
{
    // Pin the storage key. AnthropicTokenStore must NOT share a key with any other provider store —
    // that would cross-wire the tokens.
    private const string TokenKey = "imggen.anthropic_token";

    private static readonly int PersistWaitMs = 700;

    private readonly StubSecureStorage _storage = new();
    private readonly AnthropicTokenStore _sut;

    public AnthropicTokenStoreTests()
    {
        _sut = new AnthropicTokenStore(NullLogger<AnthropicTokenStore>.Instance, _storage);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_ReturnsValue()
    {
        _storage.Seed(TokenKey, "sk-ant-abcd1234");

        var result = await _sut.LoadAsync();

        result.Should().Be("sk-ant-abcd1234");
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
        _storage.Seed(TokenKey, "sk-ant-abcd1234");

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
        _sut.Persist("sk-ant-persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().Be("sk-ant-persisted");
    }

    [Fact]
    public async Task Persist_DoesNotTouchOtherProviderKeys()
    {
        // Cross-wire guard: even if all stores share an injected SecureStorage, Anthropic writes
        // must NOT leak into the other providers' token slots.
        _sut.Persist("sk-ant-persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek("imggen.api_token").Should().BeNull();
        _storage.Peek("imggen.civitai_token").Should().BeNull();
    }
}
