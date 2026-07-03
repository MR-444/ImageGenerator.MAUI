using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Vision;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class VisionObservationServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "vision-observation-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public async Task ObserveAsync_LocalOllama_UsesConfiguredVisionModelAndBaseUrl()
    {
        var ui = new Mock<IUiStateStore>();
        ui.Setup(s => s.LoadOllamaBaseUrl()).Returns("http://host:11434");
        ui.Setup(s => s.LoadOllamaVisionModel()).Returns("qwen3-vl");
        string? seenModel = null;
        string? seenBaseUrl = null;
        string? seenPrompt = null;
        string? seenImage = null;

        var sut = CreateSut(ui,
            (provider, modelId, baseUrl, systemPrompt, base64Image, _) =>
            {
                provider.Should().Be(VisionObservationProvider.LocalOllama);
                seenModel = modelId;
                seenBaseUrl = baseUrl;
                seenPrompt = systemPrompt;
                seenImage = base64Image;
                return Task.FromResult("  factual observation  ");
            });

        var result = await sut.ObserveAsync(new VisionObservationRequest(
            VisionObservationProvider.LocalOllama,
            "data:image/png;base64,abcd",
            "ref.png"));

        result.Success.Should().BeTrue();
        result.Observation.Should().Be("factual observation");
        seenModel.Should().Be("qwen3-vl");
        seenBaseUrl.Should().Be("http://host:11434");
        seenPrompt.Should().Be("BUNDLED OBSERVATION PROMPT");
        seenImage.Should().Be("abcd", "Ollama expects raw base64, not a data URI");
    }

    [Fact]
    public async Task ObserveAsync_OverridePresent_UsesItInsteadOfBundledPrompt()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "image-observation.md"), "PRIVATE OBSERVER");
        string? seenPrompt = null;

        var sut = CreateSut(new Mock<IUiStateStore>(),
            (_, _, _, systemPrompt, _, _) =>
            {
                seenPrompt = systemPrompt;
                return Task.FromResult("ok");
            });

        await sut.ObserveAsync(new VisionObservationRequest(
            VisionObservationProvider.LocalOllama,
            "abcd",
            "ref.png",
            ModelId: "qwen3-vl"));

        seenPrompt.Should().Be("PRIVATE OBSERVER");
    }

    [Fact]
    public async Task ObserveAsync_OpenRouter_NoApiKey_FailsWithoutCallingTransport()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);
        var called = false;
        var sut = CreateSut(new Mock<IUiStateStore>(),
            (_, _, _, _, _, _) =>
            {
                called = true;
                return Task.FromResult("not reached");
            },
            tokenStore);

        var result = await sut.ObserveAsync(new VisionObservationRequest(
            VisionObservationProvider.OpenRouter,
            "abcd",
            "ref.png",
            ModelId: "openai/some-vision-model"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter API key");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task ObserveAsync_OpenRouter_UsesTokenAsBackendCredential()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        VisionObservationProvider? seenProvider = null;
        string? seenModel = null;
        string? seenCredential = null;

        var sut = CreateSut(new Mock<IUiStateStore>(),
            (provider, modelId, baseUrl, _, _, _) =>
            {
                seenProvider = provider;
                seenModel = modelId;
                seenCredential = baseUrl;
                return Task.FromResult("remote observation");
            },
            tokenStore);

        var result = await sut.ObserveAsync(new VisionObservationRequest(
            VisionObservationProvider.OpenRouter,
            "abcd",
            "ref.png",
            ModelId: "google/gemini-3.1-flash-image"));

        result.Success.Should().BeTrue();
        result.Observation.Should().Be("remote observation");
        seenProvider.Should().Be(VisionObservationProvider.OpenRouter);
        seenModel.Should().Be("google/gemini-3.1-flash-image");
        seenCredential.Should().Be("sk-or");
    }

    [Fact]
    public async Task ObserveAsync_OpenRouterTransportFailure_NamesOpenRouterNotOllama()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        var sut = CreateSut(new Mock<IUiStateStore>(),
            (_, _, _, _, _, _) => throw new HttpRequestException("rate limited"),
            tokenStore);

        var result = await sut.ObserveAsync(new VisionObservationRequest(
            VisionObservationProvider.OpenRouter,
            "abcd",
            "ref.png",
            ModelId: "google/gemma-4-31b-it:free"));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter");
        result.Error.Should().NotContain("Ollama");
    }

    [Fact]
    public void ExtractErrorMessage_OpenRouterMetadataRaw_IsIncluded()
    {
        var message = VisionObservationService.ExtractErrorMessage(
            """{"error":{"message":"Provider returned error","metadata":{"raw":"google/gemma-4-31b-it:free is temporarily rate-limited upstream"}}}""");

        message.Should().Contain("Provider returned error");
        message.Should().Contain("temporarily rate-limited upstream");
    }

    private VisionObservationService CreateSut(
        Mock<IUiStateStore> uiStore,
        VisionObservationService.ObservationCompletion completion,
        Mock<IOpenRouterTokenStore>? openRouterTokenStore = null) =>
        new(uiStore.Object,
            NullLogger<VisionObservationService>.Instance,
            Mock.Of<IHttpClientFactory>(),
            completion,
            openRouterTokenStore?.Object,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Task<Stream> FakeBundledAsset(string assetName) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("BUNDLED OBSERVATION PROMPT")));
}
