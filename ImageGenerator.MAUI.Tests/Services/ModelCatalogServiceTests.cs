using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Models.Replicate;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;

namespace ImageGenerator.MAUI.Tests.Services;

public class ModelCatalogServiceTests : IDisposable
{
    private readonly Mock<IReplicateApi> _replicate = new();
    private readonly Mock<IOpenAiApi> _openAi = new();
    private readonly string _tempDir;
    private readonly ModelCatalogService _sut;

    public ModelCatalogServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ImageGenerator.MAUI.Tests", Guid.NewGuid().ToString("N"));
        _sut = new ModelCatalogService(_replicate.Object, _openAi.Object, _tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup — don't fail the test run on a locked file.
        }
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
    public async Task FetchAsync_FiltersReplicateToAllowlist()
    {
        _replicate.Setup(x => x.GetTextToImageCollectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReplicateCollectionResponse
            {
                Models =
                [
                    new() { Owner = "black-forest-labs", Name = "flux-2" },
                    new() { Owner = "openai", Name = "gpt-image-1.5" },
                    new() { Owner = "google", Name = "nano-banana-2" },
                    new() { Owner = "stability-ai", Name = "sdxl" },
                    new() { Owner = "bytedance", Name = "sdxl-lightning" }
                ]
            });
        _openAi.Setup(x => x.ListModelsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OpenAiModelsResponse { Data = [] });

        var result = await _sut.FetchAsync("token");

        result.Select(m => m.Value).Should().BeEquivalentTo(
            ["black-forest-labs/flux-2", "openai/gpt-image-1.5", "google/nano-banana-2"]);
        result.Single(m => m.Value == "openai/gpt-image-1.5").Provider.Should().Be("OpenAI (via Replicate)");
        result.Single(m => m.Value == "google/nano-banana-2").Provider.Should().Be("Google");
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

    [Fact]
    public async Task SaveCached_ThenLoadCached_RoundTripsEquivalentList()
    {
        var models = new List<ModelOption>
        {
            new("Flux 1.1 Pro", "black-forest-labs/flux-1.1-pro", "Black Forest Labs"),
            new("GPT Image 1.5", "openai/gpt-image-1.5", "OpenAI (via Replicate)"),
            new("Nano Banana 2", "google/nano-banana-2", "Google")
        };

        await _sut.SaveCachedAsync(models);
        var loaded = await _sut.LoadCachedAsync();

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(models);
    }

    [Fact]
    public async Task LoadCached_WhenFileMissing_ReturnsNull()
    {
        var result = await _sut.LoadCachedAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadCached_WhenFileCorrupt_ReturnsNullAndDoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "model-catalog.json"), "not-json{{{");

        var result = await _sut.LoadCachedAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveCached_OverwritesExistingFile()
    {
        var first = new List<ModelOption> { new("A", "owner/a", "Owner") };
        var second = new List<ModelOption>
        {
            new("B1", "owner/b1", "Owner"),
            new("B2", "owner/b2", "Owner")
        };

        await _sut.SaveCachedAsync(first);
        await _sut.SaveCachedAsync(second);
        var loaded = await _sut.LoadCachedAsync();

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(second);
    }
}
