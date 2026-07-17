using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace ImageGenerator.MAUI.Infrastructure.Imaging;

/// <summary>
/// Normalizes a reference image to a format every vision backend can decode. Ollama's image
/// loader only reads JPEG and PNG — a WebP copied out of a browser fails server-side with an
/// opaque HTTP 400 ("Failed to load image or audio file") — and the hosted APIs care that the
/// bytes match the declared media type. Sniffing the actual bytes (never the file name — a
/// ".jfif" is plain JPEG, and a ".png" from a browser may really be WebP) makes JPEG/PNG a
/// zero-copy pass-through and transcodes everything ImageSharp can read to PNG.
/// </summary>
public static class VisionImageNormalizer
{
    /// <summary>
    /// Pass-through for JPEG/PNG, PNG transcode for other decodable formats (WebP, GIF, BMP,
    /// TIFF), or a user-facing error when the bytes aren't a decodable image (e.g. AVIF).
    /// Exactly one of Bytes/Error is non-null.
    /// </summary>
    public static (byte[]? Bytes, string? Error) Normalize(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return (null, "The image was empty.");
        }

        if (PngTextChunkWriter.IsPng(bytes) || IsJpeg(bytes))
        {
            return (bytes, null);
        }

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(bytes);
            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return (ms.ToArray(), null);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
        {
            return (null, "Unsupported image format — use PNG, JPEG, WebP, GIF, BMP, or TIFF.");
        }
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
