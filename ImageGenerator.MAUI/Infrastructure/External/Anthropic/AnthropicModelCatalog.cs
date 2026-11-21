using System.Text.Json;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// Reads <c>GET /v1/models</c> so the app stops guessing which Claude ids exist.
///
/// <para>
/// Two properties of that endpoint are easy to get wrong and are handled here rather than at the
/// call site. <b>The page size defaults to 20</b>, so a single request returns the twenty newest
/// models and hides every older one — silently, with a valid response. And <b>omitting
/// <c>lifecycle</c> returns active plus deprecated but never retired</b>, which is the right default
/// for a picker: a retired model fails inference, while a deprecated one is still callable by the
/// keys that are allowed to use it, so it is offered with the word on its label rather than hidden.
/// </para>
/// </summary>
public sealed class AnthropicModelCatalog : IAnthropicModelCatalog
{
    private const string ModelsEndpoint = "https://api.anthropic.com/v1/models";

    /// <summary>The endpoint's maximum page size. Under the default of 20 the list would be a lie.</summary>
    internal const int PageSize = 1000;

    /// <summary>
    /// A cursor that never advances (a server that ignores <c>after_id</c>, a stale id) must not spin
    /// this loop into an endless request stream, so paging stops here with whatever was collected.
    /// Ten pages is 10 000 models; the real catalogue is hundreds deep.
    /// </summary>
    internal const int MaxPages = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AnthropicModelCatalog> _logger;

    public AnthropicModelCatalog(IHttpClientFactory httpClientFactory, ILogger<AnthropicModelCatalog> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<AnthropicModelInfo>> ListTextModelsAsync(
        string apiKey,
        CancellationToken ct = default)
    {
        // Checked here because the alternative is a 401 whose text says nothing a user can act on,
        // and the caller shows that text verbatim in the row's status line. Wording comes from
        // LlmCredentials so a missing key says one sentence whether it was a call or a list that asked.
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(LlmCredentials.MissingKeyMessage("Anthropic"));

        using var client = _httpClientFactory.CreateClient(AnthropicMessagesTransport.HttpClientName);
        var all = new List<AnthropicModelInfo>();
        string? after = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"{ModelsEndpoint}?limit={PageSize}";
            if (!string.IsNullOrEmpty(after))
                url += "&after_id=" + Uri.EscapeDataString(after);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // Same key header and same version as a messages call: two requests from one app should
            // not claim two API versions, and the version this app pins is one decision, not two.
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicMessagesTransport.AnthropicVersion);

            using var response = await client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Anthropic /models HTTP {StatusCode} Body={Body}",
                    (int)response.StatusCode, Truncate(body, 1000));
                throw new InvalidOperationException($"Anthropic returned HTTP {(int)response.StatusCode} listing models.");
            }

            var (items, lastId, hasMore) = ParsePage(body);
            all.AddRange(items);

            // Both stop conditions are load-bearing: has_more answers whether the server has more to
            // give, and an unchanged cursor answers whether it is willing to advance.
            if (!hasMore || string.IsNullOrEmpty(lastId) || string.Equals(lastId, after, StringComparison.Ordinal))
                return all;

            after = lastId;
        }

        _logger.LogWarning("Anthropic /models stopped after {Pages} pages; the list may be incomplete.", MaxPages);
        return all;
    }

    /// <summary>
    /// One page of the response. Tolerant on purpose: which fields arrive depends on the pinned
    /// <c>anthropic-version</c>, and a missing optional field must not empty the picker — an older
    /// response is still a list of ids that can be called.
    /// </summary>
    internal static (IReadOnlyList<AnthropicModelInfo> Items, string? LastId, bool HasMore) ParsePage(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var items = new List<AnthropicModelInfo>();
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in data.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object
                    || !row.TryGetProperty("id", out var idProp)
                    || idProp.GetString() is not { Length: > 0 } id)
                    continue;

                var lifecycle = Read(row, "lifecycle") ?? AnthropicModelInfo.Active;
                if (string.Equals(lifecycle, AnthropicModelInfo.Retired, StringComparison.OrdinalIgnoreCase))
                    continue;

                var displayName = Read(row, "display_name");
                items.Add(new AnthropicModelInfo(
                    id,
                    string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
                    lifecycle,
                    AcceptsImages(row)));
            }
        }

        // "last_id: Last ID in the data list. Can be used as the after_id for the next page."
        var lastId = Read(root, "last_id");
        var hasMore = root.TryGetProperty("has_more", out var more)
                      && more.ValueKind == JsonValueKind.True;

        return (items, lastId, hasMore);
    }

    /// <summary>capabilities.image_input.supported — read, never assumed from the id.</summary>
    private static bool AcceptsImages(JsonElement row) =>
        row.TryGetProperty("capabilities", out var capabilities)
        && capabilities.ValueKind == JsonValueKind.Object
        && capabilities.TryGetProperty("image_input", out var imageInput)
        && imageInput.ValueKind == JsonValueKind.Object
        && imageInput.TryGetProperty("supported", out var supported)
        && supported.ValueKind == JsonValueKind.True;

    private static string? Read(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}
