using System.Net;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiVramServiceTests
{
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly Mock<IComfyUiAuthStore> _authStore = new();
    private readonly List<HttpRequestMessage> _requests = [];
    private readonly List<string> _bodies = [];

    public ComfyUiVramServiceTests()
    {
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);
    }

    private ComfyUiVramService BuildService(Func<HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Loose);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) =>
            {
                _requests.Add(req);
                _bodies.Add(req.Content?.ReadAsStringAsync().Result ?? string.Empty);
            })
            .ReturnsAsync((HttpRequestMessage _, CancellationToken _) => respond());

        var client = new HttpClient(handler.Object);
        return new ComfyUiVramService(
            new StubHttpClientFactory(client), _uiState.Object, _authStore.Object,
            NullLogger<ComfyUiVramService>.Instance);
    }

    [Fact]
    public async Task TryFreeAsync_PostsFreeWithUnloadModelsAndFreeMemory()
    {
        var service = BuildService(() => new HttpResponseMessage(HttpStatusCode.OK));

        await service.TryFreeAsync();

        _requests.Should().ContainSingle();
        _requests[0].Method.Should().Be(HttpMethod.Post);
        _requests[0].RequestUri!.AbsoluteUri.Should().Be("http://test-host:8188/free");
        _bodies[0].Should().Contain("\"unload_models\":true").And.Contain("\"free_memory\":true");
    }

    [Fact]
    public async Task TryFreeAsync_ServerThrows_DoesNotThrow()
    {
        var service = BuildService(() => throw new HttpRequestException("server down"));

        var act = async () => await service.TryFreeAsync();

        await act.Should().NotThrowAsync("freeing VRAM is best-effort and must never surface an error");
    }

    [Fact]
    public async Task TryFreeAsync_InvalidServerUrl_SkipsWithoutRequest()
    {
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("not a url");
        var service = BuildService(() => new HttpResponseMessage(HttpStatusCode.OK));

        await service.TryFreeAsync();

        _requests.Should().BeEmpty();
    }
}
