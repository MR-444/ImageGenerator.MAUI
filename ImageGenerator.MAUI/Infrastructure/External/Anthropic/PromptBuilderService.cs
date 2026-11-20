using System.Text.Json;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Ollama;
using ImageGenerator.MAUI.Infrastructure.External.OpenRouter;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// Builds a V4 structured prompt from a freeform idea using either Anthropic Messages or a local
/// Ollama model. Structured-output support constrains the JSON pass to the schema's <em>shape</em>;
/// the semantic rules the schema can't express (art_style XOR photo, uppercase #RRGGBB, bbox ordering,
/// the ~60-word desc cap) are caught by <see cref="V4JsonPromptValidator"/>, with one retry that feeds
/// the validator's complaints back to the model.
/// <para>
/// The transport is raw <see cref="HttpClient"/> + System.Text.Json against api.anthropic.com — the
/// same shape <c>CivitaiPostingService</c> uses, which carries zero risk to the MAUI win-x64
/// single-file/trimmed publish. The official <c>Anthropic</c> NuGet SDK can replace the
/// <see cref="StructuredCompletion"/> seam later without touching the interface, the validator gate,
/// or the handoff — once its publishing behavior is verified on the trimmed target.
/// </para>
/// </summary>
public sealed class PromptBuilderService : IPromptBuilderService
{
    // VPE pass (idea → prose). Override beats bundled.
    private const string VpeSystemPromptAsset = "PromptBuilder/vpe-system.md";
    private const string VpeOverrideFileName = "vpe-prompt.md";

    // JSON pass (prose → V4). The override keeps its historical name so an existing user file
    // (the 3-yr IP) keeps working unchanged as the JSON-pass prompt.
    private const string JsonSystemPromptAsset = "PromptBuilder/v4-builder-system.md";
    private const string JsonOverrideFileName = "system-prompt.md";

    private const string OverrideReadmeName = "README.txt";
    private const int MaxAttempts = 2;   // initial call + one validator-feedback retry

    // Seeded into the override folder so the open-core override seam is discoverable. Plain text,
    // dropped only if missing — never overwrites a user file, never touches their system-prompt.md.
    private const string OverrideReadmeText =
        """
        "Describe an idea" prompt builder — private overrides
        ======================================================

        The builder can start from text or from a reference image. Each pass has its own override file
        you can drop in this folder:

          1. image-observation.md — Image source pre-pass. Describes a reference image factually before
                                    the normal VPE pass shapes it into a render prompt.
          2. vpe-prompt.md        — Pass 1 (VPE). Turns your idea (or image observation) into a normal
                                    PROSE image prompt that works directly with every plain-prompt model
                                    (Pollinations, Flux, gpt-image, nano-banana) and reads well to a human.
          3. system-prompt.md     — Pass 2 (Ideogram JSON adapter). Maps that prose onto the schema-valid
                                    Ideogram V4 structured caption. Only runs when "Also build an Ideogram
                                    V4 JSON prompt" is ticked.

        When a file is present it REPLACES the matching bundled prompt verbatim; otherwise the app's
        bundled clean-room prompt is used. The prose pass always runs; the JSON pass is optional.

        - Read fresh on every "Build prompt" click — no app restart needed after you edit either file.
        - Model: picked in the app: Claude Sonnet/Opus, or the configured local Ollama model.
        - Delete a file to revert that pass to its bundled prompt.

        This file (README.txt) is just documentation and is safe to delete; the app re-creates it on
        the next build. Your override files are never modified or read by anything but the builder.
        """;

    /// <summary>
    /// Transport seam: hand one prepared prompt-builder call to the backend the target names, with that
    /// provider's key already resolved (null for Local, which takes no auth). When
    /// <paramref name="schema"/> is non-null the call uses structured outputs (the JSON pass); when null
    /// it's a plain text call (the VPE prose pass).
    /// </summary>
    internal delegate Task<string> StructuredCompletion(
        LlmTarget target, string? apiKey,
        string systemPrompt, IReadOnlyList<ChatTurn> messages, JsonElement? schema, CancellationToken ct);

    private readonly IAnthropicTokenStore _anthropicTokenStore;
    private readonly IOpenRouterTokenStore _openRouterTokenStore;
    private readonly ILogger<PromptBuilderService> _logger;
    private readonly Func<string, Task<Stream>> _assetOpener;
    private readonly string _promptDirectory;
    private readonly StructuredCompletion _complete;

    /// <summary>Production ctor (DI): the transport is one Anthropic Messages call over the named
    /// HttpClient. The token store, logger, and factory are all resolved from the container.</summary>
    public PromptBuilderService(
        IAnthropicTokenStore tokenStore,
        IOpenRouterTokenStore openRouterTokenStore,
        ILogger<PromptBuilderService> logger,
        IHttpClientFactory httpClientFactory)
        : this(tokenStore, openRouterTokenStore, logger,
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
    internal PromptBuilderService(
        IAnthropicTokenStore tokenStore,
        IOpenRouterTokenStore openRouterTokenStore,
        ILogger<PromptBuilderService> logger,
        StructuredCompletion completion,
        string? promptDirectoryOverride = null,
        Func<string, Task<Stream>>? assetOpener = null)
    {
        _anthropicTokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _openRouterTokenStore = openRouterTokenStore ?? throw new ArgumentNullException(nameof(openRouterTokenStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _complete = completion ?? throw new ArgumentNullException(nameof(completion));
        _assetOpener = assetOpener ?? FileSystem.OpenAppPackageFileAsync;
        _promptDirectory = string.IsNullOrWhiteSpace(promptDirectoryOverride)
            ? OutputPaths.PromptBuilderDirectory
            : promptDirectoryOverride;
    }

    /// <summary>Pass 1 (VPE): idea → a normal prose prompt. One plain text call, no validator.</summary>
    public async Task<ProseResult> BuildProseAsync(
        string idea,
        LlmTarget target,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idea))
            return ProseResult.Fail("Describe an idea first.");

        if (!target.HasModel)
            return ProseResult.Fail(target.MissingModelError("on the Describe an idea page."));

        var (apiKey, credentialError) = await LlmCredentials.ResolveAsync(target.Provider, _anthropicTokenStore, _openRouterTokenStore);
        if (credentialError is not null)
            return ProseResult.Fail(credentialError);


        string systemPrompt;
        try
        {
            systemPrompt = await LoadPromptAsync(VpeOverrideFileName, VpeSystemPromptAsset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromptBuilder: failed to load the VPE system prompt");
            return ProseResult.Fail("Couldn't load the prompt-builder instructions. See app.log.");
        }

        var messages = new List<ChatTurn> { new("user", idea.Trim()) };

        string raw;
        try
        {
            // schema = null → a plain text call; the VPE pass emits prose, not JSON.
            raw = await _complete(target, apiKey, systemPrompt, messages, null, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromptBuilder: VPE call failed");
            return ProseResult.Fail($"The prompt builder couldn't reach {target.ProviderLabel}: {ex.Message}");
        }

        var prose = raw.Trim();
        if (string.IsNullOrEmpty(prose))
            return ProseResult.Fail($"{target.ProviderLabel} returned an empty prompt. Try rephrasing the idea.");

        return ProseResult.Ok(prose);
    }

    /// <summary>Pass 2 (Ideogram adapter): prose → a schema-valid V4 prompt with one validator-feedback retry.</summary>
    public async Task<PromptBuilderResult> BuildJsonAsync(
        string proseInner,
        LlmTarget target,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(proseInner))
            return PromptBuilderResult.Fail("Build a prose prompt first.");

        if (!target.HasModel)
            return PromptBuilderResult.Fail(target.MissingModelError("on the Describe an idea page."));

        var (apiKey, credentialError) = await LlmCredentials.ResolveAsync(target.Provider, _anthropicTokenStore, _openRouterTokenStore);
        if (credentialError is not null)
            return PromptBuilderResult.Fail(credentialError);

        string systemPrompt;
        try
        {
            systemPrompt = await LoadPromptAsync(JsonOverrideFileName, JsonSystemPromptAsset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PromptBuilder: failed to load the JSON system prompt");
            return PromptBuilderResult.Fail("Couldn't load the prompt-builder instructions. See app.log.");
        }

        var messages = new List<ChatTurn> { new("user", proseInner.Trim()) };

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            string raw;
            try
            {
                raw = await _complete(
                    target, apiKey, systemPrompt, messages, V4StructuredSchema.Schema, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PromptBuilder: model call failed ({Provider} {Model}, attempt {Attempt})", target.ProviderLabel, target.ModelId, attempt);
                return PromptBuilderResult.Fail($"The prompt builder couldn't reach {target.ProviderLabel}: {ex.Message}");
            }

            V4JsonPrompt prompt;
            try
            {
                prompt = V4JsonPromptSerializer.Deserialize(raw);
            }
            catch (V4JsonPromptParseException ex)
            {
                if (attempt >= MaxAttempts)
                    return PromptBuilderResult.Fail(
                        $"{target.ProviderLabel} returned text that isn't a valid structured prompt: {ex.Message}");

                messages.Add(new("assistant", raw));
                messages.Add(new("user",
                    $"That was not valid JSON for the schema: {ex.Message}. Return only the corrected JSON object."));
                continue;
            }

            var errors = V4JsonPromptValidator.Validate(prompt);
            if (errors.Count == 0)
                return PromptBuilderResult.Ok(prompt);

            if (attempt >= MaxAttempts)
                return PromptBuilderResult.Fail(
                    $"{target.ProviderLabel}'s prompt didn't satisfy the schema:\n• " + string.Join("\n• ", errors));

            messages.Add(new("assistant", raw));
            messages.Add(new("user",
                "The JSON had these problems:\n• " + string.Join("\n• ", errors)
                + "\nReturn only the corrected JSON object that fixes them."));
        }

        // Unreachable: the loop returns on the final attempt either way.
        return PromptBuilderResult.Fail("The prompt builder gave up after a retry.");
    }

    /// <summary>
    /// Private override beats the bundled clean-room default: if the named override file exists in the
    /// prompt-builder folder (the user's IP, outside the repo), use it verbatim; otherwise read the
    /// bundled prompt from <c>Resources/Raw/PromptBuilder</c>. Used by both passes with their own file.
    /// </summary>
    private async Task<string> LoadPromptAsync(string overrideFileName, string bundledAsset)
    {
        EnsureOverrideReadme();

        var overridePath = Path.Combine(_promptDirectory, overrideFileName);
        if (File.Exists(overridePath))
            return await File.ReadAllTextAsync(overridePath);

        await using var stream = await _assetOpener(bundledAsset);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// Best-effort, idempotent: make the override folder exist and drop a README.txt explaining how to
    /// supply a private <c>system-prompt.md</c>. Additive — it creates the folder (no-op if present)
    /// and writes README.txt only when missing, and it never reads, writes, or touches the user's
    /// <c>system-prompt.md</c>. Failures are logged and swallowed (mirrors
    /// <c>MutationLibraryService.SeedIfMissingAsync</c>); a missing README must never break a build.
    /// </summary>
    private void EnsureOverrideReadme()
    {
        try
        {
            Directory.CreateDirectory(_promptDirectory);

            var readmePath = Path.Combine(_promptDirectory, OverrideReadmeName);
            if (!File.Exists(readmePath))
                File.WriteAllText(readmePath, OverrideReadmeText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PromptBuilder: could not seed the override folder README Dir={Dir}", _promptDirectory);
        }
    }

    private static StructuredCompletion BuildHttpCompletion(IHttpClientFactory httpClientFactory, ILogger logger) =>
        (target, apiKey, systemPrompt, messages, schema, ct) => target.Provider switch
        {
            ModelProvider.Local => OllamaChatTransport.SendAsync(httpClientFactory, logger, target.BaseUrl, target.ModelId, systemPrompt, messages, schema, ct),
            ModelProvider.OpenRouter => OpenRouterChatTransport.SendAsync(httpClientFactory, logger, apiKey!, target.ModelId, systemPrompt, messages, schema, ct),
            _ => AnthropicMessagesTransport.SendAsync(httpClientFactory, logger, target.ModelId, apiKey!, systemPrompt, messages, schema, ct)
        };
}
