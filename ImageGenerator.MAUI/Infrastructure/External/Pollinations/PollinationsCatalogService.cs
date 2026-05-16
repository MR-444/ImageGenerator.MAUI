using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Infrastructure.External.Pollinations;

public sealed class PollinationsCatalogService : IPollinationsCatalogService
{
    // gen.pollinations.ai is the canonical host (legacy image.pollinations.ai/models is
    // effectively dead — returns only ["sana"]). The response is a JSON array of rich
    // model objects; we filter to image-producing free-tier ones only.
    private const string ModelsEndpoint = "https://gen.pollinations.ai/models";

    private readonly HttpClient _httpClient;

    public PollinationsCatalogService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<IReadOnlyList<ModelOption>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            var entries = await _httpClient.GetFromJsonAsync<List<PollinationsModelEntry>>(ModelsEndpoint, ct);
            if (entries is null) return [];

            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name)
                            && e.OutputModalities?.Contains("image", StringComparer.OrdinalIgnoreCase) == true
                            && e.PaidOnly != true)
                .Select(e => new ModelOption(
                    Display: ToDisplayName(e),
                    Value: ModelConstants.Pollinations.PrefixSlash + e.Name!,
                    Provider: ProviderConstants.Pollinations))
                .ToList();
        }
        catch (Exception ex)
        {
            // Mirror ModelCatalogService.SafeFetchReplicateAsync: swallow + log so a transient
            // network failure during Refresh doesn't take down the whole catalog refresh.
            Debug.WriteLine($"Pollinations catalog fetch failed: {ex.Message}");
            return [];
        }
    }

    private static string ToDisplayName(PollinationsModelEntry entry)
    {
        // Pollinations' description starts with a human title before " - " — e.g. "Flux Schnell -
        // Fast high-quality image generation". Use that title when present, falling back to a
        // title-cased slug if the description is missing.
        if (!string.IsNullOrWhiteSpace(entry.Description))
        {
            var dash = entry.Description.IndexOf(" - ", StringComparison.Ordinal);
            var title = dash > 0 ? entry.Description[..dash] : entry.Description;
            return $"{title.Trim()} (Pollinations)";
        }

        var slug = entry.Name ?? string.Empty;
        var titled = string.Join(' ',
            slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s)));
        return $"{titled} (Pollinations)";
    }

    private sealed class PollinationsModelEntry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("aliases")] public List<string>? Aliases { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("input_modalities")] public List<string>? InputModalities { get; set; }
        [JsonPropertyName("output_modalities")] public List<string>? OutputModalities { get; set; }
        [JsonPropertyName("paid_only")] public bool? PaidOnly { get; set; }
    }
}
