using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;

/// <summary>
/// Builds the request body that ReplicateImageGenerationService POSTs to /v1/models/{model}/predictions.
/// One implementation per known model; FallbackReplicateDescriptor handles unknown owner/name paths.
/// </summary>
public interface IPayloadBuilder
{
    string ModelId { get; }
    object Build(ImageGenerationParameters parameters);
}
