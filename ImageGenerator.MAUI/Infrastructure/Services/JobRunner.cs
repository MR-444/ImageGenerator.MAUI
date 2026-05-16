using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
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
            // Centralized failure logging: image services follow a swallow-and-return-Message
            // pattern (so the UI gets a clean string instead of an exception), but that means
            // a caught error never trips the unhandled-exception hooks in CrashLogger. Logging
            // here at the architectural boundary guarantees every failure lands in app.log
            // even if a service's catch block forgot an explicit CrashLogger call. Services
            // that have richer context (URL, body, stack trace) still log it earlier; this is
            // the safety net.
            var msg = result.Message ?? "Image generation failed.";
            CrashLogger.Log(
                "JobRunner.RunAsync",
                $"Model={parameters.Model}; Result={msg}");
            return new JobOutcome(JobOutcomeKind.Failed, null, msg);
        }

        Directory.CreateDirectory(OutputPaths.GeneratedImagesDirectory);
        var path = _imageFileService.GetUniqueSavePath(OutputPaths.GeneratedImagesDirectory, parameters);
        await _imageFileService.SaveImageWithMetadataAsync(path, result.ImageData, parameters);

        return new JobOutcome(JobOutcomeKind.Saved, path, $"Saved to {path}");
    }
}
