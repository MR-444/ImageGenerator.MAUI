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
/// other LLM service in this app was given. The observer used to take a provider enum and go and read
/// the model out of the settings store itself, which meant the picker on screen and the model actually
/// called were two different memories, and a page could not observe with a model it had just listed.
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
