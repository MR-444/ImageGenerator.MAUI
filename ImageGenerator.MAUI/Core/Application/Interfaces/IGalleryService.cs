using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

public interface IGalleryService
{
    // Streams items as they're discovered so the VM can render the first row of tiles
    // before the directory walk finishes. Sorted newest-first by filename.
    IAsyncEnumerable<GalleryItem> EnumerateAsync(CancellationToken ct = default);

    // Reads the metadata block embedded by ImageFileService.SaveImageWithMetadataAsync
    // (PNG: "Comment" text chunk; JPG/WebP: EXIF UserComment). Returns null when the
    // file has no recoverable metadata or can't be opened.
    Task<IReadOnlyDictionary<string, string>?> ReadMetadataAsync(string filePath, CancellationToken ct = default);
}
