using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// The coordinator on its own, with no generator around it: everything it tells its owner goes
/// through events. The flow through the generator's parameters is covered in GeneratorViewModelTests.
/// </summary>
public class TokenCoordinatorTests
{
    private readonly Mock<IApiTokenStore> _replicate = new();
    private readonly Mock<IPollinationsTokenStore> _pollinations = new();

    private TokenCoordinator Create(IOpenRouterTokenStore? openRouter = null) => new(
        _replicate.Object,
        _pollinations.Object,
        new Mock<IComfyUiAuthStore>().Object,
        new Mock<ICivitaiTokenStore>().Object,
        new Mock<IAnthropicTokenStore>().Object,
        openRouter,
        new Mock<ICivitaiPostingService>().Object);

    [Fact]
    public async Task LoadAll_RaisesOnlyTheTwoParameterTokens()
    {
        _replicate.Setup(s => s.LoadAsync()).ReturnsAsync("r8_saved");
        _pollinations.Setup(s => s.LoadAsync()).ReturnsAsync("poll_saved");
        var sut = Create();
        string? replicate = null, pollinations = null;
        sut.ReplicateTokenChanged += v => replicate = v;
        sut.PollinationsTokenChanged += v => pollinations = v;

        await sut.LoadAllAsync();

        replicate.Should().Be("r8_saved");
        pollinations.Should().Be("poll_saved");
    }

    [Fact]
    public void ForgetSelected_ClearsTheRow_AndReportsItThroughStatusRaised()
    {
        var sut = Create();
        sut.Providers[0].Value = "r8_typed";
        (string Message, StatusKind Kind)? status = null;
        string? replicate = null;
        sut.StatusRaised += (m, k) => status = (m, k);
        sut.ReplicateTokenChanged += v => replicate = v;

        sut.ForgetSelectedCommand.Execute(null);

        replicate.Should().BeEmpty();
        _replicate.Verify(s => s.Forget(), Times.Once);
        status.Should().Be(("Replicate token cleared from secure storage.", StatusKind.Info));
    }

    [Fact]
    public void OpenRouterRow_AppearsOnlyWithItsStore()
    {
        Create().Providers.Select(p => p.Key).Should().NotContain("openrouter");
        Create(new Mock<IOpenRouterTokenStore>().Object).Providers.Select(p => p.Key)
            .Should().EndWith("openrouter");
    }
}
