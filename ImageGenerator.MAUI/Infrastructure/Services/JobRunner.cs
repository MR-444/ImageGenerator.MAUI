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

        if (string.IsNullOrEmpty(result.ImageDataBase64))
        {
            // Service returned a structured error/canceled message instead of image data.
            return new JobOutcome(JobOutcomeKind.Failed, null, result.Message ?? "Image generation failed.");
        }

        Directory.CreateDirectory(OutputPaths.GeneratedImagesDirectory);
        var path = _imageFileService.GetUniqueSavePath(OutputPaths.GeneratedImagesDirectory, parameters);
        var bytes = Convert.FromBase64String(result.ImageDataBase64);
        await _imageFileService.SaveImageWithMetadataAsync(path, bytes, parameters);

        return new JobOutcome(JobOutcomeKind.Saved, path, $"Saved to {path}");
    }
}
