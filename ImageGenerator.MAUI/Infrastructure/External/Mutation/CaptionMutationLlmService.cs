using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Mutation;

/// <summary>
/// LLM caption mutator. Shares the Anthropic Messages transport with the prompt builder and adds a free
/// local-Ollama path for verifying the round-trip. Each call is constrained to <see cref="V4StructuredSchema"/>,
/// then run through the same validator gate + one feedback retry the builder uses. The system prompt is a
/// bundled clean-room asset (private override allowed), mirroring the open-core boundary of the builder.
/// </summary>
public sealed class CaptionMutationLlmService : ICaptionMutationLlmService
{
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const string OpusModelId = "claude-opus-4-8";

    private const string SystemPromptAsset = "Mutation/mutation-system.md";
    private const string OverrideFileName = "mutation-prompt.md";

    private const int MaxAttempts = 2;   // initial call + one validator-feedback retry
    private const int LabelMaxLength = 64;

    /// <summary>
    /// Transport seam: route a mutation call to the right backend. For <see cref="ModelTier.Local"/> it
    /// hits Ollama at <paramref name="baseUrl"/> (no key); otherwise the Anthropic Messages API with
    /// <paramref name="apiKey"/>. Tests inject a fake to assert tier/modelId passthrough and exercise the
    /// validate-retry loop without disk or network.
    /// </summary>
    internal delegate Task<string> MutationCompletion(
        ModelTier tier, string modelId, string? apiKey, string baseUrl,
        string systemPrompt, IReadOnlyList<ChatTurn> messages, JsonElement schema, CancellationToken ct);

    private readonly IAnthropicTokenStore _tokenStore;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger<CaptionMutationLlmService> _logger;
    private readonly MutationCompletion _complete;
    private readonly Func<string, Task<Stream>> _assetOpener;
    private readonly string _promptDirectory;

    /// <summary>Production ctor (DI): Anthropic tiers use the shared named HttpClient + token store; the
    /// Local tier uses the Ollama client + the endpoint/model from <see cref="IUiStateStore"/>.</summary>
    public CaptionMutationLlmService(
        IAnthropicTokenStore tokenStore,
        IUiStateStore uiStateStore,
        ILogger<CaptionMutationLlmService> logger,
        IHttpClientFactory httpClientFactory)
        : this(tokenStore, uiStateStore, logger,
            BuildHttpCompletion(httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)), logger))
    {
    }

    /// <summary>Core/test ctor — a fake <paramref name="completion"/> replaces the network; the prompt
    /// directory + asset opener are injectable so override precedence is unit-testable.</summary>
    internal CaptionMutationLlmService(
        IAnthropicTokenStore tokenStore,
        IUiStateStore uiStateStore,
        ILogger<CaptionMutationLlmService> logger,
        MutationCompletion completion,
        string? promptDirectoryOverride = null,
        Func<string, Task<Stream>>? assetOpener = null)
    {
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _complete = completion ?? throw new ArgumentNullException(nameof(completion));
        _assetOpener = assetOpener ?? FileSystem.OpenAppPackageFileAsync;
        _promptDirectory = string.IsNullOrWhiteSpace(promptDirectoryOverride)
            ? OutputPaths.PromptBuilderDirectory
            : promptDirectoryOverride;
    }

    public Task<LlmVariantResult> MutateAsync(
        V4JsonPrompt baseCaption, string steer, int index, ModelTier tier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseCaption);
        return RunAsync(BuildMutateUserTurn(baseCaption, steer, index), tier, ct);
    }

    public Task<LlmVariantResult> BreedAsync(
        IReadOnlyList<V4JsonPrompt> winners, string steer, int index, ModelTier tier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(winners);
        if (winners.Count == 0)
            return Task.FromResult(LlmVariantResult.Fail("Select at least one variant to breed from."));
        return RunAsync(BuildBreedUserTurn(winners, steer, index), tier, ct);
    }

    /// <summary>Resolve credentials/endpoint for the tier, then the shared validate-retry loop.</summary>
    private async Task<LlmVariantResult> RunAsync(string userContent, ModelTier tier, CancellationToken ct)
    {
        var modelId = ResolveModelId(tier);

        string? apiKey = null;
        var baseUrl = string.Empty;
        if (tier == ModelTier.Local)
        {
            baseUrl = _uiStateStore.LoadOllamaBaseUrl() is { Length: > 0 } u ? u : ModelConstants.Ollama.DefaultBaseUrl;
        }
        else
        {
            apiKey = await _tokenStore.LoadAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
                return LlmVariantResult.Fail("No Anthropic API key — add it on the Settings page.");
        }

        string systemPrompt;
        try
        {
            systemPrompt = await LoadPromptAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CaptionMutationLlm: failed to load the mutation system prompt");
            return LlmVariantResult.Fail("Couldn't load the mutation instructions. See app.log.");
        }

        var messages = new List<ChatTurn> { new("user", userContent) };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string raw;
            try
            {
                raw = await _complete(tier, modelId, apiKey, baseUrl, systemPrompt, messages, V4StructuredSchema.Schema, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptionMutationLlm: model call failed (tier {Tier}, attempt {Attempt})", tier, attempt);
                return LlmVariantResult.Fail($"The mutation couldn't reach the model: {ex.Message}");
            }

            V4JsonPrompt prompt;
            try
            {
                prompt = V4JsonPromptSerializer.Deserialize(raw);
            }
            catch (V4JsonPromptParseException ex)
            {
                if (attempt >= MaxAttempts)
                    return LlmVariantResult.Fail($"The model returned text that isn't a valid structured prompt: {ex.Message}");

                messages.Add(new("assistant", raw));
                messages.Add(new("user",
                    $"That was not valid JSON for the schema: {ex.Message}. Return only the corrected JSON object."));
                continue;
            }

            var errors = V4JsonPromptValidator.Validate(prompt);
            if (errors.Count == 0)
                return LlmVariantResult.Ok(prompt, BuildLabel(prompt));

            if (attempt >= MaxAttempts)
                return LlmVariantResult.Fail("The model's prompt didn't satisfy the schema:\n• " + string.Join("\n• ", errors));

            messages.Add(new("assistant", raw));
            messages.Add(new("user",
                "The JSON had these problems:\n• " + string.Join("\n• ", errors)
                + "\nReturn only the corrected JSON object that fixes them."));
        }

        // Unreachable: the loop returns on the final attempt either way.
        return LlmVariantResult.Fail("The mutation gave up after a retry.");
    }

    private string ResolveModelId(ModelTier tier) => tier switch
    {
        ModelTier.Opus => OpusModelId,
        ModelTier.Local => _uiStateStore.LoadOllamaModel() is { Length: > 0 } m ? m : ModelConstants.Ollama.DefaultModel,
        _ => SonnetModelId
    };

    /// <summary>Per-variant creative lenses, picked by <c>index</c>. Each call is independent and blind to
    /// its siblings, so a generic "be distinct from the others" does nothing — handing each variant a
    /// concrete, different axis is what actually spreads the results. These are SCENE/content axes (not
    /// medium/art_style) so they diversify without fighting a pinned-style lock, which the VM carries
    /// separately in the steer; the user's steer always stays dominant.</summary>
    private static readonly string[] VariationLenses =
    [
        "Anchor it on a distinct TIME OF DAY and quality of light (e.g. pre-dawn, harsh noon, golden hour, deep night).",
        "Shift the SEASON, WEATHER or atmosphere (e.g. dry heat, fog, downpour, snow, drifting haze) so it touches every element.",
        "Change the EMOTIONAL REGISTER / mood (e.g. serene, tense, wistful, triumphant, uneasy) and let pose, light and colour carry it.",
        "Re-stage the COMPOSITION — a clearly different vantage, distance or framing (e.g. intimate close-up, high overhead, low and wide).",
        "Introduce a different NARRATIVE BEAT or implied action so it reads as another moment in the story.",
        "Emphasise a different FOCAL POINT and colour-temperature lean so attention lands somewhere new.",
    ];

    private static string LensFor(int index) => VariationLenses[index % VariationLenses.Length];

    private static string BuildMutateUserTurn(V4JsonPrompt baseCaption, string steer, int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine("BASE CAPTION (Ideogram V4 JSON):");
        sb.AppendLine(V4JsonPromptSerializer.Serialize(baseCaption));
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(steer)
            ? "MUTATION REQUEST: reimagine this in a fresh, distinctly different creative direction."
            : $"MUTATION REQUEST: {steer.Trim()}");
        sb.AppendLine();
        // No seed/temperature handle on the cloud path (thinking pins it), so diversity rides on a concrete,
        // index-keyed lens — not the old "be distinct from the other variations", which a blind call ignores.
        sb.Append($"This is variation #{index + 1}. {LensFor(index)} ")
          .Append("Rewrite the WHOLE caption coherently so the high_level_description, background and every ")
          .Append("element's desc reflect that direction together; never leave any part in the original framing. ")
          .Append("Return ONLY the resulting V4 JSON object.");
        return sb.ToString();
    }

    private static string BuildBreedUserTurn(IReadOnlyList<V4JsonPrompt> winners, string steer, int index)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PARENT CAPTIONS (Ideogram V4 JSON) — the user's favourites to breed from:");
        for (var i = 0; i < winners.Count; i++)
        {
            sb.AppendLine($"[{i + 1}] {V4JsonPromptSerializer.Serialize(winners[i])}");
        }
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(steer))
        {
            sb.AppendLine($"BREEDING REQUEST: {steer.Trim()}");
            sb.AppendLine();
        }
        sb.Append($"This is offspring #{index + 1}. Combine the strongest traits of the parents into a NEW ")
          .Append($"coherent caption, then push it in this distinct direction: {LensFor(index)} ")
          .Append("Keep the whole caption coherent across background and every element desc. ")
          .Append("Return ONLY the resulting V4 JSON object.");
        return sb.ToString();
    }

    /// <summary>Short job-card label: the new high-level description, trimmed.</summary>
    private static string BuildLabel(V4JsonPrompt prompt)
    {
        var hld = prompt.HighLevelDescription?.Trim();
        if (string.IsNullOrEmpty(hld)) return "AI variant";
        return hld.Length <= LabelMaxLength ? hld : hld[..LabelMaxLength].TrimEnd() + "…";
    }

    /// <summary>Private override beats the bundled clean-room default, mirroring the prompt builder.</summary>
    private async Task<string> LoadPromptAsync()
    {
        var overridePath = Path.Combine(_promptDirectory, OverrideFileName);
        if (File.Exists(overridePath))
            return await File.ReadAllTextAsync(overridePath);

        await using var stream = await _assetOpener(SystemPromptAsset);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static MutationCompletion BuildHttpCompletion(IHttpClientFactory httpClientFactory, ILogger logger) =>
        (tier, modelId, apiKey, baseUrl, systemPrompt, messages, schema, ct) =>
            tier == ModelTier.Local
                ? OllamaChatTransport.SendAsync(httpClientFactory, logger, baseUrl, modelId, systemPrompt, messages, schema, ct)
                : AnthropicMessagesTransport.SendAsync(httpClientFactory, logger, modelId, apiKey!, systemPrompt, messages, schema, ct);
}
