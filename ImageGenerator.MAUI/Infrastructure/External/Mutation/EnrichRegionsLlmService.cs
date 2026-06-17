using System.Globalization;
using System.Text;
using System.Text.Json;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
// MAUI's implicit usings bring in Microsoft.Maui.Controls.Element — disambiguate.
using Element = ImageGenerator.MAUI.Core.Domain.Ideogram.Element;

namespace ImageGenerator.MAUI.Infrastructure.External.Mutation;

/// <summary>
/// Region-aware caption enricher. Computes deterministic spatial facts with <see cref="RegionGraph"/>, then
/// asks the model to rewrite ONLY each element's <c>desc</c> against those facts. Shares the exact transport
/// seam, schema, prompt-override convention and validate-retry loop of <see cref="CaptionMutationLlmService"/>,
/// and adds an enrichment-specific preservation gate (<see cref="EnrichmentPreservation"/>) so the model can't
/// quietly move a bbox, drop an element or rewrite the headline.
/// </summary>
public sealed class EnrichRegionsLlmService : IEnrichRegionsLlmService
{
    private const string SonnetModelId = "claude-sonnet-4-6";
    private const string OpusModelId = "claude-opus-4-8";

    private const string SystemPromptAsset = "Mutation/enrich-region-system.md";
    private const string OverrideFileName = "enrich-region-prompt.md";

    private const int MaxAttempts = 2;   // initial call + one feedback retry
    private const int LabelMaxLength = 64;
    private const int IdentityMaxLength = 40;

    /// <summary>Transport seam — identical signature to <c>CaptionMutationLlmService.MutationCompletion</c> so
    /// tests inject a fake the same way. Local → Ollama (no key); otherwise the Anthropic Messages API.</summary>
    internal delegate Task<string> EnrichCompletion(
        ModelTier tier, string modelId, string? apiKey, string baseUrl,
        string systemPrompt, IReadOnlyList<ChatTurn> messages, JsonElement schema, CancellationToken ct);

    private readonly IAnthropicTokenStore _tokenStore;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger<EnrichRegionsLlmService> _logger;
    private readonly EnrichCompletion _complete;
    private readonly Func<string, Task<Stream>> _assetOpener;
    private readonly string _promptDirectory;

    /// <summary>Production ctor (DI): shares the named HttpClient + token store with the mutator/builder.</summary>
    public EnrichRegionsLlmService(
        IAnthropicTokenStore tokenStore,
        IUiStateStore uiStateStore,
        ILogger<EnrichRegionsLlmService> logger,
        IHttpClientFactory httpClientFactory)
        : this(tokenStore, uiStateStore, logger,
            BuildHttpCompletion(httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory)), logger))
    {
    }

    /// <summary>Core/test ctor — a fake <paramref name="completion"/> replaces the network; the prompt
    /// directory + asset opener are injectable so override precedence is unit-testable.</summary>
    internal EnrichRegionsLlmService(
        IAnthropicTokenStore tokenStore,
        IUiStateStore uiStateStore,
        ILogger<EnrichRegionsLlmService> logger,
        EnrichCompletion completion,
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

    public async Task<LlmVariantResult> EnrichAsync(
        V4JsonPrompt baseCaption, ModelTier tier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(baseCaption);

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
            _logger.LogError(ex, "EnrichRegionsLlm: failed to load the enrichment system prompt");
            return LlmVariantResult.Fail("Couldn't load the enrichment instructions. See app.log.");
        }

        var graph = RegionGraph.Compute(baseCaption);
        var messages = new List<ChatTurn> { new("user", BuildEnrichUserTurn(baseCaption, graph)) };

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
                _logger.LogError(ex, "EnrichRegionsLlm: model call failed (tier {Tier}, attempt {Attempt})", tier, attempt);
                return LlmVariantResult.Fail($"The enrichment couldn't reach the model: {ex.Message}");
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

            // Schema/semantic validity first, then the enrichment-only invariant. Both feed the same retry.
            var errors = V4JsonPromptValidator.Validate(prompt);
            if (errors.Count == 0)
                errors = EnrichmentPreservation.Check(baseCaption, prompt);

            if (errors.Count == 0)
                return LlmVariantResult.Ok(prompt, BuildLabel(prompt));

            if (attempt >= MaxAttempts)
                return LlmVariantResult.Fail("The enriched prompt didn't satisfy the rules:\n• " + string.Join("\n• ", errors));

            messages.Add(new("assistant", raw));
            messages.Add(new("user",
                "The result had these problems:\n• " + string.Join("\n• ", errors)
                + "\nRewrite ONLY the element descriptions; return only the corrected JSON object that fixes them."));
        }

        // Unreachable: the loop returns on the final attempt either way.
        return LlmVariantResult.Fail("The enrichment gave up after a retry.");
    }

    private string ResolveModelId(ModelTier tier) => tier switch
    {
        ModelTier.Opus => OpusModelId,
        ModelTier.Local => _uiStateStore.LoadOllamaModel() is { Length: > 0 } m ? m : ModelConstants.Ollama.DefaultModel,
        _ => SonnetModelId
    };

    /// <summary>Compose the user turn: the base caption JSON + a compact, geometry-grounded facts block.</summary>
    internal static string BuildEnrichUserTurn(V4JsonPrompt baseCaption, RegionGraphResult graph)
    {
        var elements = baseCaption.CompositionalDeconstruction?.Elements ?? [];
        var sb = new StringBuilder();
        sb.AppendLine("BASE CAPTION (Ideogram V4 JSON):");
        sb.AppendLine(V4JsonPromptSerializer.Serialize(baseCaption));
        sb.AppendLine();
        sb.AppendLine("SPATIAL FACTS (ground truth derived from the bboxes — authoritative for geometry):");

        sb.AppendLine("ELEMENTS:");
        foreach (var fact in graph.Elements)
        {
            var identity = Identity(fact.Index, elements);
            if (!fact.IsPlaced)
            {
                sb.AppendLine($"[#{fact.Index}] {identity} — unplaced (no bbox); describe without spatial relations.");
                continue;
            }

            var span = fact.SpansMultipleBands ? " (spans multiple bands)" : string.Empty;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"[#{fact.Index}] {identity} — {Zone(fact.Horizontal)} third; {Band(fact.Band)} band{span}; " +
                $"center x{fact.CenterXFraction:F2} y{fact.CenterYFraction:F2}; area {fact.AreaFraction * 100:F0}% of frame."));
        }

        if (graph.Relations.Count > 0)
        {
            sb.AppendLine("RELATIONS:");
            foreach (var rel in graph.Relations)
                sb.AppendLine(RelationLine(rel));
        }

        sb.AppendLine();
        sb.Append("ENRICHMENT REQUEST: rewrite each element's desc so it reflects these spatial relationships ")
          .Append("(relative position, what it rests on or leans against, which background band it sits against, ")
          .Append("and overlap). Treat the facts as ground truth, but decide any front/behind using the DEPTH CUE ")
          .Append("TOGETHER with each desc's own wording — never from element order. Change ONLY the element descs; ")
          .Append("keep types, text, bboxes, palettes, the high_level_description, style_description and background ")
          .Append("exactly as given. Return ONLY the resulting V4 JSON object.");
        return sb.ToString();
    }

    private static string RelationLine(RegionRelation rel)
    {
        var parts = new List<string>(3);

        if (rel.Support != SupportRelation.None)
            parts.Add($"#{rel.FromIndex} {SupportPhrase(rel.Support)} #{rel.ToIndex}");

        parts.Add($"#{rel.FromIndex} is {PositionPhrase(rel.Position)} #{rel.ToIndex}");

        if (rel.Overlaps)
        {
            var overlap = string.Create(CultureInfo.InvariantCulture, $"#{rel.FromIndex} overlaps #{rel.ToIndex} (IoU {rel.Iou:F2})");
            if (rel.FromDepthCue is { } cue)
                overlap += $". DEPTH CUE: {DepthPhrase(rel.FromIndex, rel.ToIndex, cue)} — SOFT hint; decide front/behind from the descriptions too";
            parts.Add(overlap);
        }

        return "• " + string.Join("; ", parts) + ".";
    }

    private static string Identity(int index, IReadOnlyList<Element> elements)
    {
        if (index < 0 || index >= elements.Count) return "element";
        var e = elements[index];
        if (string.Equals(e.Type, Element.TextType, StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(e.Text))
            return $"text \"{Truncate(e.Text)}\"";
        return string.IsNullOrWhiteSpace(e.Desc) ? e.Type : $"{e.Type} \"{Truncate(e.Desc)}\"";
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= IdentityMaxLength ? trimmed : trimmed[..IdentityMaxLength].TrimEnd() + "…";
    }

    private static string Zone(HorizontalZone? zone) => zone switch
    {
        HorizontalZone.Left => "left",
        HorizontalZone.Right => "right",
        _ => "center"
    };

    private static string Band(VerticalBand? band) => band switch
    {
        VerticalBand.Sky => "upper/sky",
        VerticalBand.Ground => "lower/ground",
        _ => "middle"
    };

    private static string PositionPhrase(RelativePosition pos) => pos switch
    {
        RelativePosition.LeftOf => "left of",
        RelativePosition.RightOf => "right of",
        RelativePosition.Above => "above",
        RelativePosition.Below => "below",
        _ => "roughly centered on"
    };

    private static string SupportPhrase(SupportRelation support) => support switch
    {
        SupportRelation.RestingOn => "rests on",
        SupportRelation.LeaningAgainst => "leans against",
        SupportRelation.Mounted => "is mounted on",
        _ => "touches"
    };

    private static string DepthPhrase(int from, int to, DepthCue cue) => cue switch
    {
        DepthCue.LikelyNearer => $"#{from} leans nearer than #{to} (larger and lower in frame)",
        DepthCue.LikelyFarther => $"#{from} leans farther than #{to} (smaller and higher in frame)",
        _ => $"#{from} vs #{to} is ambiguous"
    };

    /// <summary>Short job-card label: the (preserved) high-level description, trimmed.</summary>
    private static string BuildLabel(V4JsonPrompt prompt)
    {
        var hld = prompt.HighLevelDescription?.Trim();
        if (string.IsNullOrEmpty(hld)) return "Enriched variant";
        var body = hld.Length <= LabelMaxLength ? hld : hld[..LabelMaxLength].TrimEnd() + "…";
        return "Enriched: " + body;
    }

    /// <summary>Private override beats the bundled clean-room default, mirroring the mutator/builder.</summary>
    private async Task<string> LoadPromptAsync()
    {
        var overridePath = Path.Combine(_promptDirectory, OverrideFileName);
        if (File.Exists(overridePath))
            return await File.ReadAllTextAsync(overridePath);

        await using var stream = await _assetOpener(SystemPromptAsset);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static EnrichCompletion BuildHttpCompletion(IHttpClientFactory httpClientFactory, ILogger logger) =>
        (tier, modelId, apiKey, baseUrl, systemPrompt, messages, schema, ct) =>
            tier == ModelTier.Local
                ? OllamaChatTransport.SendAsync(httpClientFactory, logger, baseUrl, modelId, systemPrompt, messages, schema, ct)
                : AnthropicMessagesTransport.SendAsync(httpClientFactory, logger, modelId, apiKey!, systemPrompt, messages, schema, ct);
}
