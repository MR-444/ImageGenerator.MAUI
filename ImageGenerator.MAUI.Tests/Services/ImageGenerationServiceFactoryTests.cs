using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Services;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services;

public class ImageGenerationServiceFactoryTests
{
    private readonly Mock<IOpenAiImageGenerationService> _openAi = new();
    private readonly Mock<IReplicateImageGenerationService> _replicate = new();
    private readonly ImageGenerationServiceFactory _sut;

    public ImageGenerationServiceFactoryTests()
    {
        _sut = new ImageGenerationServiceFactory(_openAi.Object, _replicate.Object);
    }

    [Fact]
    public async Task GenerateImageAsync_OpenAiModel_RoutesToOpenAi()
    {
        var parameters = new ImageGenerationParameters { Model = ModelConstants.OpenAI.GptImage1 };
        var expected = new GeneratedImage { ImageDataBase64 = "x", Message = "ok" };
        _openAi.Setup(x => x.GenerateImageAsync(parameters, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _sut.GenerateImageAsync(parameters);

        result.Should().BeSameAs(expected);
        _openAi.Verify(x => x.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()), Times.Once);
        _replicate.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11)]
    [InlineData(ModelConstants.Flux.Pro11Ultra)]
    [InlineData(ModelConstants.Flux.Klein4b)]
    [InlineData(ModelConstants.Flux.Flex2)]
    [InlineData(ModelConstants.Flux.Pro2)]
    [InlineData(ModelConstants.Flux.Max2)]
    [InlineData(ModelConstants.Google.NanoBanana2)]
    public async Task GenerateImageAsync_ReplicateModel_RoutesToReplicate(string model)
    {
        var parameters = new ImageGenerationParameters { Model = model };
        var expected = new GeneratedImage { ImageDataBase64 = "y", Message = "ok" };
        _replicate.Setup(x => x.GenerateImageAsync(parameters, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _sut.GenerateImageAsync(parameters);

        result.Should().BeSameAs(expected);
        _replicate.Verify(x => x.GenerateImageAsync(parameters, It.IsAny<CancellationToken>()), Times.Once);
        _openAi.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("openai/gpt-image-1.5")]
    [InlineData("black-forest-labs/flux-2")]
    [InlineData("stability-ai/sdxl")]
    public async Task GenerateImageAsync_DynamicReplicatePath_RoutesToReplicate(string model)
    {
        var parameters = new ImageGenerationParameters { Model = model };
        var expected = new GeneratedImage { ImageDataBase64 = "z", Message = "ok" };
        _replicate.Setup(x => x.GenerateImageAsync(parameters, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var result = await _sut.GenerateImageAsync(parameters);

        result.Should().BeSameAs(expected);
        _openAi.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GenerateImageAsync_MalformedModel_Throws()
    {
        var parameters = new ImageGenerationParameters { Model = "not-a-path" };

        var act = () => _sut.GenerateImageAsync(parameters);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*not-a-path*");
    }
}
