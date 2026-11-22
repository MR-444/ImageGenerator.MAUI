namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// The model declined the request: HTTP 200 with <c>stop_reason: "refusal"</c>. Not a transport failure,
/// so callers must neither say they "couldn't reach" the provider nor retry - the same request meets the
/// same safety classifier and is billed again.
/// </summary>
internal sealed class LlmRefusalException(string provider, string? category, string? explanation)
    : Exception(Describe(provider, category, explanation))
{
    /// <summary>The classifier's category (<c>cyber</c>, <c>bio</c>, …), or null when the API gave none.</summary>
    public string? Category { get; } = category;

    /// <summary>The API's own explanation, or null when it gave none.</summary>
    public string? Explanation { get; } = explanation;

    private static string Describe(string provider, string? category, string? explanation)
    {
        var head = string.IsNullOrWhiteSpace(category)
            ? $"{provider} declined this request"
            : $"{provider} declined this request ({category})";
        return string.IsNullOrWhiteSpace(explanation)
            ? $"{head}. Rephrase it, or pick another model."
            : $"{head}: {explanation}";
    }
}
