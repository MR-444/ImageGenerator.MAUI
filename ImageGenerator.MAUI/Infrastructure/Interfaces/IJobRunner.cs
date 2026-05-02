using ImageGenerator.MAUI.Core.Domain.Entities;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Runs a single generation job: calls the image-generation service, decodes the result,
/// saves it to disk with metadata. Throws OperationCanceledException on explicit cancel;
/// non-cancellation exceptions also propagate so the VM can surface them as Error.
/// </summary>
public interface IJobRunner
{
    Task<JobOutcome> RunAsync(ImageGenerationParameters parameters, CancellationToken ct);
}

public enum JobOutcomeKind { Saved, Failed }

/// <summary>
/// Result of a single job. <see cref="Kind"/> distinguishes "saved to disk" from "service
/// returned an error message" (e.g. canceled-by-server, no output URL).
/// </summary>
public sealed record JobOutcome(JobOutcomeKind Kind, string? SavedPath, string Message);
