using System.Net;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiLoraServiceTests
{
    private readonly Mock<IUiStateStore> _uiState = new();
    private readonly Mock<IComfyUiAuthStore> _authStore = new();

    public ComfyUiLoraServiceTests()
    {
        _uiState.Setup(s => s.LoadComfyUiBaseUrl()).Returns("http://test-host:8188");
        _authStore.Setup(s => s.LoadAsync()).ReturnsAsync("Bearer secret");
    }

    private (ComfyUiLoraService Service, List<(Uri Uri, string? Auth)> Requests) Build(
        HttpStatusCode status, string body)
    {
        var requests = new List<(Uri, string?)>();
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((request, _) =>
                requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString())))
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = new StringContent(body)
            });

        var service = new ComfyUiLoraService(
            new StubHttpClientFactory(new HttpClient(handler.Object)),
            _uiState.Object,
            _authStore.Object,
            NullLogger<ComfyUiLoraService>.Instance);
        return (service, requests);
    }

    [Fact]
    public async Task GetLoraNames_UsesHostCatalogAndAuth_SortsAndDeduplicates()
    {
        var (service, requests) = Build(HttpStatusCode.OK,
            """["z.safetensors","Krea2\\portrait.safetensors","Z.safetensors",""]""");

        var names = await service.GetLoraNamesAsync();

        names.Should().Equal(@"Krea2\portrait.safetensors", "z.safetensors");
        requests.Should().ContainSingle();
        requests[0].Uri.AbsoluteUri.Should().Be("http://test-host:8188/models/loras");
        requests[0].Auth.Should().Be("Bearer secret");
    }

    [Fact]
    public async Task GetLoraNames_HostError_ThrowsActionableMessage()
    {
        var (service, _) = Build(HttpStatusCode.Unauthorized, "denied");

        var act = async () => await service.GetLoraNamesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*HTTP 401*LoRAs*");
    }

    [Fact]
    public async Task GetLoraNames_InvalidJson_ThrowsActionableMessage()
    {
        var (service, _) = Build(HttpStatusCode.OK, "not json");

        var act = async () => await service.GetLoraNamesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid LoRA catalog*");
    }
}
