using System.Globalization;
using System.Runtime.CompilerServices;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class UiStateStore : IUiStateStore
{
    private const string PromptKey = "imggen.last_prompt";
    private const string ModelKey = "imggen.last_model";
    private const string UseJsonPromptKey = "imggen.use_json_prompt";
    private const string UseSageAttentionKey = "imggen.use_sage_attention";
    private const string FreeVramAfterRenderingKey = "imggen.free_vram_after_rendering";
    private const string AppThemeKey = "imggen.app_theme";
    private const string ResolutionKey = "imggen.last_resolution";
    private const string ComfyUiResolutionKey = "imggen.last_resolution.comfyui";
    private const string AspectRatioKey = "imggen.last_aspect_ratio";
    private const string ComfyUiAspectRatioKey = "imggen.last_aspect_ratio.comfyui";
    private const string ComfyUiBaseUrlKey = "imggen.comfyui_base_url";
    private const string OllamaBaseUrlKey = "imggen.ollama_base_url";
    private const string OllamaModelKey = "imggen.ollama_model";
    private const string OllamaVisionModelKey = "imggen.ollama_vision_model";
    private const string OllamaModelCacheKey = "imggen.ollama_model_cache";
    private const string OllamaVisionModelCacheKey = "imggen.ollama_vision_model_cache";
    private const string PromptWriterModelKey = "imggen.prompt_writer_model";
    private const string MutationModelKey = "imggen.mutation_model";
    private const string EnrichModelKey = "imggen.enrich_model";
    private const string OpenRouterVisionModelKey = "imggen.openrouter_vision_model";
    private const string OpenRouterVisionFreeOnlyKey = "imggen.openrouter_vision_free_only";
    private const string IdeaBuildJsonKey = "imggen.idea_build_json";
    private const string CivitaiModelRefKey = "imggen.civitai_model_ref";
    private const string OutputFolderKey = "imggen.output_folder";
    private const string WindowBoundsKey = "imggen.window_bounds";

    private static readonly TimeSpan PromptDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ILogger<UiStateStore> _logger;
    private readonly IPreferences _preferences;
    private readonly DebouncedSecureStorageWriter _promptWriter;

    public UiStateStore(
        ILogger<UiStateStore> logger,
        IPreferences? preferences = null,
        TimeSpan? promptDebounceDelay = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _preferences = preferences ?? Preferences.Default;

        // The prompt is persisted on every Parameters.Prompt change — once per KEYSTROKE
        // when typing by hand. Same debounce as the token stores: only the last value in
        // the window reaches Preferences. The skip-identical rule still applies at write
        // time, so the startup load-echo stays a no-op.
        _promptWriter = new DebouncedSecureStorageWriter(
            PromptKey, promptDebounceDelay ?? PromptDebounceDelay, _logger,
            (k, v) =>
            {
                if (SafeSet(k, v) && _logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(v));
                return Task.CompletedTask;
            });
    }

    public string? LoadPrompt()
    {
        var v = SafeGet(PromptKey);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadPrompt -> {Value}", Quote(v));
        return v;
    }

    public string? LoadModel() => LoadString(ModelKey);

    public void PersistPrompt(string value)
    {
        // Schedule() also cancels any pending stale write. It deliberately DROPS empties
        // (the token stores' clear semantics) — but a cleared prompt must still persist,
        // and an empty can't storm the store, so write that one immediately.
        _promptWriter.Schedule(value);
        if (string.IsNullOrEmpty(value) && SafeSet(PromptKey, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistPrompt({Value})", Quote(value));
    }

    public void FlushPendingWrites() => _promptWriter.Flush();

    public void PersistModel(string value) => PersistString(ModelKey, value);

    public string? LoadResolution(string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        // ComfyUI reads fall back to the old family-wide key so a pre-existing pick carries
        // over until the first per-workflow pick replaces it.
        var v = SafeGet(key)
                ?? (Shared.Constants.ModelConstants.ComfyUi.IsId(modelId) ? SafeGet(ComfyUiResolutionKey) : null);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadResolution[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistResolution(string value, string? modelId)
    {
        var key = ResolutionKeyFor(modelId);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistResolution[{Key}]({Value})", key, Quote(value));
    }

    // ComfyUI's MP presets and the other models' "WxH"-style strings are different option
    // families — each gets its own key so model switches restore that family's last pick.
    // ComfyUI is additionally keyed PER WORKFLOW: models differ in how much resolution they
    // tolerate (Krea-2 turbo renders 4 MP, raw smears above 2 MP), so one workflow's pick
    // must not leak into another. The legacy key stays the default family, keeping
    // previously saved data valid.
    private static string ResolutionKeyFor(string? modelId) =>
        Shared.Constants.ModelConstants.ComfyUi.IsId(modelId)
            ? $"{ComfyUiResolutionKey}.{Shared.Constants.ModelConstants.ComfyUi.WorkflowName(modelId!)}"
            : ResolutionKey;

    public string? LoadAspectRatio(string? modelId)
    {
        var key = AspectRatioKeyFor(modelId);
        var v = SafeGet(key);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadAspectRatio[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistAspectRatio(string value, string? modelId)
    {
        var key = AspectRatioKeyFor(modelId);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistAspectRatio[{Key}]({Value})", key, Quote(value));
    }

    // Aspect-ratio option sets differ by model family (a ComfyUI workflow defines its own), so key
    // per family like resolution — a portrait pick on ComfyUI won't override a landscape pick on
    // the Replicate models, and a switch back restores that family's last choice.
    private static string AspectRatioKeyFor(string? modelId) =>
        Shared.Constants.ModelConstants.ComfyUi.IsId(modelId) ? ComfyUiAspectRatioKey : AspectRatioKey;

    public string? LoadComfyUiPreset(string workflowName)
    {
        var key = ComfyUiPresetKeyFor(workflowName);
        var v = SafeGet(key);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadComfyUiPreset[{Key}] -> {Value}", key, Quote(v));
        return v;
    }

    public void PersistComfyUiPreset(string value, string workflowName)
    {
        var key = ComfyUiPresetKeyFor(workflowName);
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistComfyUiPreset[{Key}]({Value})", key, Quote(value));
    }

    // Per-workflow like checkpoints: the labels are the workflow's own CustomCombo options.
    private static string ComfyUiPresetKeyFor(string workflowName) =>
        $"imggen.comfyui_preset.{workflowName}";

    public string? LoadComfyUiLora(string workflowName) =>
        LoadString($"imggen.comfyui_lora.{workflowName}");

    public void PersistComfyUiLora(string value, string workflowName) =>
        PersistString($"imggen.comfyui_lora.{workflowName}", value);

    // Per-workflow like resolution/preset: whether a render is worth the extra upscale pass
    // is a per-model habit, so one workflow's checkbox must not leak into another.
    public bool LoadComfyUiUpscaleAfter(string workflowName) =>
        LoadBool($"imggen.comfyui_upscale_after.{workflowName}", false);

    public void PersistComfyUiUpscaleAfter(bool value, string workflowName) =>
        PersistBool($"imggen.comfyui_upscale_after.{workflowName}", value, false);

    // Keyed by the workflow whose UltimateSDUpscale gets patched. Stored as the invariant
    // numeric string ("2", "1.5") — never a culture-formatted label.
    public string? LoadComfyUiUpscaleFactor(string workflowName) =>
        LoadString($"imggen.comfyui_upscale_factor.{workflowName}");

    public void PersistComfyUiUpscaleFactor(string value, string workflowName) =>
        PersistString($"imggen.comfyui_upscale_factor.{workflowName}", value);

    public (double Width, double Height, double X, double Y)? LoadWindowBounds()
    {
        var raw = SafeGet(WindowBoundsKey);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.LoadWindowBounds -> {Value}", Quote(raw));
        if (raw is null) return null;

        // "w;h;x;y" in invariant culture. Anything malformed (old format, manual edit,
        // culture drift) degrades to "never persisted" rather than throwing at startup.
        var parts = raw.Split(';');
        if (parts.Length != 4) return null;
        var values = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
                || !double.IsFinite(values[i]))
                return null;
        }
        return (values[0], values[1], values[2], values[3]);
    }

    public void PersistWindowBounds(double width, double height, double x, double y)
    {
        var value = string.Create(CultureInfo.InvariantCulture, $"{width};{height};{x};{y}");
        if (SafeSet(WindowBoundsKey, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.PersistWindowBounds({Value})", value);
    }

    public string? LoadComfyUiBaseUrl() => LoadString(ComfyUiBaseUrlKey);

    public void PersistComfyUiBaseUrl(string value) => PersistString(ComfyUiBaseUrlKey, value);

    public string? LoadOllamaBaseUrl() => LoadString(OllamaBaseUrlKey);

    public void PersistOllamaBaseUrl(string value) => PersistString(OllamaBaseUrlKey, value);

    public string? LoadOllamaModel() => LoadString(OllamaModelKey);

    public void PersistOllamaModel(string value) => PersistString(OllamaModelKey, value);

    public string? LoadOllamaVisionModel() => LoadString(OllamaVisionModelKey);

    public void PersistOllamaVisionModel(string value) => PersistString(OllamaVisionModelKey, value);

    // One Ollama tag per LLM role. Until documents/plan-model-picker-rows.md all three read
    // OllamaModelKey, so choosing a cheap tag to verify a caption mutation also changed the model
    // behind the generate button.
    public string? LoadPromptWriterModel() => LoadRoleModel(PromptWriterModelKey);

    public void PersistPromptWriterModel(string value) => PersistString(PromptWriterModelKey, value);

    /// <inheritdoc cref="LoadPromptWriterModel" />
    public string? LoadMutationModel() => LoadRoleModel(MutationModelKey);

    public void PersistMutationModel(string value) => PersistString(MutationModelKey, value);

    /// <inheritdoc cref="LoadPromptWriterModel" />
    public string? LoadEnrichModel() => LoadRoleModel(EnrichModelKey);

    public void PersistEnrichModel(string value) => PersistString(EnrichModelKey, value);

    // Which provider each text role calls, and the model it calls per provider. See
    // IUiStateStore.LoadLlmProvider / LoadLlmModel for why the model is keyed by provider too.
    //
    // Every arm is written out, and an unlisted role throws. These keys are the difference between four
    // pages remembering four choices and one page overwriting another's, so a fifth role quietly landing
    // on the enrich keys is not a failure this file should be able to have.
    private static string ProviderKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer_provider",
        ModelRole.Mutate => "imggen.mutation_provider",
        ModelRole.Enrich => "imggen.enrich_provider",
        // The observer used to have no provider key at all: its picker reset to Ollama on every
        // navigation, however often it was used, because nothing was ever written.
        ModelRole.Observe => "imggen.observe_provider",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No provider key for this role.")
    };

    private static string AnthropicModelKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer.anthropic_model",
        ModelRole.Mutate => "imggen.mutation.anthropic_model",
        ModelRole.Enrich => "imggen.enrich.anthropic_model",
        // Unreachable by design, so it is a fault rather than a key: nothing in this build can send an
        // image to Claude, so the observe role is never offered Anthropic. A role that gains a provider
        // should fail here rather than invent a pref nobody wrote and nobody can clear.
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "This role has no Anthropic model.")
    };

    private static string OpenRouterModelKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer.openrouter_model",
        ModelRole.Mutate => "imggen.mutation.openrouter_model",
        ModelRole.Enrich => "imggen.enrich.openrouter_model",
        // The observer keeps the key it has always used. A role that names the same decision as the old
        // picker needs no migration, and none of the seeding that would have come with a new key.
        ModelRole.Observe => OpenRouterVisionModelKey,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No OpenRouter key for this role.")
    };

    public ModelProvider LoadLlmProvider(ModelRole role, ModelProvider fallback)
    {
        var key = ProviderKey(role);
        if (RoleKeyIsStored(key))
        {
            // An enum written as text outlives renames; degrade to the caller's fallback rather than
            // throwing on startup, which is what the tier picker did with an unrecognised stored value.
            return Enum.TryParse<ModelProvider>(LoadString(key), out var parsed) ? parsed : fallback;
        }

        // First run after the provider split: inherit what the prompt writer used to remember as a tier
        // (the only role that ever did), so "I run this on Ollama" and "I run this on Sonnet" survive the
        // upgrade; every other role starts on the fallback its host asked for.
        ModelProvider? inherited = null;
        if (role == ModelRole.PromptWrite && LoadLegacyPromptWriterTier() is { } legacyTier)
        {
            inherited = legacyTier == LegacyTierLocal ? ModelProvider.Local : ModelProvider.Anthropic;
            AdoptLegacyTierModel(role, legacyTier);
        }
        var adopted = inherited ?? fallback;
        PersistString(key, adopted.ToString());
        _logger.LogDebug("UiStateStore.{Key} adopted {Provider} once", key, adopted);
        return adopted;
    }

    public void PersistLlmProvider(ModelRole role, ModelProvider value) => PersistString(ProviderKey(role), value.ToString());

    public string? LoadLlmModel(ModelRole role, ModelProvider provider) => provider switch
    {
        // The Local slot is the per-role key from Phase A, seeded once from the generation model. That
        // seeding is correct here and only here: the generation model IS an Ollama tag.
        ModelProvider.Local => role switch
        {
            ModelRole.PromptWrite => LoadPromptWriterModel(),
            ModelRole.Mutate => LoadMutationModel(),
            ModelRole.Enrich => LoadEnrichModel(),
            // Same argument as the generation models: the observer's tag is an Ollama tag, and it is the
            // one the old vision picker already remembered. Reusing it is why this role needs no seeding.
            ModelRole.Observe => LoadOllamaVisionModel(),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No local model key for this role.")
        },
        // A default Claude model is honest — the app has always named one. There is no default
        // OpenRouter slug: the catalog is hundreds deep and any guess bills a credit to someone.
        ModelProvider.Anthropic => LoadString(AnthropicModelKey(role)) ?? AnthropicModels.DefaultFor(role),
        _ => LoadString(OpenRouterModelKey(role))
    };

    public void PersistLlmModel(ModelRole role, ModelProvider provider, string? value)
    {
        var key = provider switch
        {
            ModelProvider.Local => role switch
            {
                ModelRole.PromptWrite => PromptWriterModelKey,
                ModelRole.Mutate => MutationModelKey,
                ModelRole.Enrich => EnrichModelKey,
                ModelRole.Observe => OllamaVisionModelKey,
                _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No local model key for this role.")
            },
            ModelProvider.Anthropic => AnthropicModelKey(role),
            _ => OpenRouterModelKey(role)
        };
        PersistString(key, value ?? string.Empty);
    }

    public LlmTarget LoadLlmTarget(ModelRole role, ModelProvider fallback)
    {
        var provider = LoadLlmProvider(role, fallback);
        return new LlmTarget(
            provider,
            LoadLlmModel(role, provider) ?? string.Empty,
            provider == ModelProvider.Local ? ModelConstants.Ollama.ResolveBaseUrl(LoadOllamaBaseUrl()) : string.Empty);
    }
    // --- The tier fossil ------------------------------------------------------------------
    // Nothing in this app writes imggen.prompt_writer_tier since the tier pickers went: ModelTier is
    // deleted, and each role now remembers a provider and a model separately. The key still exists on
    // installs that ran a build before the role pickers, and it is read once, on that role's first read,
    // so such an install resumes where it left off instead of on nothing. Stored as the old enum's int.
    // When the owner decides those installs no longer matter, this whole block - key, reader, adopter and
    // their tests - is one deletion.

    private const string PromptWriterTierKey = "imggen.prompt_writer_tier";
    private const int LegacyTierSonnet = 0;
    private const int LegacyTierOpus = 1;
    private const int LegacyTierLocal = 2;

    /// <summary>The stored tier, or null when the key is absent, empty, holds the old "never picked" -1,
    /// or holds something this app never wrote. Reading a fossil must not be able to break a startup.</summary>
    private int? LoadLegacyPromptWriterTier()
    {
        try
        {
            var stored = _preferences.Get(PromptWriterTierKey, -1);
            return stored is LegacyTierSonnet or LegacyTierOpus or LegacyTierLocal ? stored : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", PromptWriterTierKey);
            return null;
        }
    }

    /// <summary>
    /// A tier named a provider and a model in one word, so inheriting only the provider would be half a
    /// migration. "Sonnet" became "Anthropic", and the model then fell to this role's default — Opus —
    /// which is a silent upgrade to the pricier model made by a reader, on the first launch after an
    /// update. Write down the model the tier named, where no model has ever been written, and the upgrade
    /// keeps the model it already had.
    /// </summary>
    private void AdoptLegacyTierModel(ModelRole role, int tier)
    {
        if (tier == LegacyTierLocal) return;
        var key = AnthropicModelKey(role);
        if (RoleKeyIsStored(key)) return;
        PersistString(key, tier == LegacyTierOpus ? AnthropicModels.Opus : AnthropicModels.Sonnet);
        _logger.LogDebug("UiStateStore.{Key} adopted the model its {Tier} tier named once", key, tier);
    }

    /// <summary>
    /// Read-through seeding, and the only reason a Load ever writes: a role with a value of its own
    /// keeps it; a role whose key has never been written adopts the generation model ONCE and writes
    /// it down, so this update changes nobody's behaviour on first launch. Written on the first read
    /// is what makes it once — afterwards the role owns the value, and clearing it stays cleared.
    /// </summary>
    private string? LoadRoleModel(string key)
    {
        // ContainsKey rather than "did something come back": this store collapses an empty value to
        // null on read, and a role cleared because its model left the server must stay cleared rather
        // than silently re-borrowing whatever the generate button renders with.
        if (RoleKeyIsStored(key)) return LoadString(key);

        var seed = LoadString(OllamaModelKey);
        if (string.IsNullOrWhiteSpace(seed)) return null;

        PersistString(key, seed);
        _logger.LogDebug("UiStateStore.{Key} adopted the generation model {Model} once", key, seed);
        return seed;
    }

    private bool RoleKeyIsStored(string key)
    {
        try
        {
            return _preferences.ContainsKey(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UiStateStore.ContainsKey({Key}) failed", key);
            return false;
        }
    }

    public IReadOnlyList<string> LoadOllamaModels(string baseUrl) =>
        LoadModelCache(OllamaModelCacheKey, baseUrl);

    public IReadOnlyList<string> LoadOllamaVisionModels(string baseUrl) =>
        LoadModelCache(OllamaVisionModelCacheKey, baseUrl);

    public void PersistOllamaModels(string baseUrl, IReadOnlyList<string> models, IReadOnlyList<string> visionModels)
    {
        PersistModelCache(OllamaModelCacheKey, baseUrl, models);
        PersistModelCache(OllamaVisionModelCacheKey, baseUrl, visionModels);
    }

    // Newline-joined, with the server URL as the FIRST line. Storing the URL inside the value
    // (rather than mangling it into the key) makes the cache self-invalidating on a server switch
    // and leaves exactly one Preferences entry per list to clean up. Ollama model tags cannot
    // contain a newline, so the split is unambiguous.
    private IReadOnlyList<string> LoadModelCache(string key, string baseUrl)
    {
        var raw = SafeGet(key);
        if (raw is null) return [];

        var lines = raw.Split('\n');
        if (!string.Equals(lines[0], baseUrl, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("UiStateStore.LoadModelCache({Key}) -> stale (cached for {Cached})", key, lines[0]);
            return [];
        }

        var models = lines.Skip(1).Where(l => l.Length > 0).ToArray();
        _logger.LogDebug("UiStateStore.LoadModelCache({Key}) -> {Count} model(s)", key, models.Length);
        return models;
    }

    private void PersistModelCache(string key, string baseUrl, IReadOnlyList<string> models) =>
        PersistString(key, string.Join('\n', models.Prepend(baseUrl)));

    public string? LoadOpenRouterVisionModel() => LoadString(OpenRouterVisionModelKey);

    public void PersistOpenRouterVisionModel(string value) => PersistString(OpenRouterVisionModelKey, value);

    // Default TRUE — show only free OpenRouter vision models unless the user opts in to paid ones.
    public bool LoadOpenRouterVisionFreeOnly() => LoadBool(OpenRouterVisionFreeOnlyKey, true);

    public void PersistOpenRouterVisionFreeOnly(bool value) =>
        PersistBool(OpenRouterVisionFreeOnlyKey, value, true);

    public bool? LoadIdeaBuildJson()
    {
        try
        {
            // Tri-state: "never toggled" must stay distinguishable so the page can default the
            // checkbox from the active model exactly once, then honor the stored choice forever.
            var value = _preferences.ContainsKey(IdeaBuildJsonKey)
                ? _preferences.Get(IdeaBuildJsonKey, false)
                : (bool?)null;
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.LoadIdeaBuildJson -> {Value}", value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", IdeaBuildJsonKey);
            return null;
        }
    }

    public void PersistIdeaBuildJson(bool value) => PersistBool(IdeaBuildJsonKey, value, false);

    public string? LoadOutputFolder() => LoadString(OutputFolderKey);

    public void PersistOutputFolder(string value) => PersistString(OutputFolderKey, value);

    public string? LoadCivitaiModelRef() => LoadString(CivitaiModelRefKey);

    public void PersistCivitaiModelRef(string value) => PersistString(CivitaiModelRefKey, value);

    public bool LoadUseJsonPrompt() => LoadBool(UseJsonPromptKey, false);

    public void PersistUseJsonPrompt(bool value) => PersistBool(UseJsonPromptKey, value, false);

    public bool LoadUseSageAttention() => LoadBool(UseSageAttentionKey, false);

    public void PersistUseSageAttention(bool value) => PersistBool(UseSageAttentionKey, value, false);

    // Default TRUE — free GPU memory after each render unless the user opted out.
    public bool LoadFreeVramAfterRendering() => LoadBool(FreeVramAfterRenderingKey, true);

    public void PersistFreeVramAfterRendering(bool value) =>
        PersistBool(FreeVramAfterRenderingKey, value, true);

    public AppTheme LoadAppTheme()
    {
        try
        {
            // Stored as the AppTheme enum int. Default Unspecified = "System" (follow OS), the app's
            // original behavior. Validate the stored int: a corrupt/foreign value degrades to
            // Unspecified rather than feeding an undefined enum into UserAppTheme at startup.
            var stored = _preferences.Get(AppThemeKey, (int)AppTheme.Unspecified);
            var theme = stored switch
            {
                (int)AppTheme.Light => AppTheme.Light,
                (int)AppTheme.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified
            };
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.LoadAppTheme -> {Value}", theme);
            return theme;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", AppThemeKey);
            return AppTheme.Unspecified;
        }
    }

    public void PersistAppTheme(AppTheme value)
    {
        try
        {
            if (_preferences.ContainsKey(AppThemeKey)
                && _preferences.Get(AppThemeKey, (int)AppTheme.Unspecified) == (int)value) return;
            _preferences.Set(AppThemeKey, (int)value);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.PersistAppTheme({Value})", value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", AppThemeKey);
        }
    }

    // Truncated: the JSON prompt runs to several KB and used to be dumped verbatim into
    // app.log on every load AND persist — the value's head plus its length is enough to
    // identify it.
    private static string Quote(string? v) =>
        v is null ? "<null>"
        : v.Length <= 100 ? $"\"{v}\""
        // AsSpan, not v[..100]: the interpolation handler appends the ReadOnlySpan<char>
        // directly, so the truncated head never allocates a throwaway substring.
        : $"\"{v.AsSpan(0, 100)}…\" ({v.Length} chars)";

    private string? SafeGet(string key)
    {
        try
        {
            // ContainsKey-then-Get avoids any ambiguity around how the platform handler treats
            // a `null` defaultValue for `Get<string?>` — some implementations coerce missing
            // keys to "" instead of null, which would defeat the IsNullOrEmpty guard upstream.
            if (!_preferences.ContainsKey(key)) return null;
            var value = _preferences.Get(key, string.Empty);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", key);
            return null;
        }
    }

    /// <summary>True when the value was written; false when skipped (already stored) or failed.</summary>
    private bool SafeSet(string key, string value)
    {
        try
        {
            // Skip identical rewrites: every startup Load echoes straight back through the
            // VM's PropertyChanged persist hooks (LoadPrompt -> Parameters.Prompt ->
            // PersistPrompt with the very value just read), and bound controls re-push
            // unchanged values. Preferences.Set writes through to the backing store each
            // call, so the skip saves the I/O — and its multi-KB log line.
            if (_preferences.ContainsKey(key) && _preferences.Get(key, string.Empty) == value)
                return false;
            _preferences.Set(key, value);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", key);
            return false;
        }
    }

    // Per-accessor Load/Persist for the plain single-key settings all share one shape: read
    // (or skip-identical write) through SafeGet/SafeSet, then a debug line naming the accessor.
    // [CallerMemberName] supplies that name so the log reads "UiStateStore.LoadModel -> …" as
    // before, without each method spelling it out. (Debounced Prompt, the per-family keyed
    // settings, and the validated enum settings keep their own bodies — they don't fit this shape.)
    private string? LoadString(string key, [CallerMemberName] string caller = "")
    {
        var v = SafeGet(key);
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.{Caller} -> {Value}", caller, Quote(v));
        return v;
    }

    private void PersistString(string key, string value, [CallerMemberName] string caller = "")
    {
        if (SafeSet(key, value) && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("UiStateStore.{Caller}({Value})", caller, Quote(value));
    }

    private bool LoadBool(string key, bool defaultValue, [CallerMemberName] string caller = "")
    {
        try
        {
            var value = _preferences.Get(key, defaultValue);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.{Caller} -> {Value}", caller, value);
            return value;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Get({Key}) failed", key);
            return defaultValue;
        }
    }

    private void PersistBool(string key, bool value, bool defaultValue, [CallerMemberName] string caller = "")
    {
        try
        {
            // Skip identical rewrites like SafeSet (bool overload of Get).
            if (_preferences.ContainsKey(key) && _preferences.Get(key, defaultValue) == value) return;
            _preferences.Set(key, value);
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("UiStateStore.{Caller}({Value})", caller, value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Preferences.Set({Key}) failed", key);
        }
    }
}
