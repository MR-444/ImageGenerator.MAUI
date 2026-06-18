using FluentAssertions;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Shared;

/// <summary>
/// The "do ComfyUI and Ollama share one box?" decision that gates GPU serialization. Must be
/// case-insensitive (the persisted ComfyUI host "fireEngine" vs the "fireengine" default) and port-blind.
/// </summary>
public class GpuColocationTests
{
    [Theory]
    [InlineData("http://fireEngine:8188", "http://fireengine:11434", true)]    // same host, different case + port
    [InlineData("http://127.0.0.1:8188", "http://127.0.0.1:11434", true)]
    [InlineData("http://fireengine:8188", "http://localhost:11434", false)]    // different hosts
    [InlineData("http://192.168.1.10:8188", "http://192.168.1.11:11434", false)]
    public void SameHost_ComparesHostCaseInsensitively_IgnoringPort(string comfy, string ollama, bool expected) =>
        GpuColocation.SameHost(comfy, ollama).Should().Be(expected);

    [Fact]
    public void SameHost_NullOrBlank_FallsBackToConfiguredDefaults()
    {
        // Both unset ⇒ ComfyUI default 127.0.0.1 vs Ollama default fireengine ⇒ not colocated.
        GpuColocation.SameHost(null, null).Should().BeFalse();
        GpuColocation.SameHost("", "http://fireengine:11434").Should().BeFalse();
        // The real user case: ComfyUI persisted to fireEngine, Ollama blank ⇒ default fireengine ⇒ colocated.
        GpuColocation.SameHost("http://fireEngine:8188", null).Should().BeTrue();
    }

    [Fact]
    public void SameHost_UnparseableUrl_IsNotColocated() =>
        GpuColocation.SameHost("not a url", "http://fireengine:11434").Should().BeFalse();
}
