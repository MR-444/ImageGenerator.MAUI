namespace ImageGenerator.MAUI.Core.Domain.Entities;

// Metadata stays null until lazy-loaded — enumerating a thousand-file directory must not
// open every PNG up front. Callers ask the service for metadata on demand (tile expand /
// hover) and the service decides whether to cache.
public sealed record GalleryItem(
    string FilePath,
    string FileName,
    DateTime CreatedAt,
    long FileSize,
    IReadOnlyDictionary<string, string>? Metadata = null);
