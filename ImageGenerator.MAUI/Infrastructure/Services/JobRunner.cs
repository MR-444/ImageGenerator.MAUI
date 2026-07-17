using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
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

    public async Task<JobOutcome> RunAsync(
        ImageGenerationParameters parameters, CancellationToken ct, IProgress<JobProgress>? progress = null)
    {
        var result = await _imageService.GenerateImageAsync(parameters, ct, progress);

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

        if (!ShouldChainUpscale(parameters))
        {
            return new JobOutcome(JobOutcomeKind.Saved, path, $"Saved to {path}");
        }
        return await RunUpscalePassAsync(parameters, result.ImageData, path, ct, progress);
    }

    /// <summary>
    /// The chained pass runs only for ComfyUI renders (the VM holds the GPU gate across this
    /// whole method for those), needs a designated workflow, and must not re-upscale an
    /// upscale — generating directly WITH the upscale workflow never chains.
    /// </summary>
    private static bool ShouldChainUpscale(ImageGenerationParameters parameters) =>
        parameters.UpscaleAfterRender
        && !string.IsNullOrEmpty(parameters.UpscaleWorkflow)
        && ModelConstants.ComfyUi.IsId(parameters.Model)
        && !string.Equals(
            ModelConstants.ComfyUi.WorkflowName(parameters.Model),
            parameters.UpscaleWorkflow,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Feeds the just-saved render into the designated upscale workflow as a second normal
    /// generation (the ComfyUI service uploads it for the workflow's LoadImage node). The
    /// render is already on disk, so every failure past this point is PARTIAL success: the
    /// outcome stays Saved with the render path and a caveat message. On success the outcome
    /// carries the upscaled path — downstream side effects (featured preview, CivitAI post)
    /// then act on the better image.
    /// </summary>
    private async Task<JobOutcome> RunUpscalePassAsync(
        ImageGenerationParameters parameters,
        byte[] renderBytes,
        string renderPath,
        CancellationToken ct,
        IProgress<JobProgress>? progress)
    {
        progress?.Report(new JobProgress("Upscaling…"));

        var up = parameters.Clone();
        up.Model = ModelConstants.ComfyUi.PrefixSlash + parameters.UpscaleWorkflow;
        up.UpscaleAfterRender = false;
        // Upscale graphs take plain tile conditioning; the render's prompt rides along as-is
        // (same trick as the reference implementation: the compose-time prompt conditions the
        // tiles). Workflow-specific state of the RENDER workflow must not leak into the
        // upscale pass or its metadata.
        up.UseJsonPrompt = false;
        up.ComfyUiPreset = string.Empty;
        up.ComfyUiPresetDisplay = string.Empty;
        up.ComfyUiModelDisplay = string.Empty;
        up.ImagePrompts.Clear();
        up.ImagePrompts.Add(Convert.ToBase64String(renderBytes));

        try
        {
            var relabeled = progress is null ? null : new UpscaleProgressRelabeler(progress);
            var result = await _imageService.GenerateImageAsync(up, ct, relabeled);
            if (result.ImageData is null or { Length: 0 })
            {
                var reason = result.Message ?? "Upscale failed.";
                _logger.LogError(
                    "Upscale pass failed Workflow={Workflow} Reason={Reason}", parameters.UpscaleWorkflow, reason);
                return new JobOutcome(JobOutcomeKind.Saved, renderPath, $"Saved to {renderPath}. Upscale failed: {reason}");
            }

            var upscaledPath = GetUniqueUpscaledPath(renderPath);
            try
            {
                await _imageFileService.SaveImageWithMetadataAsync(upscaledPath, result.ImageData, up);
            }
            catch (Exception ex)
            {
                // Same partial-file cleanup as the render save above.
                try
                {
                    if (File.Exists(upscaledPath)) File.Delete(upscaledPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "Failed to delete partial upscale after save error Path={Path}", upscaledPath);
                }
                _logger.LogError(ex, "Upscale save failed Path={Path}", upscaledPath);
                return new JobOutcome(JobOutcomeKind.Saved, renderPath, $"Saved to {renderPath}. Upscale save failed: {ex.Message}");
            }

            return new JobOutcome(
                JobOutcomeKind.Saved, upscaledPath, $"Saved to {renderPath} + upscaled to {upscaledPath}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new JobOutcome(JobOutcomeKind.Saved, renderPath, $"Saved to {renderPath}. Upscale canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upscale pass threw Workflow={Workflow}", parameters.UpscaleWorkflow);
            return new JobOutcome(JobOutcomeKind.Saved, renderPath, $"Saved to {renderPath}. Upscale failed: {ex.Message}");
        }
    }

    /// <summary>"<c>render.png</c>" → "<c>render_upscaled.png</c>", then _upscaled_1, _2, … on collision.</summary>
    private static string GetUniqueUpscaledPath(string renderPath)
    {
        var directory = Path.GetDirectoryName(renderPath) ?? OutputPaths.GeneratedImagesDirectory;
        var stem = Path.GetFileNameWithoutExtension(renderPath) + "_upscaled";
        var ext = Path.GetExtension(renderPath);

        var candidate = Path.Combine(directory, stem + ext);
        if (!File.Exists(candidate)) return candidate;
        for (var i = 1; i < 100; i++)
        {
            var next = Path.Combine(directory, $"{stem}_{i}{ext}");
            if (!File.Exists(next)) return next;
        }
        throw new IOException($"Could not find an unused upscale filename for '{renderPath}'.");
    }

    /// <summary>Rewrites the ws phase label so the chained pass reads "Upscaling…" instead of "Rendering…".</summary>
    private sealed class UpscaleProgressRelabeler(IProgress<JobProgress> inner) : IProgress<JobProgress>
    {
        public void Report(JobProgress value) =>
            inner.Report(value.Message.StartsWith("Rendering", StringComparison.Ordinal)
                ? value with { Message = "Upscaling" + value.Message["Rendering".Length..] }
                : value);
    }
}
