using ImageGenerator.MAUI.Core.Application.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;

/// <summary>
/// Defines the contract for a service that generates images using the Replicate API.
/// This interface extends the base <see cref="IImageGenerationService"/> to provide
/// functionalities specific to the Replicate image generation service.
/// </summary>
public interface IReplicateImageGenerationService : IImageGenerationService; 