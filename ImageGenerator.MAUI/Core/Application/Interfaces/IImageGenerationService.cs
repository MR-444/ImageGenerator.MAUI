using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Core.Application.Interfaces;

/// <summary>
/// Defines the contract for a service responsible for generating images based on
/// the provided parameters.
/// </summary>
public interface IImageGenerationService
{
    /// <summary>
    /// Asynchronously generates an image based on the provided parameters.
    /// </summary>
    /// <param name="parameters">Image generation parameters (model, prompt, dimensions, token, ...).</param>
    /// <param name="cancellationToken">Token that cancels the generation and any in-flight HTTP / polling work.</param>
    Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default);
}
