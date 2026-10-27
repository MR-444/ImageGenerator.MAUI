using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Processing;

namespace ImageGenerator.MAUI.Infrastructure.Imaging;

/// <summary>
/// Normalizes a reference image to a format and resolution every vision backend can decode.
/// Ollama's image loader only reads JPEG and PNG — a WebP copied out of a browser fails
/// server-side with an opaque HTTP 400 ("Failed to load image or audio file") — and vision models
/// slice large images into tiles that rapidly exhaust the context window (16 tiles = 16,384 tokens).
/// Images exceeding <see cref="MaxDimension"/> are proportionally downscaled to fit safely within
/// an 8,192 token context window.
/// </summary>
public static class VisionImageNormalizer
{
    /// <summary>
    /// 1024px ensures tiled vision models (LLaMA-Vision, MiniCPM-V, etc.) produce at most a 2x2
    /// tile grid (~4,096 tokens), fitting safely within an 8,192-token context limit (NumCtx)
    /// with plenty of room left for system instructions and generation.
    /// </summary>
    public const int MaxDimension = 1024;

    /// <summary>
    /// Pass-through for JPEG/PNG within max dimensions, downscaling and/or transcoding for
    /// other decodable formats (WebP, GIF, BMP, TIFF) or oversize images, or a user-facing
    /// error when the bytes aren't decodable.
    /// Exactly one of Bytes/Error is non-null.
    /// </summary>
    public static (byte[]? Bytes, string? Error) Normalize(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return (null, "The image was empty.");
        }

        var isPng = PngTextChunkWriter.IsPng(bytes);
        var isJpeg = IsJpeg(bytes);

        // Fast path: if already JPEG/PNG and within dimensions, keep zero-copy pass-through.
        // Image.Identify only reads the header chunks without decoding the full pixel buffer.
        if (isPng || isJpeg)
        {
            try
            {
                var info = SixLabors.ImageSharp.Image.Identify(bytes);
                if (info is { Width: <= MaxDimension, Height: <= MaxDimension })
                {
                    return (bytes, null);
                }
            }
            catch
            {
                // If header identification fails, fall through to full Load.
            }
        }

        try
        {
            using var image = SixLabors.ImageSharp.Image.Load(bytes);

            // Proportionally downscale if either dimension exceeds MaxDimension
            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(MaxDimension, MaxDimension),
                    Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
                }));
            }

            using var ms = new MemoryStream();
            if (isJpeg)
            {
                image.Save(ms, new JpegEncoder { Quality = 85 });
            }
            else
            {
                image.Save(ms, new PngEncoder());
            }

            return (ms.ToArray(), null);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or ImageProcessingException)
        {
            return (null, "Unsupported image format — use PNG, JPEG, WebP, GIF, BMP, or TIFF.");
        }
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}
