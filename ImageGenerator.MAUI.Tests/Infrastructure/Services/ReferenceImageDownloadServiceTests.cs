using System.Net;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.TestSupport;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class ReferenceImageDownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_ImageUrl_ReturnsBytes()
    {
        var sut = new ReferenceImageDownloadService(new StubHttpClientFactory(
            new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
                {
                    Headers = { ContentType = new("image/png") }
                }
            }))));

        var result = await sut.DownloadAsync(new Uri("https://example.test/ref.png"), 20 * 1024 * 1024);

        result.Success.Should().BeTrue();
        result.FileName.Should().Be("ref.png");
        result.Bytes.Should().Equal([1, 2, 3, 4]);
    }

    [Fact]
    public async Task DownloadAsync_UntypedImageUrl_SniffsImageBytes()
    {
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];
        var sut = new ReferenceImageDownloadService(new StubHttpClientFactory(
            new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(jpegBytes)
            }))));

        var result = await sut.DownloadAsync(new Uri("https://example.test/media?id=123"), 20 * 1024 * 1024);

        result.Success.Should().BeTrue();
        result.FileName.Should().Be("browser-reference.jpg");
        result.Bytes.Should().Equal(jpegBytes);
    }

    [Fact]
    public async Task DownloadAsync_NonImageContent_Fails()
    {
        var sut = new ReferenceImageDownloadService(new StubHttpClientFactory(
            new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("not image")
                {
                    Headers = { ContentType = new("text/html") }
                }
            }))));

        var result = await sut.DownloadAsync(new Uri("https://example.test/page"), 20 * 1024 * 1024);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("did not return an image");
    }

    [Fact]
    public async Task DownloadAsync_OversizedImage_FailsBeforeReadingBody()
    {
        var content = new ByteArrayContent([1, 2, 3, 4])
        {
            Headers =
            {
                ContentType = new("image/png"),
                ContentLength = 21 * 1024 * 1024
            }
        };
        var sut = new ReferenceImageDownloadService(new StubHttpClientFactory(
            new HttpClient(new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            }))));

        var result = await sut.DownloadAsync(new Uri("https://example.test/ref.png"), 20 * 1024 * 1024);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("larger than 20 MB");
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
