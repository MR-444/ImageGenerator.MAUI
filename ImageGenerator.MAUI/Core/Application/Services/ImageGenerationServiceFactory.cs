using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.External.OpenAi.Interfaces;
using ImageGenerator.MAUI.Infrastructure.External.Replicate.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Core.Application.Services;

/// <summary>
/// Routes a generation request to the correct provider based on the model name.
/// Unknown models fail fast rather than silently hitting Replicate with a bogus model id.
/// </summary>
public class ImageGenerationServiceFactory : IImageGenerationService
{
    private readonly IOpenAiImageGenerationService _openAiService;
    private readonly IReplicateImageGenerationService _replicateService;

    public ImageGenerationServiceFactory(
        IOpenAiImageGenerationService openAiService,
        IReplicateImageGenerationService replicateService)
    {
        _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
        _replicateService = replicateService ?? throw new ArgumentNullException(nameof(replicateService));
    }

    public Task<GeneratedImage> GenerateImageAsync(ImageGenerationParameters parameters, CancellationToken cancellationToken = default)
    {
        var service = Resolve(parameters.Model);
        return service.GenerateImageAsync(parameters, cancellationToken);
    }

    internal IImageGenerationService Resolve(string modelName)
    {
        // Legacy: the hardcoded `openAI/...` constant uses the native OpenAI API client.
        if (string.Equals(modelName, ModelConstants.OpenAI.GptImage1, StringComparison.Ordinal))
            return _openAiService;

        // Any `{owner}/{name}` path is a Replicate-hosted model. This covers Flux (all variants),
        // OpenAI-hosted-on-Replicate (openai/gpt-image-1.5), and anything else the dynamic catalog
        // fetch surfaces from the text-to-image collection.
        if (LooksLikeReplicatePath(modelName))
            return _replicateService;

        throw new ArgumentException(
            $"Unknown model: '{modelName}'. Expected '{{owner}}/{{name}}' (Replicate) or legacy '{ModelConstants.OpenAI.GptImage1}'.",
            nameof(modelName));
    }

    private static bool LooksLikeReplicatePath(string modelName)
    {
        var slash = modelName.IndexOf('/');
        return slash > 0 && slash < modelName.Length - 1;
    }
}
