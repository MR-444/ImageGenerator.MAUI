using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;

/// <summary>
/// Yields the model-specific metadata lines that ImageFileService writes into the saved image
/// (PNG tEXt chunk or JPG/WebP EXIF UserComment). Common lines (Prompt/Seed/Dimensions/...) are
/// produced by ImageFileService itself; this interface only adds the model-specific extras.
/// </summary>
public interface IMetadataDescriber
{
    string ModelId { get; }
    IEnumerable<string> Lines(ImageGenerationParameters parameters);

    /// <summary>
    /// Inverse of <see cref="Lines"/>: reads this model's extra metadata keys back out of a
    /// parsed recipe (Remix from an image) and writes them onto <paramref name="parameters"/>.
    /// Co-located with <see cref="Lines"/> so the write/read pair can't drift. Parsing is
    /// defensive — a missing or unparseable key leaves the existing parameter value untouched.
    /// </summary>
    void Apply(ImageGenerationParameters parameters, IReadOnlyDictionary<string, string> meta);
}
