using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class JobRunner : IJobRunner
{
    private readonly IImageGenerationService _imageService;
    private readonly IImageFileService _imageFileService;

    public JobRunner(IImageGenerationService imageService, IImageFileService imageFileService)
    {
        _imageService = imageService;
        _imageFileService = imageFileService;
    }

    public async Task<JobOutcome> RunAsync(ImageGenerationParameters parameters, CancellationToken ct)
    {
        var result = await _imageService.GenerateImageAsync(parameters, ct);

        if (result.ImageData is null or { Length: 0 })
        {
            // Service returned a structured error/canceled message instead of image data.
            return new JobOutcome(JobOutcomeKind.Failed, null, result.Message ?? "Image generation failed.");
        }

        Directory.CreateDirectory(OutputPaths.GeneratedImagesDirectory);
        var path = _imageFileService.GetUniqueSavePath(OutputPaths.GeneratedImagesDirectory, parameters);
        await _imageFileService.SaveImageWithMetadataAsync(path, result.ImageData, parameters);

        return new JobOutcome(JobOutcomeKind.Saved, path, $"Saved to {path}");
    }
}
