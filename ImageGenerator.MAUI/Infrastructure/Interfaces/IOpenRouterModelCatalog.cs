namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Lists OpenRouter models, once per kind of caller.</summary>
public interface IOpenRouterModelCatalog
{
    /// <summary>Models that can observe images and return text.</summary>
    Task<IReadOnlyList<OpenRouterModelInfo>> ListVisionModelsAsync(
        bool freeOnly,
        CancellationToken ct = default);

    /// <summary>
    /// Models that return text, whatever they take as input — the list for a role that only ever
    /// sends text (prompt building, mutation, enrichment).
    /// Two rules keep it from being the vision list under another name: it does <b>not</b> require image
    /// input, because requiring it would hide nearly every model worth writing prose with; and it does
    /// require text <b>output</b>, because a model that answers in embeddings cannot answer a writer.
    /// The vision filter stays the stricter one — an observer that cannot see is useless, a writer that
    /// could have seen is merely not using a feature.
    /// </summary>
    Task<IReadOnlyList<OpenRouterModelInfo>> ListChatModelsAsync(
        bool freeOnly,
        CancellationToken ct = default);
}

public sealed record OpenRouterModelInfo(
    string Id,
    string Name,
    bool IsFree)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Id : $"{Name} ({Id})";
}
