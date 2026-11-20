using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using ResizeMode = SixLabors.ImageSharp.Processing.ResizeMode;
// Image is also Microsoft.Maui.Controls.Image, which the project's global usings bring in.
using RgbaImage = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using Size = SixLabors.ImageSharp.Size;

namespace ImageGenerator.MAUI.Infrastructure.Imaging;

/// <summary>
/// The one place that turns an image into bytes worth putting on the wire. Every image this app
/// sends out — a model's input image, a CivitAI post's media, the reference image pasted on the
/// Idea page — arrives here, because all three had the same failure: an 8000×6000, 36 MB phone
/// photo goes out as-is, base64 turns it into a ~48 MB body, and the endpoint answers with an
/// HTTP 413, a dropped connection or a timeout that looks like nothing in particular.
///
/// What it does, in order:
///  - empty input is an error;
///  - a file already inside <see cref="ImagePayloadLimits.MaxBytes"/> and
///    <see cref="ImagePayloadLimits.MaxDimension"/>, upright (no non-default EXIF orientation),
///    single-frame, and in a format the destination is known to take, is handed back
///    byte-identical. No decode, no second lossy generation, no surprise for a caller that wanted
///    its own file back;
///  - anything else is decoded (downsampled at decode time when over the dimension cap, and
///    capped to its first frame), straightened (plenty of endpoints read the pixels and ignore the
///    EXIF orientation tag, which sends the model a sideways photo), stripped of the EXIF and XMP
///    tags that carry GPS and device details no destination needs, and re-encoded to fit —
///    PNG while the image has a pixel that is not fully opaque, JPEG otherwise, because
///    flattening transparency onto a JPEG's black background invents content the user never had;
///  - resolution is given up before anything else is thrown away, and when even the smallest step
///    cannot reach the budget the caller is told so in words it can act on, rather than sending
///    something the endpoint will reject.
///
/// Animated sources are reduced to their first frame, which is what a single-frame encoder and
/// every receiving model would show anyway. A file nothing can decode is refused rather than sent
/// off to fail somewhere less legible — unless the destination reads more formats than this app
/// does, in which case a small one of its own kind is still worth uploading.
///
/// Bytes are counted before base64, which adds about a third on top.
/// </summary>
public static class ImagePayloadNormalizer
{
    /// <summary>Message for bytes nothing can decode. Names what the app can take, in plain words.</summary>
    private const string UndecodableMessage =
        "Unsupported image format — use PNG, JPEG, WebP, GIF, BMP, or TIFF.";

    private const string OutOfMemoryMessage =
        "The image is too large to process in memory — resize it before importing.";

    /// <summary>
    /// Long edges tried after the limit's own, biggest first, down to the smallest that still
    /// carries an image a model can read. Nothing smaller is attempted: a file that still does not
    /// fit is reported, not ground into something the user did not ask for.
    /// </summary>
    private static readonly int[] ShrinkSteps = [1536, 1024, 768];

    /// <summary>
    /// <see cref="Prepare"/> off the UI thread: decoding a 36 MB photo and re-encoding it is real
    /// work, and it lands exactly when the user drops or pastes a file.
    /// </summary>
    public static Task<ImagePayload> PrepareAsync(
        byte[] bytes,
        ImagePayloadLimits limits,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Prepare(bytes, limits, cancellationToken), cancellationToken);

    /// <summary>
    /// Returns the bytes to send with a one-line note saying what was done to them (null when
    /// nothing was), or an error. Exactly one of Bytes/Error on the result is non-null.
    /// </summary>
    public static ImagePayload Prepare(
        byte[] bytes,
        ImagePayloadLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (limits.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "MaxBytes must be positive.");
        }

        if (bytes.Length == 0)
        {
            return ImagePayload.Fail("The image was empty.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Fast-path pass-through when the destination accepts unknown formats and imposes no
        // dimension cap (e.g. CivitAI uploads under 10 MiB): keep caller bytes & metadata intact
        // without even touching Image.Identify.
        if (bytes.Length <= limits.MaxBytes && limits is { RequiresKnownFormat: false, MaxDimension: null })
        {
            return ImagePayload.Passthrough(bytes);
        }

        ImageInfo? info;
        try
        {
            info = SixLabors.ImageSharp.Image.Identify(bytes);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException
                                   or InvalidImageContentException
                                   or ImageProcessingException)
        {
            return !limits.RequiresKnownFormat && bytes.Length <= limits.MaxBytes
                ? ImagePayload.Passthrough(bytes)
                : ImagePayload.Fail(UndecodableMessage);
        }
        catch (OutOfMemoryException)
        {
            return ImagePayload.Fail(OutOfMemoryMessage);
        }

        var orientation = ReadExifOrientation(info);
        var (sourceWidth, sourceHeight) = SwapsAxes(orientation)
            ? (info.Height, info.Width)
            : (info.Width, info.Height);

        var overDimensionCap = limits.MaxDimension is { } cap && (sourceWidth > cap || sourceHeight > cap);
        var needsTransform = NeedsOrientation(orientation) || info.FrameCount > 1;
        var isKnownPassableFormat =
            info.Metadata.DecodedImageFormat is PngFormat or JpegFormat
            || PngTextChunkWriter.IsPng(bytes)
            || IsJpeg(bytes);

        // Pass-through: under the byte budget, under the dimension cap, upright, single-frame, and
        // in a format the destination is known to take. Costs nothing, and keeps the caller's bytes.
        if (bytes.Length <= limits.MaxBytes && !overDimensionCap && !needsTransform
            && (isKnownPassableFormat || !limits.RequiresKnownFormat))
        {
            return ImagePayload.Passthrough(bytes);
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Ask decoders to stop after frame 1 and downsample directly during decode when the
            // image exceeds MaxDimension. Only set TargetSize when strictly larger than the cap:
            // TargetSize behaves like ResizeMode.Max and would otherwise upscale small images.
            var decoderOptions = new DecoderOptions
            {
                MaxFrames = 1,
                TargetSize = overDimensionCap && limits.MaxDimension is { } targetCap
                    ? new Size(targetCap, targetCap)
                    : null
            };

            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(decoderOptions, bytes);

            // Belt-and-suspenders: DecoderOptions.MaxFrames is a hint for decoders that don't
            // honor it natively, so trim any extra frames in place.
            while (image.Frames.Count > 1)
            {
                image.Frames.RemoveFrame(image.Frames.Count - 1);
            }

            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            // JPEGs cannot represent transparency, so skip the pixel scan when the source is JPEG.
            var lossless = info.Metadata.DecodedImageFormat is not JpegFormat && HasTransparency(image);

            byte[]? smallest = null;
            foreach (var rung in Rungs(limits))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var attempt = EncodeInPlace(image, rung, limits, lossless);
                if (attempt.Bytes.Length <= limits.MaxBytes)
                {
                    return ImagePayload.Reencoded(attempt.Bytes, Note(sourceWidth, sourceHeight, attempt, bytes));
                }

                smallest = attempt.Bytes;
            }

            return ImagePayload.Fail(
                $"{SizeOf(bytes.Length)} is over the {SizeOf(limits.MaxBytes)} allowed, and re-encoding it left " +
                $"{SizeOf(smallest!.Length)} — resize or flatten it first.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutOfMemoryException)
        {
            return ImagePayload.Fail(OutOfMemoryMessage);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException
                                   or InvalidImageContentException
                                   or ImageProcessingException)
        {
            return ImagePayload.Fail(UndecodableMessage);
        }
    }

    private static ushort ReadExifOrientation(ImageInfo info)
    {
        if (info.Metadata.ExifProfile?.TryGetValue(ExifTag.Orientation, out var value) == true)
        {
            return value.Value;
        }

        return 1;
    }

    private static bool NeedsOrientation(ushort orientation) => orientation is >= 2 and <= 8;

    private static bool SwapsAxes(ushort orientation) => orientation is >= 5 and <= 8;

    /// <summary>
    /// The long edges to try, in strictly decreasing order. A limit with no cap (a CivitAI upload,
    /// where the resolution is the whole point of having upscaled) gets one attempt at its own
    /// pixels and must take the failure if that does not reach the budget.
    /// </summary>
    private static int?[] Rungs(ImagePayloadLimits limits)
    {
        if (limits.MaxDimension is not { } cap)
        {
            return [null];
        }

        List<int?> rungs =
        [
            cap,
            .. ShrinkSteps.Where(step => step < cap).Select(step => (int?)step)
        ];
        return [.. rungs];
    }

    /// <summary>What one encode attempt produced, at what size, and in which encoder.</summary>
    private sealed record EncodedPayload(byte[] Bytes, int Width, int Height, bool Lossless);

    /// <summary>
    /// Mutates the working image down in place if its longest edge exceeds <paramref name="maxEdge"/>
    /// and encodes it. Safe because rungs are strictly decreasing, and avoids cloning a full
    /// multi-megapixel pixel buffer on every shrink step.
    /// </summary>
    private static EncodedPayload EncodeInPlace(
        RgbaImage image,
        int? maxEdge,
        ImagePayloadLimits limits,
        bool lossless)
    {
        // Only resize when strictly larger than the edge: ResizeMode.Max grows a small image to
        // fill the box, and a 2×2 WebP that merely needs to become a PNG must stay 2×2.
        if (maxEdge is { } edge && Math.Max(image.Width, image.Height) > edge)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(edge, edge),
                Mode = ResizeMode.Max
            }));
        }

        using var buffer = new MemoryStream(Math.Min(limits.MaxBytes, 64 * 1024));
        image.Save(buffer, lossless
            ? new PngEncoder()
            : new JpegEncoder { Quality = limits.JpegQuality });
        return new EncodedPayload(buffer.ToArray(), image.Width, image.Height, lossless);
    }

    private static bool HasTransparency(RgbaImage image)
    {
        var found = false;
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height && !found; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A != byte.MaxValue)
                    {
                        found = true;
                        return;
                    }
                }
            }
        });
        return found;
    }

    /// <summary>
    /// The one line the user gets about their file: what it was, what is going out instead. The
    /// dimensions are only named when they changed, so a file that merely changed container reads
    /// as "1.2 MB → 480 KB JPEG" rather than pretending to have been shrunk.
    /// </summary>
    private static string Note(int sourceWidth, int sourceHeight, EncodedPayload encoded, byte[] sourceBytes)
    {
        var resized = sourceWidth == encoded.Width && sourceHeight == encoded.Height
            ? string.Empty
            : $"{sourceWidth}×{sourceHeight} → {encoded.Width}×{encoded.Height}, ";
        return $"{resized}{SizeOf(sourceBytes.Length)} → {SizeOf(encoded.Bytes.Length)} " +
               $"{(encoded.Lossless ? "PNG" : "JPEG")}";
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;

    private static string SizeOf(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (double)(1024 * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B"
    };
}

/// <summary>
/// What "small enough" means for one destination. The budgets differ because what breaks differs;
/// the way a payload reaches them is the same and lives in <see cref="ImagePayloadNormalizer"/>.
/// </summary>
/// <param name="MaxBytes">Encoded budget, counted before base64.</param>
/// <param name="MaxDimension">
/// Longest edge to send. Null keeps every pixel and makes bytes the only budget, which is what a
/// caller wants when the resolution is the point of the image.
/// </param>
/// <param name="JpegQuality">Quality of the lossy re-encode; ignored while the image is lossless.</param>
/// <param name="RequiresKnownFormat">
/// True when the destination understands nothing this class cannot encode, so a file that cannot be
/// decoded is refused here rather than forwarded to fail somewhere less legible. False where the
/// destination decodes more formats than the app does: an unreadable-but-small file is its business,
/// not ours, and only gets refused when it is also over budget — the one case where reading it is
/// the only way to make it smaller.
/// </param>
public readonly record struct ImagePayloadLimits(
    int MaxBytes,
    int? MaxDimension = null,
    int JpegQuality = 85,
    bool RequiresKnownFormat = true)
{
    /// <summary>
    /// An image attached to a generation model. Hosted models rescale to 1024–2048px internally,
    /// so the long edge here gives up nothing the model would have used; the byte budget is what
    /// keeps a dozen input images from becoming a dozen megabytes of JSON.
    /// </summary>
    public static readonly ImagePayloadLimits ModelInput = new(2 * 1024 * 1024, MaxDimension: 2048);

    /// <summary>
    /// An image a vision model looks at: half the long edge of <see cref="ModelInput"/>, twice
    /// the bytes. Tighter pixels because a tiled vision model slices the image into billed context
    /// tokens and 1024px keeps even a 2×2 tile grid well inside an 8,192-token window; looser bytes
    /// because it takes them as an inline string, and because Ollama's loader only decodes JPEG and
    /// PNG at all — a browser WebP has to be transcoded before it gets there, and transcode room
    /// has to exist.
    /// </summary>
    public static readonly ImagePayloadLimits Vision = new(4 * 1024 * 1024, MaxDimension: 1024);

    /// <summary>
    /// A CivitAI post's media. CivitAI's upload_image rejects anything whose decoded size passes
    /// 10 MiB (observed: an upscaled PNG at 13,246,392 bytes), and it displays the file at its own
    /// resolution — so this is a byte budget only, never a resize, at a quality that keeps an
    /// upscaled render looking upscaled. It also decodes more than this app does, so a small file
    /// the app cannot read is still worth uploading.
    /// </summary>
    public static readonly ImagePayloadLimits CivitAiUpload =
        new(10 * 1024 * 1024, JpegQuality: 92, RequiresKnownFormat: false);
}

/// <summary>
/// Outcome of <see cref="ImagePayloadNormalizer.Prepare"/>. Exactly one of Bytes/Error is non-null;
/// Note is the user-facing line saying what was done to the image, null when nothing was.
/// </summary>
public sealed record ImagePayload(byte[]? Bytes, string? Note, string? Error)
{
    /// <summary>The caller's own bytes, untouched.</summary>
    public static ImagePayload Passthrough(byte[] bytes) => new(bytes, null, null);

    /// <summary>Different bytes than went in; Note says how.</summary>
    public static ImagePayload Reencoded(byte[] bytes, string note) => new(bytes, note, null);

    public static ImagePayload Fail(string error) => new(null, null, error);
}
