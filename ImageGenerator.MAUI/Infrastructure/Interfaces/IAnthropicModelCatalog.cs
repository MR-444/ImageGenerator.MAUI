namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Lists the models an Anthropic key can actually be pointed at. Live, for the same reason
/// OpenRouter's list is live: the set changes.
/// </summary>
public interface IAnthropicModelCatalog
{
    /// <summary>
    /// Every model the key can see, in the order the API serves them (newest first).
    /// <paramref name="apiKey"/> is passed by the caller because the store, not the catalog,
    /// decides which key a role uses.
    /// </summary>
    Task<IReadOnlyList<AnthropicModelInfo>> ListTextModelsAsync(
        string apiKey,
        CancellationToken ct = default);
}

/// <summary>
/// One row of Anthropic's model catalogue.
/// <see cref="Lifecycle"/> is kept rather than filtered away because a deprecated model is still
/// callable — for some keys only — and hiding it would be a guess about who is allowed to use it.
/// Retired models are dropped: they fail inference, so offering one is a way to lose a run.
/// </summary>
public sealed record AnthropicModelInfo(
    string Id,
    string DisplayName,
    string Lifecycle,
    bool AcceptsImages)
{
    public const string Active = "active";
    public const string Deprecated = "deprecated";
    public const string Retired = "retired";

    public bool IsDeprecated => string.Equals(Lifecycle, Deprecated, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a picker row reads. The id is always present: two display names can match, an id cannot.</summary>
    public string Display => IsDeprecated
        ? $"{DisplayName} ({Id}) — deprecated"
        : $"{DisplayName} ({Id})";
}
