using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public class UiStateStoreTests
{
    // Pin the storage keys here so a typo-rename in the SUT surfaces as a failure
    // rather than a silent loss of persisted UI state across app launches.
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";

    private readonly StubPreferences _preferences = new();
    private readonly UiStateStore _sut;

    public UiStateStoreTests()
    {
        _sut = new UiStateStore(NullLogger<UiStateStore>.Instance, _preferences);
    }

    [Fact]
    public void LoadPrompt_KeyMissing_ReturnsNull()
    {
        _sut.LoadPrompt().Should().BeNull();
    }

    [Fact]
    public void LoadPrompt_KeyPresent_ReturnsValue()
    {
        _preferences.Seed(PromptKey, "a cat in a hat");

        _sut.LoadPrompt().Should().Be("a cat in a hat");
    }

    [Fact]
    public void LoadPrompt_EmptyStringValue_ReturnsNull()
    {
        // SafeGet collapses empty -> null so the IsNullOrEmpty guard upstream
        // doesn't have to know whether the platform handler returned "" or null.
        _preferences.Seed(PromptKey, string.Empty);

        _sut.LoadPrompt().Should().BeNull();
    }

    [Fact]
    public void LoadPrompt_ContainsKeyThrows_SwallowedAndReturnsNull()
    {
        _preferences.ThrowOnContainsKey = new InvalidOperationException("backend down");

        _sut.LoadPrompt().Should().BeNull();
    }

    [Fact]
    public void LoadPrompt_GetThrows_SwallowedAndReturnsNull()
    {
        _preferences.Seed(PromptKey, "value");
        _preferences.ThrowOnGet = new InvalidOperationException("backend down");

        _sut.LoadPrompt().Should().BeNull();
    }

    [Fact]
    public void LoadModel_KeyPresent_ReturnsValueFromModelKey()
    {
        // Confirms LoadModel reads imggen.last_model and not imggen.last_prompt.
        _preferences.Seed(ModelKey, "openai/gpt-image-1.5");
        _preferences.Seed(PromptKey, "wrong-key value");

        _sut.LoadModel().Should().Be("openai/gpt-image-1.5");
    }

    [Fact]
    public void LoadModel_GetThrows_SwallowedAndReturnsNull()
    {
        _preferences.Seed(ModelKey, "value");
        _preferences.ThrowOnGet = new InvalidOperationException("backend down");

        _sut.LoadModel().Should().BeNull();
    }

    [Fact]
    public void PersistPrompt_WritesToPromptKey()
    {
        _sut.PersistPrompt("hello");

        _preferences.Get(PromptKey, string.Empty).Should().Be("hello");
    }

    [Fact]
    public void PersistPrompt_SetThrows_SwallowedSilently()
    {
        _preferences.ThrowOnSet = new InvalidOperationException("backend down");

        var act = () => _sut.PersistPrompt("hello");

        act.Should().NotThrow();
    }

    [Fact]
    public void PersistModel_WritesToModelKey()
    {
        _sut.PersistModel("openai/gpt-image-1.5");

        _preferences.Get(ModelKey, string.Empty).Should().Be("openai/gpt-image-1.5");
        _preferences.Get(PromptKey, string.Empty).Should().BeEmpty("PersistModel must not touch the prompt key");
    }

    [Fact]
    public void PersistModel_SetThrows_SwallowedSilently()
    {
        _preferences.ThrowOnSet = new InvalidOperationException("backend down");

        var act = () => _sut.PersistModel("v");

        act.Should().NotThrow();
    }
}
