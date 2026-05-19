using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Single IImageGenerationService entry point that picks the right concrete backend based on
/// the selected model id. Model ids prefixed "pollinations/" hit Pollinations.ai; everything
/// else (including bare "owner/name" Replicate ids) goes to Replicate. Keeps JobRunner and
/// the VM ignorant of provider plurality.
/// </summary>
public sealed class ImageGenerationDispatcher : IImageGenerationService
{
    private readonly ReplicateImageGenerationService _replicate;
    private readonly PollinationsImageGenerationService _pollinations;

    public ImageGenerationDispatcher(
        ReplicateImageGenerationService replicate,
        PollinationsImageGenerationService pollinations)
    {
        _replicate = replicate ?? throw new ArgumentNullException(nameof(replicate));
        _pollinations = pollinations ?? throw new ArgumentNullException(nameof(pollinations));
    }

    public Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        if (IsPollinations(parameters.Model))
        {
            return _pollinations.GenerateImageAsync(parameters, cancellationToken);
        }
        return _replicate.GenerateImageAsync(parameters, cancellationToken);
    }

    private static bool IsPollinations(string modelId) =>
        !string.IsNullOrEmpty(modelId)
        && modelId.StartsWith(ModelConstants.Pollinations.PrefixSlash, StringComparison.Ordinal);
}
