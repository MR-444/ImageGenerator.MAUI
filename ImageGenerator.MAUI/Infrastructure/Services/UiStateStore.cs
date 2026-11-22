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
    private const string OpenRouterFreeOnlyKey = "imggen.openrouter_vision_free_only";
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
        // ComfyUI falls back to the family-wide key until this workflow has a pick of its own.
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
    // must not leak into another.
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

    public string? LoadGptOption(string field, string? modelId) =>
        LoadString(GptOptionKeyFor(field, modelId));

    public void PersistGptOption(string field, string value, string? modelId) =>
        PersistString(GptOptionKeyFor(field, modelId), value);

    private static string GptOptionKeyFor(string field, string? modelId) =>
        $"imggen.gpt_option.{field}.{modelId}";

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

        // "w;h;x;y" in invariant culture. Anything malformed degrades to "never persisted"
        // rather than throwing at startup.
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

    // One Ollama tag per LLM role, so a pick on one page doesn't change the model the others call.
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
    // Every arm is written out and an unlisted role throws, so a new role can't silently share
    // another role's keys.
    private static string ProviderKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer_provider",
        ModelRole.Mutate => "imggen.mutation_provider",
        ModelRole.Enrich => "imggen.enrich_provider",
        ModelRole.Observe => "imggen.observe_provider",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No provider key for this role.")
    };

    private static string AnthropicModelKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer.anthropic_model",
        ModelRole.Mutate => "imggen.mutation.anthropic_model",
        ModelRole.Enrich => "imggen.enrich.anthropic_model",
        // Observe is never offered Anthropic (no image input to Claude in this build), so reaching
        // here is a fault, not a key.
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "This role has no Anthropic model.")
    };

    private static string OpenRouterModelKey(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => "imggen.prompt_writer.openrouter_model",
        ModelRole.Mutate => "imggen.mutation.openrouter_model",
        ModelRole.Enrich => "imggen.enrich.openrouter_model",
        ModelRole.Observe => OpenRouterVisionModelKey,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No OpenRouter key for this role.")
    };

    public ModelProvider LoadLlmProvider(ModelRole role, ModelProvider fallback)
    {
        var key = ProviderKey(role);
        if (RoleKeyIsStored(key))
        {
            // An unrecognised stored value degrades to the caller's fallback rather than throwing on startup.
            return Enum.TryParse<ModelProvider>(LoadString(key), out var parsed) ? parsed : fallback;
        }

        // No provider stored yet: the prompt writer inherits its legacy tier if one is stored;
        // every other role starts on the caller's fallback. Either way the result is written once.
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
        // The Local slot is the role's own key, seeded once from the shared Ollama model (LoadRoleModel).
        ModelProvider.Local => role switch
        {
            ModelRole.PromptWrite => LoadPromptWriterModel(),
            ModelRole.Mutate => LoadMutationModel(),
            ModelRole.Enrich => LoadEnrichModel(),
            ModelRole.Observe => LoadString(OllamaVisionModelKey),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No local model key for this role.")
        },
        // Anthropic defaults to a Claude model; OpenRouter has none, since any guess could bill a paid model.
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

    private const string PromptWriterTierKey = "imggen.prompt_writer_tier";
    private const int LegacyTierSonnet = 0;
    private const int LegacyTierOpus = 1;
    private const int LegacyTierLocal = 2;

    /// <summary>The stored tier, or null when the key is absent, holds -1 ("never picked"), or holds an
    /// unknown value. Never throws.</summary>
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
    /// A tier named both a provider and a model. Writes the tier's model where no Anthropic model is
    /// stored, so a "Sonnet" tier stays on Sonnet instead of falling to the role's default (Opus).
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
    /// Read-through seeding, the only Load that writes: a role whose key was never written adopts the
    /// generation model and stores it, so this happens once; afterwards the role owns the value, and a
    /// cleared value stays cleared.
    /// </summary>
    private string? LoadRoleModel(string key)
    {
        // ContainsKey, not a null check: an empty value reads as null, and a cleared role must not
        // re-adopt the generation model.
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

    // Default TRUE — show only free OpenRouter vision models unless the user opts in to paid ones.
    public bool LoadOpenRouterFreeOnly() => LoadBool(OpenRouterFreeOnlyKey, true);

    public void PersistOpenRouterFreeOnly(bool value) =>
        PersistBool(OpenRouterFreeOnlyKey, value, true);

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
            // Stored as the AppTheme enum int. Default Unspecified = "System" (follow OS). A corrupt
            // value degrades to Unspecified rather than feeding an undefined enum into UserAppTheme.
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

    // Truncated: the JSON prompt runs to several KB; its head plus its length identify it in app.log.
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
    // [CallerMemberName] supplies that name so the log reads "UiStateStore.LoadModel -> …"
    // without each method spelling it out. (Debounced Prompt, the per-family keyed settings,
    // and the validated enum settings keep their own bodies — they don't fit this shape.)
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
