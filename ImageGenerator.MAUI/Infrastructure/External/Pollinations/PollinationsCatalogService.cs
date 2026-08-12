using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Pollinations;

public sealed class PollinationsCatalogService : IPollinationsCatalogService
{
    // gen.pollinations.ai is the canonical host (legacy image.pollinations.ai/models is
    // effectively dead — returns only ["sana"]). /image/models is the narrow feed (image +
    // video models, ~54 entries vs ~221 on /models), so we still filter by output modality.
    // Per the docs, listing endpoints need no auth — only *generation* requires an API key.
    private const string ModelsEndpoint = "https://gen.pollinations.ai/image/models";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PollinationsCatalogService> _logger;

    public PollinationsCatalogService(IHttpClientFactory httpClientFactory, ILogger<PollinationsCatalogService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<ModelOption>> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient(PollinationsImageGenerationService.HttpClientName);
            var entries = await httpClient.GetFromJsonAsync<List<PollinationsModelEntry>>(ModelsEndpoint, ct);
            if (entries is null) return [];

            return entries
                .Where(e => !string.IsNullOrWhiteSpace(e.Name)
                            && e.OutputModalities?.Contains("image", StringComparer.OrdinalIgnoreCase) == true
                            // Community models are alpha, unmonitored, and — per the docs — run on
                            // the owner's own backend rather than Pollinations infrastructure, so
                            // prompts would leave to a third party. Excluded on privacy grounds.
                            && e.Community != true
                            && e.Alpha != true)
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
            _logger.LogWarning(ex, "Pollinations catalog fetch failed");
            return [];
        }
    }

    // Paid-only models are listed rather than hidden: every request needs an API key now, so
    // the distinction is whether the key has pollen credits, not whether the model is reachable.
    // A short suffix keeps that visible in the picker, which binds ModelOption.Display directly.
    private const string PaidSuffix = " · paid";

    private static string ToDisplayName(PollinationsModelEntry entry)
    {
        var suffix = entry.PaidOnly == true ? PaidSuffix : string.Empty;
        return $"{ToModelName(entry)} (Pollinations{suffix})";
    }

    private static string ToModelName(PollinationsModelEntry entry)
    {
        // "title" is the human model name ("FLUX.1 Schnell"); "description" is a marketing
        // sentence and must never be used here. (An older API shape packed both into
        // description as "Title - blurb"; that field no longer carries the title at all.)
        if (!string.IsNullOrWhiteSpace(entry.Title)) return entry.Title.Trim();

        // Slug fallback — "flux-pro-ultra" → "Flux Pro Ultra". Owner-namespaced community slugs
        // ("vendouple/lucid-origin") keep only the model part.
        var slug = entry.Name ?? string.Empty;
        var lastSlash = slug.LastIndexOf('/');
        if (lastSlash >= 0) slug = slug[(lastSlash + 1)..];

        return string.Join(' ',
            slug.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s)));
    }

    private sealed class PollinationsModelEntry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("output_modalities")] public List<string>? OutputModalities { get; set; }
        [JsonPropertyName("paid_only")] public bool? PaidOnly { get; set; }
        [JsonPropertyName("community")] public bool? Community { get; set; }
        [JsonPropertyName("alpha")] public bool? Alpha { get; set; }
    }
}
