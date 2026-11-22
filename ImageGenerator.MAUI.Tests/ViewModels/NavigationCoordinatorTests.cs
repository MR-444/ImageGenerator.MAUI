using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>The routes to the companion pages and what they hand over, without Shell.</summary>
public class NavigationCoordinatorTests
{
    private const string StructuredPrompt =
        "{\"high_level_description\":\"x\",\"compositional_deconstruction\":{\"background\":\"y\",\"elements\":[{\"type\":\"obj\",\"desc\":\"z\"}]}}";

    private readonly ImageGenerationParameters _parameters = new() { Prompt = StructuredPrompt };
    private readonly Mock<IGalleryService> _gallery = new();
    private readonly List<string> _routes = [];
    private (string Message, StatusKind Kind)? _status;

    private NavigationCoordinator Create(Func<string, Task>? navigate = null) => new(
        () => _parameters, _gallery.Object, (m, k) => _status = (m, k),
        NullLogger.Instance, navigate ?? (route => { _routes.Add(route); return Task.CompletedTask; }));

    [Fact]
    public async Task OpenMutationEngine_StructuredPrompt_HandsOverTheParsedCaption()
    {
        var sut = Create();

        await sut.OpenMutationEngineCommand.ExecuteAsync(null);

        _routes.Should().Equal("mutation-engine");
        sut.PendingMutationBase.Should().NotBeNull();
        sut.PendingMutationBase!.HighLevelDescription.Should().Be("x");
    }

    [Fact]
    public async Task OpenMutationEngine_PlainPrompt_NavigatesWithNoCaption()
    {
        _parameters.Prompt = "just words";
        var sut = Create();

        await sut.OpenMutationEngineCommand.ExecuteAsync(null);

        _routes.Should().Equal("mutation-engine");
        sut.PendingMutationBase.Should().BeNull("the mutation page reports the missing structure itself");
    }

    [Fact]
    public async Task MutateFromImage_AdoptsTheCaptionAsPrompt()
    {
        _parameters.Prompt = "old";
        _gallery.Setup(g => g.ReadMetadataAsync("a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string> { ["Prompt"] = StructuredPrompt });
        var sut = Create();

        await sut.MutateFromImageAsync("a.png");

        _parameters.Prompt.Should().Be(StructuredPrompt);
        sut.PendingMutationBase.Should().NotBeNull();
        _routes.Should().Equal("mutation-engine");
    }

    [Fact]
    public async Task MutateFromImage_NoCaption_WarnsAndStays()
    {
        _gallery.Setup(g => g.ReadMetadataAsync("a.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
        var sut = Create();

        await sut.MutateFromImageAsync("a.png");

        _routes.Should().BeEmpty();
        _status.Should().Be(("No caption found in this image.", StatusKind.Warning));
    }

    [Fact]
    public async Task NavigationFailure_IsReportedOnTheStatusLine()
    {
        var sut = Create(_ => throw new InvalidOperationException("no shell"));

        await sut.OpenIdeaToPromptCommand.ExecuteAsync(null);

        _status.Should().Be(("Couldn't open the prompt builder: no shell", StatusKind.Error));
    }
}
