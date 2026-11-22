namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns Preferences-backed persistence of non-secret UI state that should survive an app
/// restart — currently the last-used prompt, model, and the structured-JSON toggle. Mirrors
/// <see cref="IApiTokenStore"/> but uses Preferences rather than SecureStorage since these
/// values aren't credentials.
/// </summary>
public interface IUiStateStore
{
    string? LoadPrompt();
    string? LoadModel();
    /// <summary>
    /// Resolution is persisted per option-format FAMILY, derived from the model id: ComfyUI
    /// models use megapixel presets ("2.0 MP"), everything else shares the legacy key
    /// (Ideogram "WxH" strings etc.). ComfyUI is additionally keyed PER WORKFLOW — models
    /// tolerate different resolutions, so one workflow's pick must not leak into another;
    /// reads fall back to the old family-wide key until a per-workflow pick exists.
    /// </summary>
    string? LoadResolution(string? modelId);
    /// <summary>
    /// Last aspect-ratio pick, persisted per model family (ComfyUI vs the rest) like resolution.
    /// Null when never persisted — callers keep the model's default first option. "custom" is
    /// never persisted (its width/height aren't), so this never returns it.
    /// </summary>
    string? LoadAspectRatio(string? modelId);
    /// <summary>False when never persisted — the toggle is opt-in per session by default.</summary>
    bool LoadUseJsonPrompt();
    /// <summary>
    /// Whether compatible ComfyUI workflows receive runtime SageAttention nodes. False when
    /// never persisted because hosts without KJNodes/sageattention cannot execute the node.
    /// </summary>
    bool LoadUseSageAttention();
    /// <summary>Whether to POST /free to ComfyUI when rendering goes idle, freeing GPU memory for the
    /// local Ollama prompt/AI tools. TRUE when never persisted (default-on; the user can disable it in Settings).</summary>
    bool LoadFreeVramAfterRendering();
    /// <summary>
    /// The user's chosen color theme. <see cref="AppTheme.Unspecified"/> ("System", follow OS) when never
    /// persisted — preserving the app's original OS-following behavior. Any out-of-range stored value
    /// degrades to <see cref="AppTheme.Unspecified"/> rather than throwing.
    /// </summary>
    AppTheme LoadAppTheme();

    /// <summary>Null when never persisted — callers fall back to ModelConstants.ComfyUi.DefaultBaseUrl.</summary>
    string? LoadComfyUiBaseUrl();
    /// <summary>
    /// The local Ollama server URL used by the prompt builder and AI tools' free "Local" tier. Null when never
    /// set — callers fall back to <c>ModelConstants.Ollama.DefaultBaseUrl</c>.
    /// </summary>
    string? LoadOllamaBaseUrl();
    /// <summary>
    /// Last-known model list from the Ollama server, so the pickers show real options at launch
    /// instead of a lone seeded entry — and keep showing them while the server is down. The server
    /// URL is stored WITH the list and checked on load, so a cache belonging to a different Ollama
    /// host is discarded rather than shown. Empty when never cached or when the URL no longer matches.
    /// </summary>
    IReadOnlyList<string> LoadOllamaModels(string baseUrl);
    /// <inheritdoc cref="LoadOllamaModels"/>
    IReadOnlyList<string> LoadOllamaVisionModels(string baseUrl);
    /// <inheritdoc cref="LoadOllamaModels"/>
    void PersistOllamaModels(string baseUrl, IReadOnlyList<string> models, IReadOnlyList<string> visionModels);
    /// <summary>Whether the OpenRouter vision model picker should show free models only. Default true.</summary>
    bool LoadOpenRouterFreeOnly();
    /// <summary>
    /// The "Describe an idea" page's BuildJson checkbox. Null when never toggled — the page then
    /// defaults it from the active model's JSON-prompt support. Once stored, the user's choice wins
    /// over the model default on every later visit.
    /// </summary>
    bool? LoadIdeaBuildJson();
    /// <summary>
    /// The user's configured ROOT data folder. Null when never set — callers fall back to
    /// <c>OutputPaths.DefaultRootDirectory</c>. Every data folder (images under <c>pictures\</c>,
    /// json-prompts, comfy-workflows, mutation-library, prompt-builder) follows this root; only
    /// app.log stays anchored at the default location (it's configured pre-DI, before this loads).
    /// </summary>
    string? LoadOutputFolder();
    /// <summary>
    /// The raw CivitAI model reference text (URL or version id) for gallery-targeted posting.
    /// Persisted across sessions — unlike the post checkboxes — because the target model rarely
    /// changes; the off-by-default checkbox still gates any actual upload. Null when never set.
    /// </summary>
    string? LoadCivitaiModelRef();
    /// <summary>
    /// Per-workflow: preset labels are the workflow's own CustomCombo options, so one
    /// workflow's pick must never leak into another. Null when never explicitly picked.
    /// </summary>
    string? LoadComfyUiPreset(string workflowName);
    /// <summary>
    /// Per-workflow Krea-2 LoRA selection. The value is an exact ComfyUI catalog path or
    /// <c>No LoRA</c>; null when the user has never picked one.
    /// </summary>
    string? LoadComfyUiLora(string workflowName);
    void PersistPrompt(string value);
    void PersistModel(string value);
    /// <inheritdoc cref="LoadResolution"/>
    void PersistResolution(string value, string? modelId);
    /// <inheritdoc cref="LoadAspectRatio"/>
    void PersistAspectRatio(string value, string? modelId);
    void PersistUseJsonPrompt(bool value);
    /// <inheritdoc cref="LoadUseSageAttention"/>
    void PersistUseSageAttention(bool value);
    /// <inheritdoc cref="LoadFreeVramAfterRendering"/>
    void PersistFreeVramAfterRendering(bool value);
    /// <inheritdoc cref="LoadAppTheme"/>
    void PersistAppTheme(AppTheme value);

    void PersistComfyUiBaseUrl(string value);
    /// <inheritdoc cref="LoadOllamaBaseUrl"/>
    void PersistOllamaBaseUrl(string value);

    /// <summary>
    /// The Ollama tag one LLM role calls on its Local tier: the prompt writer, the caption mutator,
    /// the region enricher. Three values because they are three decisions — all three used to read the
    /// one Ollama model on the Settings page (<c>imggen.ollama_model</c>), so picking a cheap tag to test
    /// a mutation also changed the prompt writer.
    /// Null when never set and never seeded; the store seeds a role that has no value of its own from
    /// that old key exactly once. There is no default model — callers must refuse
    /// with "pick a model first" rather than guess one.
    /// </summary>
    string? LoadPromptWriterModel();
    /// <inheritdoc cref="LoadPromptWriterModel" />
    string? LoadMutationModel();
    /// <inheritdoc cref="LoadPromptWriterModel" />
    string? LoadEnrichModel();
    /// <inheritdoc cref="LoadPromptWriterModel" />
    void PersistPromptWriterModel(string value);
    /// <inheritdoc cref="LoadPromptWriterModel" />
    void PersistMutationModel(string value);
    /// <inheritdoc cref="LoadPromptWriterModel" />
    void PersistEnrichModel(string value);

    /// <summary>
    /// Which provider answers this role's calls. <paramref name="fallback"/> is what a first run, or a
    /// stored value that no longer parses, lands on — the caller supplies it because the right answer is
    /// per role: the roles that used to run on Claude default to Anthropic, while enrichment starts on
    /// the free local model rather than reopening on a paid call every navigation. An enum written as
    /// text is allowed to outlive a rename, so an unparseable value degrades instead of throwing, which
    /// is what the tier picker already did with an unknown selection.
    /// </summary>
    ModelProvider LoadLlmProvider(ModelRole role, ModelProvider fallback);
    /// <inheritdoc cref="LoadLlmProvider" />
    void PersistLlmProvider(ModelRole role, ModelProvider value);

    /// <summary>
    /// The model this role calls <em>on the given provider</em>. Per provider on purpose: a role that
    /// tries Claude for a run and comes back to Ollama must not have lost its local tag, and an Ollama
    /// tag is not a valid Claude or OpenRouter id — seeding those slots from the generation model would
    /// be the "hand a model something it cannot read" bug all over again.
    /// Anthropic falls back to that role's historical default model; Local and OpenRouter have no
    /// default (which models exist is per-server / per-catalog) and return null for "pick one first".
    /// </summary>
    string? LoadLlmModel(ModelRole role, ModelProvider provider);
    /// <inheritdoc cref="LoadLlmModel" />
    void PersistLlmModel(ModelRole role, ModelProvider provider, string? value);

    /// <summary>
    /// Provider + model + endpoint in one call: what a service is handed so it never has to work out
    /// which model its role wanted. The single place the "no URL typed means the default host" rule is
    /// applied for an LLM call. <paramref name="fallback"/> is passed straight to
    /// <see cref="LoadLlmProvider"/>, so a caller asking for a target and a caller asking for a provider
    /// agree on what a first run means.
    /// </summary>
    LlmTarget LoadLlmTarget(ModelRole role, ModelProvider fallback);
    /// <inheritdoc cref="LoadOpenRouterFreeOnly"/>
    void PersistOpenRouterFreeOnly(bool value);
    /// <inheritdoc cref="LoadIdeaBuildJson"/>
    void PersistIdeaBuildJson(bool value);
    /// <inheritdoc cref="LoadOutputFolder"/>
    void PersistOutputFolder(string value);
    /// <inheritdoc cref="LoadCivitaiModelRef"/>
    void PersistCivitaiModelRef(string value);
    /// <inheritdoc cref="LoadComfyUiPreset"/>
    void PersistComfyUiPreset(string value, string workflowName);
    /// <inheritdoc cref="LoadComfyUiLora"/>
    void PersistComfyUiLora(string value, string workflowName);
    /// <summary>
    /// Per-workflow "Upscale after render" checkbox — whether the extra pass is worth it is
    /// a per-model habit. Default false.
    /// </summary>
    bool LoadComfyUiUpscaleAfter(string workflowName);
    /// <inheritdoc cref="LoadComfyUiUpscaleAfter"/>
    void PersistComfyUiUpscaleAfter(bool value, string workflowName);
    /// <summary>
    /// Per-workflow upscale factor override as an invariant numeric string ("2", "1.5");
    /// null when never picked. Keyed by the workflow whose UltimateSDUpscale gets patched.
    /// </summary>
    string? LoadComfyUiUpscaleFactor(string workflowName);
    /// <inheritdoc cref="LoadComfyUiUpscaleFactor"/>
    void PersistComfyUiUpscaleFactor(string value, string workflowName);
    /// <summary>
    /// Last window size/position in DIPs. Null when never persisted or unparseable —
    /// callers fall back to a screen-relative first-launch size. Values may describe a
    /// monitor that no longer exists; callers must clamp to the current screen.
    /// </summary>
    (double Width, double Height, double X, double Y)? LoadWindowBounds();
    /// <inheritdoc cref="LoadWindowBounds"/>
    void PersistWindowBounds(double width, double height, double x, double y);
    /// <summary>Writes a still-pending debounced prompt immediately. Call on app shutdown.</summary>
    void FlushPendingWrites();
}
