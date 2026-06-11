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
    private const string UseJsonPromptKey = "imggen.use_json_prompt";

    // Real (tiny) debounce window for the prompt writer; prompt tests wait comfortably
    // past it so slow CI runners don't race the background write — same numbers as
    // DebouncedSecureStorageWriterTests.
    private static readonly TimeSpan PromptDebounce = TimeSpan.FromMilliseconds(50);
    private const int WaitPastDebounceMs = 250;

    private readonly StubPreferences _preferences = new();
    private readonly UiStateStore _sut;

    public UiStateStoreTests()
    {
        _sut = new UiStateStore(NullLogger<UiStateStore>.Instance, _preferences, PromptDebounce);
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
    public void FlushPendingWrites_PersistsScheduledPromptImmediately()
    {
        // App-close path: a prompt still inside the debounce window must reach
        // Preferences synchronously when Window.Destroying flushes the store.
        var sut = new UiStateStore(
            NullLogger<UiStateStore>.Instance, _preferences, TimeSpan.FromMinutes(5));
        sut.PersistPrompt("typed just before closing");

        sut.FlushPendingWrites();

        _preferences.Get(PromptKey, string.Empty).Should().Be("typed just before closing");
    }

    // Resolution is persisted per option-format family: ComfyUI models ("comfyui/*") get
    // their own key; every other model shares the legacy key so old saved data stays valid.
    private const string ResolutionKey = "imggen.last_resolution";
    private const string ComfyResolutionKey = "imggen.last_resolution.comfyui";
    private const string IdeogramModel = "ideogram-ai/ideogram-v4-balanced";
    private const string ComfyModel = "comfyui/Ideogram workflow_MR";

    [Fact]
    public void LoadResolution_KeyMissing_ReturnsNull()
    {
        _sut.LoadResolution(IdeogramModel).Should().BeNull();
        _sut.LoadResolution(ComfyModel).Should().BeNull();
    }

    [Fact]
    public void Resolution_NonComfyModel_RoundTripsThroughTheLegacyKey()
    {
        _sut.PersistResolution("1440x2880", IdeogramModel);

        _sut.LoadResolution(IdeogramModel).Should().Be("1440x2880");
        _preferences.Get(ResolutionKey, string.Empty).Should().Be("1440x2880");
    }

    [Fact]
    public void Resolution_ComfyModel_RoundTripsThroughTheComfyKey_LeavingLegacyUntouched()
    {
        _sut.PersistResolution("2.0 MP", ComfyModel);

        _sut.LoadResolution(ComfyModel).Should().Be("2.0 MP");
        _preferences.Get(ComfyResolutionKey, string.Empty).Should().Be("2.0 MP");
        _preferences.Get(ResolutionKey, string.Empty).Should().BeEmpty(
            "a ComfyUI pick must not clobber the other models' saved resolution");
    }

    [Fact]
    public void Resolution_FamiliesAreIsolated_EachModelRestoresItsOwnPick()
    {
        _sut.PersistResolution("1440x2880", IdeogramModel);
        _sut.PersistResolution("2.0 MP", ComfyModel);

        _sut.LoadResolution(IdeogramModel).Should().Be("1440x2880");
        _sut.LoadResolution(ComfyModel).Should().Be("2.0 MP");
    }

    [Fact]
    public void LoadResolution_ComfyModel_DoesNotFallBackToTheLegacyKey()
    {
        _preferences.Seed(ResolutionKey, "1440x2880");

        _sut.LoadResolution(ComfyModel).Should().BeNull(
            "an Ideogram 'WxH' string is never a valid ComfyUI MP preset");
    }

    [Fact]
    public void LoadResolution_NullModelId_ReadsTheLegacyKey()
    {
        _preferences.Seed(ResolutionKey, "1440x2880");

        _sut.LoadResolution(null).Should().Be("1440x2880");
    }

    [Fact]
    public void LoadComfyUiBaseUrl_KeyMissing_ReturnsNull()
    {
        _sut.LoadComfyUiBaseUrl().Should().BeNull();
    }

    [Fact]
    public void ComfyUiBaseUrl_RoundTripsThroughPreferences()
    {
        _sut.PersistComfyUiBaseUrl("http://fireEngine:8188");

        _sut.LoadComfyUiBaseUrl().Should().Be("http://fireEngine:8188");
    }

    [Fact]
    public void ComfyUiCheckpoint_RoundTripsPerWorkflowKeys()
    {
        // Per-workflow isolation: checkpoints are architecture-bound, so one workflow's
        // pick must never surface for another.
        _sut.PersistComfyUiCheckpoint("flux.safetensors", "Flux Workflow");
        _sut.PersistComfyUiCheckpoint("sdxl.safetensors", "SDXL Workflow");

        _sut.LoadComfyUiCheckpoint("Flux Workflow").Should().Be("flux.safetensors");
        _sut.LoadComfyUiCheckpoint("SDXL Workflow").Should().Be("sdxl.safetensors");
        _sut.LoadComfyUiCheckpoint("Never Picked").Should().BeNull();
        _preferences.Get("imggen.comfyui_checkpoint.Flux Workflow", string.Empty)
            .Should().Be("flux.safetensors", "the key shape is pinned like the others above");
    }

    [Fact]
    public void LoadUseJsonPrompt_KeyMissing_ReturnsFalse()
    {
        _sut.LoadUseJsonPrompt().Should().BeFalse();
    }

    [Fact]
    public void UseJsonPrompt_RoundTripsThroughPreferences()
    {
        _sut.PersistUseJsonPrompt(true);
        _sut.LoadUseJsonPrompt().Should().BeTrue();

        _sut.PersistUseJsonPrompt(false);
        _sut.LoadUseJsonPrompt().Should().BeFalse();
    }

    [Fact]
    public void LoadUseJsonPrompt_GetThrows_SwallowedAndReturnsFalse()
    {
        _preferences.Seed(UseJsonPromptKey, true);
        _preferences.ThrowOnGet = new InvalidOperationException("backend down");

        _sut.LoadUseJsonPrompt().Should().BeFalse();
    }

    [Fact]
    public void PersistUseJsonPrompt_SetThrows_IsSwallowed()
    {
        _preferences.ThrowOnSet = new InvalidOperationException("backend down");

        var act = () => _sut.PersistUseJsonPrompt(true);

        act.Should().NotThrow();
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
    public async Task PersistPrompt_WritesToPromptKey_AfterTheDebounceWindow()
    {
        _sut.PersistPrompt("hello");

        _preferences.SetCallCount.Should().Be(0, "the prompt write is debounced, not immediate");
        await Task.Delay(WaitPastDebounceMs);
        _preferences.Get(PromptKey, string.Empty).Should().Be("hello");
    }

    [Fact]
    public async Task PersistPrompt_KeystrokeBurst_CollapsesToOneWriteOfTheLastValue()
    {
        // Hand-typing fires PersistPrompt once per keystroke; only the final value may
        // reach Preferences.
        for (var i = 0; i < 10; i++)
        {
            _sut.PersistPrompt($"a cat in a ha{i}");
        }
        await Task.Delay(WaitPastDebounceMs);

        _preferences.SetCallCount.Should().Be(1);
        _preferences.Get(PromptKey, string.Empty).Should().Be("a cat in a ha9");
    }

    // Skip-identical rule: the startup Loads echo straight back through the VM's persist
    // hooks (LoadPrompt -> Parameters.Prompt -> PersistPrompt with the value just read), so
    // identical rewrites must never reach the backing store.

    [Fact]
    public async Task PersistPrompt_UnchangedValue_SkipsTheWrite()
    {
        _sut.PersistPrompt("hello");
        await Task.Delay(WaitPastDebounceMs);

        _sut.PersistPrompt("hello");
        await Task.Delay(WaitPastDebounceMs);

        _preferences.SetCallCount.Should().Be(1, "identical rewrites are wasted I/O");
        _preferences.Get(PromptKey, string.Empty).Should().Be("hello");
    }

    [Fact]
    public async Task PersistPrompt_ChangedValue_StillWrites()
    {
        _sut.PersistPrompt("hello");
        await Task.Delay(WaitPastDebounceMs);
        _sut.PersistPrompt("hello world");
        await Task.Delay(WaitPastDebounceMs);

        _preferences.SetCallCount.Should().Be(2);
        _preferences.Get(PromptKey, string.Empty).Should().Be("hello world");
    }

    [Fact]
    public async Task PersistPrompt_Clear_PersistsImmediately_AndCancelsThePendingWrite()
    {
        // Clearing must both stick (an empty prompt is a real state) and cancel any
        // not-yet-flushed keystroke value, or stale text would resurrect after the delay.
        _sut.PersistPrompt("stale text");
        _sut.PersistPrompt(string.Empty);
        await Task.Delay(WaitPastDebounceMs);

        _preferences.SetCallCount.Should().Be(1, "only the clear may land");
        _preferences.Get(PromptKey, "sentinel").Should().BeEmpty();
        _sut.LoadPrompt().Should().BeNull("SafeGet collapses empty to null");
    }

    [Fact]
    public void PersistResolution_UnchangedValue_SkipsTheWrite()
    {
        _sut.PersistResolution("2.0 MP", ComfyModel);

        _sut.PersistResolution("2.0 MP", ComfyModel);

        _preferences.SetCallCount.Should().Be(1);
    }

    [Fact]
    public void PersistUseJsonPrompt_UnchangedValue_SkipsTheWrite()
    {
        _sut.PersistUseJsonPrompt(true);

        _sut.PersistUseJsonPrompt(true);

        _preferences.SetCallCount.Should().Be(1);
        _sut.LoadUseJsonPrompt().Should().BeTrue();
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
