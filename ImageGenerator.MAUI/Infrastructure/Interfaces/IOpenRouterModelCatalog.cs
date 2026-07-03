namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>Lists OpenRouter models that can observe images and return text.</summary>
public interface IOpenRouterModelCatalog
{
    Task<IReadOnlyList<OpenRouterModelInfo>> ListVisionModelsAsync(
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
