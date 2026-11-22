namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

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

/// <param name="Target">
/// Who to call, with which model, at which address — resolved by the caller, the same contract every
/// other LLM service in this app has, so the model on screen is the model called.
/// </param>
public sealed record VisionObservationRequest(
    LlmTarget Target,
    string Base64Image,
    string FileName);

public sealed record VisionObservationResult(bool Success, string? Observation, string? Error)
{
    public static VisionObservationResult Ok(string observation) => new(true, observation, null);

    public static VisionObservationResult Fail(string error) => new(false, null, error);
}
