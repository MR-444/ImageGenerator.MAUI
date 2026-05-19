using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class JobRunner : IJobRunner
{
    private readonly IImageGenerationService _imageService;
    private readonly IImageFileService _imageFileService;
    private readonly ILogger<JobRunner> _logger;

    public JobRunner(
        IImageGenerationService imageService,
        IImageFileService imageFileService,
        ILogger<JobRunner> logger)
    {
        _imageService = imageService;
        _imageFileService = imageFileService;
        _logger = logger;
    }

    public async Task<JobOutcome> RunAsync(ImageGenerationParameters parameters, CancellationToken ct)
    {
        var result = await _imageService.GenerateImageAsync(parameters, ct);

        if (result.ImageData is null or { Length: 0 })
        {
            // Service returned a structured error/canceled message instead of image data.
            // Centralized failure logging: image services follow a swallow-and-return-Message
            // pattern (so the UI gets a clean string instead of an exception), but that means
            // a caught error never trips the unhandled-exception hooks. Logging here at the
            // architectural boundary guarantees every failure lands in app.log even if a
            // service's catch block forgot an explicit log call. Services that have richer
            // context (URL, body, stack trace) still log it earlier; this is the safety net.
            var msg = result.Message ?? "Image generation failed.";
            _logger.LogError("Job failed Model={Model} Reason={Reason}", parameters.Model, msg);
            return new JobOutcome(JobOutcomeKind.Failed, null, msg);
        }

        Directory.CreateDirectory(OutputPaths.GeneratedImagesDirectory);
        var path = _imageFileService.GetUniqueSavePath(OutputPaths.GeneratedImagesDirectory, parameters);

        try
        {
            await _imageFileService.SaveImageWithMetadataAsync(path, result.ImageData, parameters);
        }
        catch (Exception ex)
        {
            // ImageSharp.SaveAsync writes directly to the final path with no temp+move, so
            // a mid-write failure (disk full, AV lock, IO race) can leave a partial image
            // orphaned in the gallery. Best-effort cleanup; a cleanup failure is swallowed
            // so the user-facing outcome stays focused on the original save error.
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to delete partial image after save error Path={Path}", path);
            }

            _logger.LogError(ex, "Image save failed Path={Path} Model={Model}", path, parameters.Model);
            return new JobOutcome(JobOutcomeKind.Failed, null, $"Image save failed: {ex.Message}");
        }

        return new JobOutcome(JobOutcomeKind.Saved, path, $"Saved to {path}");
    }
}
