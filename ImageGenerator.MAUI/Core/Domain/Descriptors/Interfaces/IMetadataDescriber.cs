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
}
