using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// Builds a V4 structured prompt from a freeform idea with one Claude Opus 4.8 call using the
/// Messages API's native structured outputs (<c>output_config.format = json_schema</c>, GA — no beta
/// header). Constrained decoding guarantees the JSON parses and matches the schema's <em>shape</em>;
/// the semantic rules the schema can't express (art_style XOR photo, uppercase #RRGGBB, bbox ordering,
/// the ~60-word desc cap) are caught by <see cref="V4JsonPromptValidator"/>, with one retry that feeds
/// the validator's complaints back to the model.
/// <para>
/// The transport is raw <see cref="HttpClient"/> + System.Text.Json against api.anthropic.com — the
/// same shape <c>CivitaiPostingService</c> uses, which carries zero risk to the MAUI win-x64
/// single-file/trimmed publish. The official <c>Anthropic</c> NuGet SDK can replace the
/// <see cref="StructuredCompletion"/> seam later without touching the interface, the validator gate,
/// or the handoff — once its publish behavior is verified on the trimmed target.
/// </para>
/// </summary>
public sealed class AnthropicPromptBuilderService : IPromptBuilderService
{
    /// <summary>Hardcoded model. User-verified the only viable tier; Fable 5 is the one-line future bump.</summary>
    public const string ModelId = "claude-opus-4-8";

    public const string HttpClientName = "anthropic";

    private const string SystemPromptAsset = "PromptBuilder/v4-builder-system.md";
    private const string OverrideFileName = "system-prompt.md";
    private const string MessagesEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const int MaxOutputTokens = 16000;
    private const int MaxAttempts = 2;   // initial call + one validator-feedback retry

    /// <summary>
    /// Transport seam: given the API key + assembled turns, return the model's raw structured-output
    /// JSON text. Production = one Anthropic Messages call; tests inject a fake so the request build,
    /// override precedence, and validate-retry logic run without disk or network.
    /// </summary>
    internal delegate Task<string> StructuredCompletion(
        string apiKey, string systemPrompt, IReadOnlyList<ChatTurn> messages, CancellationToken ct);

    internal readonly record struct ChatTurn(string Role, string Content);

    // Verbatim property names (no naming policy): the body uses the exact API field spellings.
    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // JSON Schema mirroring V4JsonPrompt's SHAPE. Structured outputs cannot express string length,
    // numeric ranges, regex, or the art_style XOR photo rule — those stay in V4JsonPromptValidator.
    // additionalProperties:false on every object (required by structured outputs). Optional-parameter
    // count (~9) is well under the 24 total / 16 union-typed caps.
    private static readonly JsonElement V4Schema = JsonDocument.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "high_level_description": { "type": "string" },
            "style_description": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "aesthetics": { "type": "string" },
                "lighting": { "type": "string" },
                "medium": { "type": "string" },
                "art_style": { "type": "string" },
                "photo": { "type": "string" },
                "color_palette": { "type": "array", "items": { "type": "string" } }
              },
              "required": ["medium"]
            },
            "compositional_deconstruction": {
              "type": "object",
              "additionalProperties": false,
              "properties": {
                "background": { "type": "string" },
                "elements": {
                  "type": "array",
                  "items": {
                    "type": "object",
                    "additionalProperties": false,
                    "properties": {
                      "type": { "type": "string", "enum": ["obj", "text"] },
                      "bbox": { "type": "array", "items": { "type": "integer" } },
                      "text": { "type": "string" },
                      "desc": { "type": "string" },
                      "color_palette": { "type": "array", "items": { "type": "string" } }
                    },
                    "required": ["type", "desc"]
                  }
                }
              },
              "required": ["background", "elements"]
            }
          },
          "required": ["high_level_description", "compositional_deconstruction"]
        }
        """).RootElement.Clone();

    private readonly IAnthropicTokenStore _tokenStore;
    private readonly ILogger<AnthropicPromptBuilderService> _logger;
    private readonly Func<string, Task<Stream>> _assetOpener;
    private readonly string _promptDirectory;
    private readonly StructuredCompletion _complete;

    /// <summary>Production ctor (DI): the transport is one Anthropic Messages call over the named
    /// HttpClient. The token store, logger, and factory are all resolved from the container.</summary>
    public AnthropicPromptBuilderService(
        IAnthropicTokenStore tokenStore,
        ILogger<AnthropicPromptBuilderService> logger,
        IHttpClientFactory httpClientFactory)
        : this(tokenStore, logger,
            BuildHttpCompletion(httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)), logger))
    {
    }

    /// <summary>
    /// Core/test ctor. Seams mirror <c>MutationLibraryService</c>: a fake <paramref name="completion"/>
    /// replaces the network, an injectable <paramref name="assetOpener"/> (default
    /// <see cref="FileSystem.OpenAppPackageFileAsync"/>) and a <paramref name="promptDirectoryOverride"/>
    /// make the override-precedence + request-building + validate-retry logic unit-testable without
    /// disk or HTTP. Internal so the <see cref="StructuredCompletion"/> seam stays non-public.
    /// </summary>
    internal AnthropicPromptBuilderService(
        IAnthropicTokenStore tokenStore,
        ILogger<AnthropicPromptBuilderService> logger,
        StructuredCompletion completion,
        string? promptDirectoryOverride = null,
        Func<string, Task<Stream>>? assetOpener = null)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _complete = completion ?? throw new ArgumentNullException(nameof(completion));
        _assetOpener = assetOpener ?? FileSystem.OpenAppPackageFileAsync;
        _promptDirectory = string.IsNullOrWhiteSpace(promptDirectoryOverride)
            ? OutputPaths.PromptBuilderDirectory
            : promptDirectoryOverride;
    }

    public async Task<PromptBuilderResult> BuildAsync(string idea, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idea))
            return PromptBuilderResult.Fail("Describe an idea first.");

        var apiKey = await _tokenStore.LoadAsync();
        if (string.IsNullOrWhiteSpace(apiKey))
            return PromptBuilderResult.Fail("No Anthropic API key — add it on the Settings page.");

        string systemPrompt;
        try
        {
            systemPrompt = await LoadSystemPromptAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromptBuilder: failed to load the system prompt");
            return PromptBuilderResult.Fail("Couldn't load the prompt-builder instructions. See app.log.");
        }

        var messages = new List<ChatTurn> { new("user", idea.Trim()) };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string raw;
            try
            {
                raw = await _complete(apiKey!, systemPrompt, messages, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PromptBuilder: Anthropic call failed (attempt {Attempt})", attempt);
                return PromptBuilderResult.Fail($"The prompt builder couldn't reach Claude: {ex.Message}");
            }

            V4JsonPrompt prompt;
            try
            {
                prompt = V4JsonPromptSerializer.Deserialize(raw);
            }
            catch (V4JsonPromptParseException ex)
            {
                if (attempt >= MaxAttempts)
                    return PromptBuilderResult.Fail($"Claude returned text that isn't a valid structured prompt: {ex.Message}");

                messages.Add(new("assistant", raw));
                messages.Add(new("user",
                    $"That was not valid JSON for the schema: {ex.Message}. Return only the corrected JSON object."));
                continue;
            }

            var errors = V4JsonPromptValidator.Validate(prompt);
            if (errors.Count == 0)
                return PromptBuilderResult.Ok(prompt);

            if (attempt >= MaxAttempts)
                return PromptBuilderResult.Fail("Claude's prompt didn't satisfy the schema:\n• " + string.Join("\n• ", errors));

            messages.Add(new("assistant", raw));
            messages.Add(new("user",
                "The JSON had these problems:\n• " + string.Join("\n• ", errors)
                + "\nReturn only the corrected JSON object that fixes them."));
        }

        // Unreachable: the loop returns on the final attempt either way.
        return PromptBuilderResult.Fail("The prompt builder gave up after a retry.");
    }

    /// <summary>
    /// Private override beats the bundled clean-room default: if <c>system-prompt.md</c> exists in the
    /// prompt-builder folder (the user's 3-yr IP, outside the repo), use it verbatim; otherwise read
    /// the bundled basic prompt from <c>Resources/Raw/PromptBuilder</c>.
    /// </summary>
    private async Task<string> LoadSystemPromptAsync()
    {
        var overridePath = Path.Combine(_promptDirectory, OverrideFileName);
        if (File.Exists(overridePath))
            return await File.ReadAllTextAsync(overridePath);

        await using var stream = await _assetOpener(SystemPromptAsset);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static StructuredCompletion BuildHttpCompletion(IHttpClientFactory httpClientFactory, ILogger logger) =>
        (apiKey, systemPrompt, messages, ct) => SendAsync(httpClientFactory, logger, apiKey, systemPrompt, messages, ct);

    private static async Task<string> SendAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string apiKey,
        string systemPrompt,
        IReadOnlyList<ChatTurn> messages,
        CancellationToken ct)
    {
        var body = new
        {
            model = ModelId,
            max_tokens = MaxOutputTokens,
            // Opus 4.8 supports ONLY adaptive thinking ("enabled"+budget_tokens 400s here).
            thinking = new { type = "adaptive" },
            // System as a cached block — prompt caching cuts repeat-call input cost ~10×.
            system = new object[]
            {
                new { type = "text", text = systemPrompt, cache_control = new { type = "ephemeral" } }
            },
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            // effort lives INSIDE output_config (per the claude-api skill), alongside the json_schema.
            // high = max creativity for the idea→V4 job.
            output_config = new
            {
                effort = "high",
                format = new { type = "json_schema", schema = V4Schema }
            }
        };

        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesEndpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "PromptBuilder Anthropic HTTP {StatusCode} Body={Body}",
                (int)response.StatusCode, Truncate(responseBody, 1000));
            throw new InvalidOperationException(
                $"HTTP {(int)response.StatusCode} — {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}");
        }

        return ExtractText(responseBody);
    }

    /// <summary>Pull the first text content block out of a Messages response (Opus 4.8 omits the
    /// thinking block's content but may still emit one before the text block).</summary>
    private static string ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) && type.GetString() == "text"
                    && block.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        throw new InvalidOperationException("Anthropic response carried no text content block.");
    }

    private static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — caller falls back to a truncated raw snippet.
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "…";
}
