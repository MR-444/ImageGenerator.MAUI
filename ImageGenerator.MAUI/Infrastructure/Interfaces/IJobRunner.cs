using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Runs a single generation job: calls the image-generation service, decodes the result,
/// saves it to disk with metadata. Throws OperationCanceledException on explicit cancel;
/// non-cancellation exceptions also propagate so the VM can surface them as Error.
/// </summary>
public interface IJobRunner
{
    /// <param name="progress">Optional live progress sink, forwarded to the generation service.</param>
    Task<JobOutcome> RunAsync(
        ImageGenerationParameters parameters, CancellationToken ct, IProgress<JobProgress>? progress = null);
}

public enum JobOutcomeKind { Saved, Failed }

/// <summary>
/// Result of a single job. <see cref="Kind"/> distinguishes "saved to disk" from "service
/// returned an error message" (e.g. canceled-by-server, no output URL).
/// </summary>
public sealed record JobOutcome(JobOutcomeKind Kind, string? SavedPath, string Message);
