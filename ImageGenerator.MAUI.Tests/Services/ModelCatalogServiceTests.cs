using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Models.Replicate;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services;

public class ModelCatalogServiceTests
{
    private readonly Mock<IReplicateApi> _replicate = new();
    private readonly Mock<IOpenAiApi> _openAi = new();
    private readonly ModelCatalogService _sut;

    public ModelCatalogServiceTests()
    {
        _sut = new ModelCatalogService(_replicate.Object, _openAi.Object);
    }

    [Fact]
    public async Task FetchAsync_EmptyToken_ReturnsEmptyWithoutCallingProviders()
    {
        var result = await _sut.FetchAsync(" ");

        result.Should().BeEmpty();
        _replicate.VerifyNoOtherCalls();
        _openAi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FetchAsync_BothProvidersSucceed_ReturnsConcatenatedModels()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicateCollectionResponse
            {
                Models =
                [
                    new() { Owner = "black-forest-labs", Name = "flux-2" },
                    new() { Owner = "openai", Name = "gpt-image-1.5" }
                ]
            });
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiModelsResponse
            {
                Data = [new() { Id = "gpt-image-1" }]
            });

        var result = await _sut.FetchAsync("token");

        result.Should().HaveCount(3);
        result.Select(m => m.Value).Should().Contain(
            ["black-forest-labs/flux-2", "openai/gpt-image-1.5", "openAI/gpt-image-1"]);
    }

    [Fact]
    public async Task FetchAsync_ReplicateFails_ReturnsOpenAiOnly()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401"));
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiModelsResponse { Data = [new() { Id = "gpt-image-1" }] });

        var result = await _sut.FetchAsync("token");

        result.Should().HaveCount(1);
        result[0].Provider.Should().Be("OpenAI");
    }

    [Fact]
    public async Task FetchAsync_BothFail_ReturnsEmpty()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401"));
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("401"));

        var result = await _sut.FetchAsync("token");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FetchAsync_FiltersReplicateToBlackForestLabsAndOpenAi()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicateCollectionResponse
            {
                Models =
                [
                    new() { Owner = "black-forest-labs", Name = "flux-2" },
                    new() { Owner = "openai", Name = "gpt-image-1.5" },
                    new() { Owner = "stability-ai", Name = "sdxl" },
                    new() { Owner = "google", Name = "imagen-3" },
                    new() { Owner = "bytedance", Name = "sdxl-lightning" }
                ]
            });
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiModelsResponse { Data = [] });

        var result = await _sut.FetchAsync("token");

        result.Select(m => m.Value).Should().BeEquivalentTo(
            ["black-forest-labs/flux-2", "openai/gpt-image-1.5"]);
        result.Single(m => m.Value == "openai/gpt-image-1.5").Provider.Should().Be("OpenAI (via Replicate)");
    }

    [Fact]
    public async Task FetchAsync_FiltersOpenAiToImageModels()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicateCollectionResponse { Models = [] });
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiModelsResponse
            {
                Data =
                [
                    new() { Id = "gpt-4-turbo" },
                    new() { Id = "text-embedding-3-small" },
                    new() { Id = "gpt-image-1" },
                    new() { Id = "gpt-image-1.5" },
                    new() { Id = "dall-e-3" },
                    new() { Id = "whisper-1" }
                ]
            });

        var result = await _sut.FetchAsync("token");

        result.Select(m => m.Display).Should().BeEquivalentTo(
            ["gpt-image-1", "gpt-image-1.5", "dall-e-3"]);
    }
}
