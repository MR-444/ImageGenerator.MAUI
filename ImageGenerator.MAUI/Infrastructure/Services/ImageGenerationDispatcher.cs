using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Infrastructure.External.Pollinations;
using ImageGenerator.MAUI.Infrastructure.External.Replicate;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.Services;

/// <summary>
/// Single IImageGenerationService entry point that picks the right concrete backend based on
/// the selected model id. Model ids prefixed "pollinations/" hit Pollinations.ai, "comfyui/"
/// hit the user's ComfyUI instance; everything else (including bare "owner/name" Replicate
/// ids) goes to Replicate. Keeps JobRunner and the VM ignorant of provider plurality.
/// </summary>
public sealed class ImageGenerationDispatcher : IImageGenerationService
{
    private readonly ReplicateImageGenerationService _replicate;
    private readonly PollinationsImageGenerationService _pollinations;
    private readonly ComfyUiImageGenerationService _comfyUi;

    public ImageGenerationDispatcher(
        ReplicateImageGenerationService replicate,
        PollinationsImageGenerationService pollinations,
        ComfyUiImageGenerationService comfyUi)
    {
        _replicate = replicate ?? throw new ArgumentNullException(nameof(replicate));
        _pollinations = pollinations ?? throw new ArgumentNullException(nameof(pollinations));
        _comfyUi = comfyUi ?? throw new ArgumentNullException(nameof(comfyUi));
    }

    public Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        if (ModelConstants.Pollinations.IsId(parameters.Model))
        {
            return _pollinations.GenerateImageAsync(parameters, cancellationToken);
        }
        if (ModelConstants.ComfyUi.IsId(parameters.Model))
        {
            return _comfyUi.GenerateImageAsync(parameters, cancellationToken);
        }
        return _replicate.GenerateImageAsync(parameters, cancellationToken);
    }
}
