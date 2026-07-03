namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Provider used for the image-observation pass before the normal VPE prompt builder.</summary>
public enum VisionObservationProvider
{
    LocalOllama,
    OpenRouter
}

/// <summary>
/// Turns a reference image into a factual visual observation that can be fed into the existing
/// "Describe an idea" VPE pass. This service does not mutate or stylize; it reports what is visible.
/// </summary>
public interface IVisionObservationService
{
    Task<VisionObservationResult> ObserveAsync(
        VisionObservationRequest request,
        CancellationToken ct = default);
}

public sealed record VisionObservationRequest(
    VisionObservationProvider Provider,
    string Base64Image,
    string FileName,
    string? ModelId = null);

public sealed record VisionObservationResult(bool Success, string? Observation, string? Error)
{
    public static VisionObservationResult Ok(string observation) => new(true, observation, null);

    public static VisionObservationResult Fail(string error) => new(false, null, error);
}
