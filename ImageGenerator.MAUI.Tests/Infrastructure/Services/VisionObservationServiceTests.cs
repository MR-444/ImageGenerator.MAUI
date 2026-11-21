using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Vision;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
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
    public async Task ObserveAsync_Local_CallsTheModelAndAddressTheCallerResolved()
    {
        string? seenModel = null;
        string? seenBaseUrl = null;
        string? seenPrompt = null;
        string? seenImage = null;
        ModelProvider? seenProvider = null;

        var sut = CreateSut(
            (provider, modelId, baseUrl, systemPrompt, base64Image, _) =>
            {
                seenProvider = provider;
                seenModel = modelId;
                seenBaseUrl = baseUrl;
                seenPrompt = systemPrompt;
                seenImage = base64Image;
                return Task.FromResult("  factual observation  ");
            });

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.Local, "qwen3-vl", "http://host:11434")));

        result.Success.Should().BeTrue();
        result.Observation.Should().Be("factual observation");
        seenProvider.Should().Be(ModelProvider.Local);
        seenModel.Should().Be("qwen3-vl");
        seenBaseUrl.Should().Be("http://host:11434");
        seenPrompt.Should().Be("BUNDLED OBSERVATION PROMPT");
        seenImage.Should().Be("abcd", "Ollama expects raw base64, not a data URI");
    }

    [Fact]
    public async Task ObserveAsync_LocalWithNoAddressAsksTheServerEveryOllamaAddressDefaultsTo()
    {
        // A target built by a page whose base-url box was cleared is not a request to POST to "".
        string? seenBaseUrl = null;
        var sut = CreateSut((_, _, baseUrl, _, _, _) =>
        {
            seenBaseUrl = baseUrl;
            return Task.FromResult("ok");
        });

        await sut.ObserveAsync(Request(new LlmTarget(ModelProvider.Local, "qwen3-vl", string.Empty)));

        seenBaseUrl.Should().Be(ModelConstants.Ollama.DefaultBaseUrl);
    }

    [Fact]
    public async Task ObserveAsync_OverridePresent_UsesItInsteadOfBundledPrompt()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "image-observation.md"), "PRIVATE OBSERVER");
        string? seenPrompt = null;

        var sut = CreateSut((_, _, _, systemPrompt, _, _) =>
        {
            seenPrompt = systemPrompt;
            return Task.FromResult("ok");
        });

        await sut.ObserveAsync(Request(new LlmTarget(ModelProvider.Local, "qwen3-vl", "http://host:11434")));

        seenPrompt.Should().Be("PRIVATE OBSERVER");
    }

    [Fact]
    public async Task ObserveAsync_OpenRouter_NoApiKey_FailsWithoutCallingTransport()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);
        var called = false;
        var sut = CreateSut(
            (_, _, _, _, _, _) =>
            {
                called = true;
                return Task.FromResult("not reached");
            },
            tokenStore);

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.OpenRouter, "openai/some-vision-model", string.Empty)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter API key");
        result.Error.Should().Contain("Settings page", "the sentence has to say where to fix it");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task ObserveAsync_OpenRouter_NoModel_FailsWithoutCallingTransport()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        var called = false;
        var sut = CreateSut(
            (_, _, _, _, _, _) =>
            {
                called = true;
                return Task.FromResult("not reached");
            },
            tokenStore);

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.OpenRouter, string.Empty, string.Empty)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter model selected");
        result.Error.Should().NotContain("Ollama", "the miss belongs to the provider on screen");
        called.Should().BeFalse("an empty model id must not be sent and hoped over");
    }

    [Fact]
    public async Task ObserveAsync_OpenRouter_UsesTokenAsBackendCredential()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        ModelProvider? seenProvider = null;
        string? seenModel = null;
        string? seenCredential = null;

        var sut = CreateSut(
            (provider, modelId, baseUrl, _, _, _) =>
            {
                seenProvider = provider;
                seenModel = modelId;
                seenCredential = baseUrl;
                return Task.FromResult("remote observation");
            },
            tokenStore);

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.OpenRouter, "google/gemini-3.1-flash-image", string.Empty)));

        result.Success.Should().BeTrue();
        result.Observation.Should().Be("remote observation");
        seenProvider.Should().Be(ModelProvider.OpenRouter);
        seenModel.Should().Be("google/gemini-3.1-flash-image");
        seenCredential.Should().Be("sk-or");
    }

    [Fact]
    public async Task ObserveAsync_AnthropicTarget_RefusesBecauseNothingCanSendItAnImage()
    {
        // The provider enum is shared now, so a role that gains a provider can point an image at a
        // provider that cannot take one. Refusing in words beats a 400 from the endpoint.
        var called = false;
        var sut = CreateSut((_, _, _, _, _, _) =>
        {
            called = true;
            return Task.FromResult("must never run");
        });

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.Anthropic, "claude-sonnet-4-6", string.Empty)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Claude");
        result.Error.Should().Contain("cannot observe images");
        called.Should().BeFalse();
    }

    [Fact]
    public async Task ObserveAsync_OpenRouterTransportFailure_NamesOpenRouterNotOllama()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        var sut = CreateSut(
            (_, _, _, _, _, _) => throw new HttpRequestException("rate limited"),
            tokenStore);

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.OpenRouter, "google/gemma-4-31b-it:free", string.Empty)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter");
        result.Error.Should().NotContain("Ollama");
    }

    [Fact]
    public async Task ObserveAsync_TransportAnswersNothing_NamesTheProviderThatSaidNothing()
    {
        var tokenStore = new Mock<IOpenRouterTokenStore>();
        tokenStore.Setup(s => s.LoadAsync()).ReturnsAsync("sk-or");
        var sut = CreateSut((_, _, _, _, _, _) => Task.FromResult("   "), tokenStore);

        var result = await sut.ObserveAsync(Request(
            new LlmTarget(ModelProvider.OpenRouter, "google/gemma-4-31b-it:free", string.Empty)));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("OpenRouter returned an empty image observation");
    }

    [Fact]
    public async Task ObserveAsync_LocalWithNoModel_RefusesRatherThanBorrowingSomething()
    {
        // The end of the old resolution chain was LoadOllamaModel(): the model the generate button renders
        // WITH, which cannot answer a chat request about a picture. The settings store is gone from this
        // service, so there is nothing left to borrow — this pins that the refusal still happens, in words,
        // and names the server the user has to pick from.
        var called = false;
        var sut = CreateSut((_, _, _, _, _, _) =>
        {
            called = true;
            return Task.FromResult("must never run");
        });

        var result = await sut.ObserveAsync(Request(new LlmTarget(ModelProvider.Local, string.Empty, "http://host:11434")));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("No local Ollama model selected");
        called.Should().BeFalse("no model call is made against a model that was never picked");
    }

    [Fact]
    public void ExtractErrorMessage_OpenRouterMetadataRaw_IsIncluded()
    {
        var message = VisionObservationService.ExtractErrorMessage(
            """{"error":{"message":"Provider returned error","metadata":{"raw":"google/gemma-4-31b-it:free is temporarily rate-limited upstream"}}}""");

        message.Should().Contain("Provider returned error");
        message.Should().Contain("temporarily rate-limited upstream");
    }

    private static VisionObservationRequest Request(LlmTarget target) =>
        new(target, "data:image/png;base64,abcd", "ref.png");

    /// <param name="tokenStore">
    /// Pass the same mock the test configured. There is deliberately no settings-store parameter: this
    /// service used to take one and go and read the model and the server address out of it, which is how
    /// the model on screen and the model called became two different answers.
    /// </param>
    private VisionObservationService CreateSut(
        VisionObservationService.ObservationCompletion completion,
        Mock<IOpenRouterTokenStore>? tokenStore = null) =>
        new(
            NullLogger<VisionObservationService>.Instance,
            Mock.Of<IHttpClientFactory>(),
            completion,
            tokenStore?.Object,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Task<Stream> FakeBundledAsset(string assetName) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("BUNDLED OBSERVATION PROMPT")));
}
