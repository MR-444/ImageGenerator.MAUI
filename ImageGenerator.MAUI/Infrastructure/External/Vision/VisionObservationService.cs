using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ImageGenerator.MAUI.Core.Domain.Services;
using ImageGenerator.MAUI.Infrastructure.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Vision;

/// <summary>
/// Factual image-observation pass used by "Describe an idea" when the idea source is a reference image.
/// Routes to local Ollama and OpenRouter vision models from the <see cref="LlmTarget"/> the caller
/// resolved: which model and which address are answers supplied by the picker, not questions asked of
/// the settings store. Claude is not wired for images, so the target's provider is checked, not assumed.
/// </summary>
public sealed class VisionObservationService : IVisionObservationService
{
    public const string OpenRouterHttpClientName = "openrouter";

    private const string ObservationSystemPromptAsset = "PromptBuilder/image-observation.md";
    private const string ObservationOverrideFileName = "image-observation.md";
    private const int NumCtx = 8192;
    private const double Temperature = 0.2;

    private readonly IOpenRouterTokenStore? _openRouterTokenStore;
    private readonly ILogger<VisionObservationService> _logger;
    private readonly Func<string, Task<Stream>> _assetOpener;
    private readonly string _promptDirectory;
    private readonly ObservationCompletion _complete;

    /// <param name="baseUrlOrApiKey">
    /// What a completed call needs that is not in the target: the Ollama address for a local call, the
    /// OpenRouter key for a remote one. Named for both jobs because it is one parameter serving two — a
    /// wart kept deliberately, since splitting it would touch every test seam to no user-visible gain.
    /// </param>
    internal delegate Task<string> ObservationCompletion(
        ModelProvider provider,
        string modelId,
        string baseUrlOrApiKey,
        string systemPrompt,
        string base64Image,
        CancellationToken ct);

    public VisionObservationService(
        ILogger<VisionObservationService> logger,
        IHttpClientFactory httpClientFactory,
        IOpenRouterTokenStore? openRouterTokenStore = null)
        : this(
            logger,
            httpClientFactory,
            BuildHttpCompletion(httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)), logger),
            openRouterTokenStore)
    {
    }

    internal VisionObservationService(
        ILogger<VisionObservationService> logger,
        IHttpClientFactory httpClientFactory,
        ObservationCompletion completion,
        IOpenRouterTokenStore? openRouterTokenStore = null,
        string? promptDirectoryOverride = null,
        Func<string, Task<Stream>>? assetOpener = null)
    {
        _openRouterTokenStore = openRouterTokenStore;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _complete = completion ?? throw new ArgumentNullException(nameof(completion));
        _assetOpener = assetOpener ?? FileSystem.OpenAppPackageFileAsync;
        _promptDirectory = string.IsNullOrWhiteSpace(promptDirectoryOverride)
            ? OutputPaths.PromptBuilderDirectory
            : promptDirectoryOverride;
    }

    public async Task<VisionObservationResult> ObserveAsync(
        VisionObservationRequest request,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Base64Image))
            return VisionObservationResult.Fail("Pick a reference image first.");

        var target = request.Target;
        if (target.Provider is not (ModelProvider.Local or ModelProvider.OpenRouter))
        {
            // Named in the log as well as on screen: "not available in this build" is only worth reading
            // if the log can tell a missing capability apart from a broken network.
            _logger.LogWarning(
                "VisionObservation: asked to observe with {Provider}, which cannot take an image in this build",
                target.Provider);
            return VisionObservationResult.Fail(
                $"{target.ProviderLabel} cannot observe images in this build — pick Ollama or OpenRouter.");
        }

        if (!target.HasModel)
            return VisionObservationResult.Fail(target.MissingModelError("in the vision model row above"));

        string systemPrompt;
        try
        {
            systemPrompt = await LoadPromptAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VisionObservation: failed to load the observation prompt");
            return VisionObservationResult.Fail("Couldn't load the image-observation instructions. See app.log.");
        }

        var (baseUrlOrApiKey, credentialError) = await ResolveCredentialAsync(target);
        if (credentialError is not null)
            return VisionObservationResult.Fail(credentialError);

        string raw;
        try
        {
            raw = await _complete(
                target.Provider,
                target.ModelId,
                baseUrlOrApiKey,
                systemPrompt,
                StripDataUriPrefix(request.Base64Image),
                ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VisionObservation: model call failed Provider={Provider}", target.Provider);
            return VisionObservationResult.Fail(
                $"The image observer couldn't reach {target.ProviderLabel}: {ex.Message}");
        }

        var observation = raw.Trim();
        return string.IsNullOrEmpty(observation)
            ? VisionObservationResult.Fail($"{target.ProviderLabel} returned an empty image observation. Try another vision model.")
            : VisionObservationResult.Ok(observation);
    }

    /// <summary>
    /// The one thing the caller cannot supply: the secret. Which model and which address used to be
    /// resolved here too, out of the settings store, which left the picker on screen and the model being
    /// called as two separate memories.
    /// </summary>
    private async Task<(string BaseUrlOrApiKey, string? Error)> ResolveCredentialAsync(LlmTarget target)
    {
        if (target.Provider == ModelProvider.OpenRouter)
        {
            if (_openRouterTokenStore is null)
            {
                _logger.LogWarning(
                    "VisionObservation: OpenRouter was asked to observe with no {Store} registered.",
                    nameof(IOpenRouterTokenStore));
                return (string.Empty, "OpenRouter support is not registered in this build.");
            }

            var apiKey = await _openRouterTokenStore.LoadAsync();
            return string.IsNullOrWhiteSpace(apiKey)
                ? (string.Empty, LlmCredentials.MissingKeyMessage("OpenRouter"))
                : (apiKey, null);
        }

        // Local needs no secret, only an address; the target already carries the configured one.
        return (ModelConstants.Ollama.ResolveBaseUrl(target.BaseUrl), null);
    }

    private async Task<string> LoadPromptAsync()
    {
        EnsureOverrideReadme();

        var overridePath = Path.Combine(_promptDirectory, ObservationOverrideFileName);
        if (File.Exists(overridePath))
            return await File.ReadAllTextAsync(overridePath);

        await using var stream = await _assetOpener(ObservationSystemPromptAsset);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private void EnsureOverrideReadme()
    {
        try
        {
            Directory.CreateDirectory(_promptDirectory);

            var readmePath = Path.Combine(_promptDirectory, "README.txt");
            if (!File.Exists(readmePath))
            {
                File.WriteAllText(readmePath,
                    """
                    "Describe an idea" prompt builder — private overrides
                    ======================================================

                    Drop image-observation.md here to replace the bundled reference-image observation
                    instructions. Drop vpe-prompt.md or system-prompt.md for the existing prose and
                    Ideogram JSON passes.
                    """);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "VisionObservation: could not seed the override folder README Dir={Dir}", _promptDirectory);
        }
    }

    private static ObservationCompletion BuildHttpCompletion(IHttpClientFactory httpClientFactory, ILogger logger) =>
        (provider, modelId, baseUrlOrApiKey, systemPrompt, base64Image, ct) => provider switch
        {
            ModelProvider.Local =>
                SendOllamaVisionAsync(httpClientFactory, logger, baseUrlOrApiKey, modelId, systemPrompt, base64Image, ct),
            ModelProvider.OpenRouter =>
                SendOpenRouterVisionAsync(httpClientFactory, logger, apiKey: baseUrlOrApiKey, modelId, systemPrompt, base64Image, ct),
            _ => throw new InvalidOperationException($"Unsupported vision provider: {provider}")
        };

    private static readonly JsonSerializerOptions BodyJson = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static async Task<string> SendOllamaVisionAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string baseUrl,
        string modelId,
        string systemPrompt,
        string base64Image,
        CancellationToken ct)
    {
        var body = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = "Observe the attached reference image.", images = new[] { base64Image } }
            },
            stream = false,
            think = false,
            options = new { num_ctx = NumCtx, temperature = Temperature }
        };

        var endpoint = $"{baseUrl.TrimEnd('/')}/api/chat";
        using var client = httpClientFactory.CreateClient(Ollama.OllamaChatTransport.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json");
        request.Options.Set(RemoteHttpLoggingHandler.PurposeKey, "local image observation");
        request.Options.Set(RemoteHttpLoggingHandler.ModelKey, modelId);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Ollama vision HTTP {StatusCode} Url={Url} Body={Body}",
                (int)response.StatusCode, endpoint, Truncate(responseBody, 1000));
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} - {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.GetString() ?? string.Empty;
        }

        throw new InvalidOperationException("Ollama response carried no message content.");
    }

    private static async Task<string> SendOpenRouterVisionAsync(
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        string apiKey,
        string modelId,
        string systemPrompt,
        string base64Image,
        CancellationToken ct)
    {
        var body = new
        {
            model = modelId,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Observe the attached reference image." },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = ImageDataUriEncoder.BuildDataUri(base64Image) }
                        }
                    }
                }
            },
            stream = false,
            temperature = Temperature,
            max_tokens = 1200
        };

        using var client = httpClientFactory.CreateClient(OpenRouterHttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, BodyJson), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/mr444/ImageGenerator.MAUI");
        request.Headers.TryAddWithoutValidation("X-Title", "Emberforge");
        request.Options.Set(RemoteHttpLoggingHandler.PurposeKey, "remote image observation");
        request.Options.Set(RemoteHttpLoggingHandler.ModelKey, modelId);

        using var response = await client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenRouter vision HTTP {StatusCode} Body={Body}",
                (int)response.StatusCode, Truncate(responseBody, 1000));
            throw new HttpRequestException(
                $"HTTP {(int)response.StatusCode} - {ExtractErrorMessage(responseBody) ?? Truncate(responseBody, 200)}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var doc = JsonDocument.Parse(responseBody);
        if (doc.RootElement.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return ExtractOpenRouterContent(content);
        }

        throw new InvalidOperationException("OpenRouter response carried no message content.");
    }

    private static string ExtractOpenRouterContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in content.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && part.TryGetProperty("text", out var text))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(text.GetString());
            }
        }

        return sb.ToString();
    }

    private static string StripDataUriPrefix(string value)
    {
        var comma = value.IndexOf(',');
        return value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? value[(comma + 1)..]
            : value;
    }

    internal static string? ExtractErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.ValueKind != JsonValueKind.Object)
                    return error.GetString();

                var messageText = error.TryGetProperty("message", out var message)
                    ? message.GetString()
                    : null;
                var rawText = error.TryGetProperty("metadata", out var metadata)
                    && metadata.ValueKind == JsonValueKind.Object
                    && metadata.TryGetProperty("raw", out var raw)
                    ? raw.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(rawText)
                    ? messageText
                    : string.IsNullOrWhiteSpace(messageText)
                        ? rawText
                        : $"{messageText}: {rawText}";
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max] + "...";
}
