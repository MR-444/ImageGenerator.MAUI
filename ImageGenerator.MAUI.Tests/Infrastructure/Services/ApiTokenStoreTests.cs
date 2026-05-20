using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class ApiTokenStoreTests
{
    // Pin the storage key — a typo-rename here would silently strand the
    // user's saved Replicate token across the rename boundary.
    private const string TokenKey = "imggen.api_token";

    // Wall-clock buffer above the SUT's hard-coded 500ms debounce. Two stores'
    // Persist tests run in parallel (different fixture classes), so this only
    // counts once toward total test wall time.
    private static readonly int PersistWaitMs = 700;

    private readonly StubSecureStorage _storage = new();
    private readonly ApiTokenStore _sut;

    public ApiTokenStoreTests()
    {
        _sut = new ApiTokenStore(NullLogger<ApiTokenStore>.Instance, _storage);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_ReturnsValue()
    {
        _storage.Seed(TokenKey, "r8_abcd1234");

        var result = await _sut.LoadAsync();

        result.Should().Be("r8_abcd1234");
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
        _storage.Seed(TokenKey, "r8_abcd1234");

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
        // End-to-end: confirms the store wires its SecureStorage instance into
        // the debounced writer (so the injected stub receives the eventual
        // SetAsync call). Debounce semantics themselves are covered in
        // DebouncedSecureStorageWriterTests.
        _sut.Persist("r8_persisted");

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().Be("r8_persisted");
    }

    [Fact]
    public async Task Persist_EmptyValue_DoesNotWriteAnything()
    {
        // The writer drops empty values without scheduling (matches the
        // "user cleared the field" semantics from the pre-extraction code).
        _sut.Persist(string.Empty);

        await Task.Delay(PersistWaitMs);

        _storage.Peek(TokenKey).Should().BeNull();
    }
}
