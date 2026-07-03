using System.Globalization;
using System.Text.Json;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Vision;

/// <summary>
/// Reads OpenRouter's public model catalog and filters it to models that accept image input and return
/// text. The catalog is intentionally live because OpenRouter's model set changes frequently.
/// </summary>
public sealed class OpenRouterModelCatalog : IOpenRouterModelCatalog
{
    private const string ModelsEndpoint = "https://openrouter.ai/api/v1/models";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<OpenRouterModelCatalog> _logger;

    public OpenRouterModelCatalog(IHttpClientFactory httpClientFactory, ILogger<OpenRouterModelCatalog> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<OpenRouterModelInfo>> ListVisionModelsAsync(
        bool freeOnly,
        CancellationToken ct = default)
    {
        using var client = _httpClientFactory.CreateClient(VisionObservationService.OpenRouterHttpClientName);
        using var response = await client.GetAsync(ModelsEndpoint, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenRouter /models HTTP {StatusCode} Body={Body}",
                (int)response.StatusCode, Truncate(body, 1000));
            throw new InvalidOperationException($"OpenRouter returned HTTP {(int)response.StatusCode} listing models.");
        }

        return ParseVisionModels(body, freeOnly);
    }

    internal static IReadOnlyList<OpenRouterModelInfo> ParseVisionModels(string body, bool freeOnly)
    {
        using var doc = JsonDocument.Parse(body);
        var result = new List<OpenRouterModelInfo>();

        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var model in data.EnumerateArray())
        {
            if (!model.TryGetProperty("id", out var idProp) || idProp.GetString() is not { Length: > 0 } id)
                continue;

            if (!SupportsModality(model, "input_modalities", "image")
                || !SupportsModality(model, "output_modalities", "text"))
                continue;

            var name = model.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? id
                : id;
            var isFree = IsFree(model);
            if (freeOnly && !isFree)
                continue;

            result.Add(new OpenRouterModelInfo(id, name, isFree));
        }

        return result
            .OrderByDescending(m => m.IsFree)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool SupportsModality(JsonElement model, string propertyName, string modality)
    {
        if (!model.TryGetProperty("architecture", out var architecture)
            || !architecture.TryGetProperty(propertyName, out var modalities)
            || modalities.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var item in modalities.EnumerateArray())
        {
            if (string.Equals(item.GetString(), modality, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsFree(JsonElement model)
    {
        if (!model.TryGetProperty("pricing", out var pricing))
            return false;

        return PriceIsZero(pricing, "prompt") && PriceIsZero(pricing, "completion");
    }

    private static bool PriceIsZero(JsonElement pricing, string propertyName)
    {
        if (!pricing.TryGetProperty(propertyName, out var value))
            return false;

        var text = value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.GetRawText();

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
            && price == 0m;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}
