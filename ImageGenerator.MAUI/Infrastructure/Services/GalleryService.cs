using System.Runtime.CompilerServices;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
// `Image` collides with Microsoft.Maui.Controls.Image under the implicit MAUI usings, so
// reach for ImageSharp's static factory through its full namespace at every call site.
// The `using SixLabors.ImageSharp;` above is still required: GetPngMetadata is an extension
// method declared on `MetadataExtensions` in that namespace, not in Formats.Png.

namespace ImageGenerator.MAUI.Infrastructure.Services;

public class GalleryService : IGalleryService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    // 2 s in-flight guard. SaveImageWithMetadataAsync writes via image.SaveAsync() — not an
    // atomic temp+rename — so a recently-written file may still be partial. Skipping freshly-
    // touched files keeps half-decoded tiles out of the grid; the watcher refresh that follows
    // the save completing will pick them up.
    private static readonly TimeSpan InFlightGuard = TimeSpan.FromSeconds(2);

    private readonly Func<DateTime> _clock;
    private readonly string _rootDirectory;

    public GalleryService(string? rootDirectory = null, Func<DateTime>? clock = null)
    {
        _rootDirectory = rootDirectory ?? OutputPaths.GeneratedImagesDirectory;
        _clock = clock ?? (() => DateTime.Now);
    }

    public async IAsyncEnumerable<GalleryItem> EnumerateAsync([EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!Directory.Exists(_rootDirectory))
        {
            yield break;
        }

        // Materialize once so we can sort. The output dir is small (hundreds, maybe low
        // thousands) — virtualizing the walk would only matter for tens of thousands.
        var paths = Directory
            .EnumerateFiles(_rootDirectory)
            .Where(p => ImageExtensions.Contains(Path.GetExtension(p)))
            .ToList();

        // Sort by filename desc — BuildFileName's "yyyyMMdd_HHmmss_..." prefix makes lex
        // comparison equal chronological. Cheaper than File.GetCreationTime per entry.
        paths.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));

        var cutoff = _clock() - InFlightGuard;

        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();

            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists) continue;
                if (info.LastWriteTime > cutoff) continue;
            }
            catch (IOException)
            {
                // File vanished between enumerate and stat. Skip — the next refresh handles it.
                continue;
            }

            yield return new GalleryItem(
                FilePath: info.FullName,
                FileName: info.Name,
                CreatedAt: info.CreationTime,
                FileSize: info.Length,
                Metadata: null);

            // Yield to the scheduler so a UI-thread caller can paint a row of tiles before
            // the rest of the dir walk finishes. Cheap; only matters on big dirs.
            await Task.Yield();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> ReadMetadataAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return null;

        string? raw;
        try
        {
            // IdentifyAsync reads only the headers / metadata blocks — no pixel decode, no
            // SIMD-allocated working buffers. The same Comment chunk and EXIF UserComment
            // accessors are exposed through ImageInfo.Metadata as on a fully-decoded Image.
            var info = await SixLabors.ImageSharp.Image.IdentifyAsync(filePath, ct);
            raw = ExtractCommentText(info, Path.GetExtension(filePath));
        }
        catch (Exception)
        {
            // Corrupted file, unsupported encoder, partial download — treat as "no metadata".
            return null;
        }

        if (string.IsNullOrWhiteSpace(raw)) return null;

        return ParseLines(raw);
    }

    private static string? ExtractCommentText(SixLabors.ImageSharp.ImageInfo info, string extension)
    {
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            var pngMeta = info.Metadata.GetPngMetadata();
            return pngMeta.TextData.FirstOrDefault(t => t.Keyword == "Comment").Value;
        }

        // JPG and WebP both round-trip via EXIF UserComment in SaveImageWithMetadataAsync.
        var exif = info.Metadata.ExifProfile;
        if (exif is null) return null;
        return exif.TryGetValue(ExifTag.UserComment, out var value)
            ? value?.Value.Text
            : null;
    }

    private static IReadOnlyDictionary<string, string> ParseLines(string raw)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        // Split on Environment.NewLine to mirror string.Join(Environment.NewLine, lines) at the
        // write side. Falling back to LF handles the edge case of a metadata block authored on
        // a different platform — defensive, not strictly needed for app-written files.
        var lines = raw.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;
            var key = line[..colon].Trim();
            // Value can legally contain colons (timestamps, prompts mentioning "12:00"), so
            // only split on the *first* colon — substring after, then ltrim a single space.
            var value = line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];
            if (key.Length == 0) continue;
            dict[key] = value;
        }

        return dict;
    }
}
