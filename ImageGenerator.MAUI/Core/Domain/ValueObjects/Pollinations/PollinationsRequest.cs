namespace ImageGenerator.MAUI.Core.Domain.ValueObjects.Pollinations;

/// <summary>
/// Parameter bag handed from a descriptor's PayloadBuilder to the Pollinations service.
/// Model is the bare server-side name (e.g. "flux"), not the synthetic "pollinations/flux"
/// id used internally for provider routing.
/// </summary>
/// <param name="Quality">
/// Discrete quality tier, only honoured by the gptimage family (gptimage, gptimage-large,
/// gpt-image-2). Null for every other model, which omits the query param entirely.
/// </param>
public sealed record PollinationsRequest(
    string Prompt,
    string Model,
    int Width,
    int Height,
    long Seed,
    bool Safe,
    string? Quality = null);
