namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

public interface IReferenceImageDownloadService
{
    Task<ReferenceImageDownloadResult> DownloadAsync(
        Uri uri,
        long maxBytes,
        CancellationToken ct = default);
}

public sealed record ReferenceImageDownloadResult(
    bool Success,
    string? FileName,
    byte[]? Bytes,
    string? Error)
{
    public static ReferenceImageDownloadResult Ok(string fileName, byte[] bytes) =>
        new(true, fileName, bytes, null);

    public static ReferenceImageDownloadResult Fail(string error) =>
        new(false, null, null, error);
}
