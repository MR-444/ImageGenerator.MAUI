using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class ReferenceImageDownloadService : IReferenceImageDownloadService
{
    public const string HttpClientName = "reference-image-import";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".gif"
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public ReferenceImageDownloadService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<ReferenceImageDownloadResult> DownloadAsync(
        Uri uri,
        long maxBytes,
        CancellationToken ct = default)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return ReferenceImageDownloadResult.Fail("Only http and https image URLs can be imported.");

        using var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Options.Set(RemoteHttpLoggingHandler.PurposeKey, "browser reference image download");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return ReferenceImageDownloadResult.Fail($"Image URL returned HTTP {(int)response.StatusCode}.");

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength > maxBytes)
            return ReferenceImageDownloadResult.Fail($"Image is larger than {FormatBytes(maxBytes)}.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;

            if (memory.Length + read > maxBytes)
                return ReferenceImageDownloadResult.Fail($"Image is larger than {FormatBytes(maxBytes)}.");

            memory.Write(buffer, 0, read);
        }

        if (memory.Length == 0)
            return ReferenceImageDownloadResult.Fail("The image URL returned an empty file.");

        var bytes = memory.ToArray();
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var detectedExtension = DetectImageExtension(bytes);
        if (!IsImageMediaType(mediaType) && detectedExtension is null)
            return ReferenceImageDownloadResult.Fail("The URL did not return an image.");

        return ReferenceImageDownloadResult.Ok(FileNameFor(uri, mediaType, detectedExtension), bytes);
    }

    private static bool IsImageMediaType(string? mediaType) =>
        mediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true;

    private static string? DetectImageExtension(byte[] bytes)
    {
        if (bytes is [0xFF, 0xD8, 0xFF, ..])
            return ".jpg";

        if (bytes is [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..])
            return ".png";

        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
            return ".webp";

        if (bytes.Length >= 6
            && bytes[0] == (byte)'G'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'8'
            && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9')
            && bytes[5] == (byte)'a')
            return ".gif";

        return null;
    }

    private static string FileNameFor(Uri uri, string? mediaType, string? detectedExtension)
    {
        var name = Path.GetFileName(uri.LocalPath);
        if (!string.IsNullOrWhiteSpace(name) && ImageExtensions.Contains(Path.GetExtension(name)))
            return name;

        var extension = mediaType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => detectedExtension ?? ".img"
        };
        return "browser-reference" + extension;
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024
            ? $"{bytes / (1024 * 1024)} MB"
            : $"{bytes} bytes";
}
