using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.ComfyUi;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging;
using static ImageGenerator.MAUI.Presentation.Common.UiDispatcher;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// Sub-VM that owns every ComfyUI workflow probe: the read-only baked-model display, the
/// quality-preset picker, the Krea-2 LoRA catalog, the LoadImage input-image capability and
/// the "Upscale after render" chain plus its upscale-factor picker. Created and held by
/// GeneratorViewModel (exposed as <c>ComfyUi</c>); bound from the ComfyUI cards on MainPage.
///
/// Concurrency contract — why this code looks over-defensive: each probe is fired
/// fire-and-forget from RefreshCapabilities with its OWN monotonic version token, so a slow
/// /models/loras round-trip can never discard a fast local workflow-file update. Every
/// completion re-checks its token both before and inside the DispatchToUi hop. Do not merge
/// the tokens and do not await the probes sequentially.
///
/// Suppression contract: a MAUI/WinUI Picker with a two-way-bound SelectedItem pushes null —
/// or a valid first option — back into its source whenever its ItemsSource is swapped. Every
/// programmatic options swap here therefore runs inside a persist-suppression window, and the
/// null guards in the OnSelected*Changed hooks catch the deferred pushes that land after the
/// window has closed. Both are real platform binding quirks, not defensive noise.
/// </summary>
public sealed partial class ComfyUiWorkflowCoordinator : ObservableObject
{
    private readonly IComfyUiCheckpointService _checkpointService;

    // Optional: absent in the unit-test harness and on hosts without a ComfyUI install, in
    // which case the LoRA picker stays hidden rather than probing a server that isn't there.
    private readonly IComfyUiLoraService? _comfyUiLoraService;
    private readonly IUiStateStore _uiStateStore;
    private readonly ILogger _logger;

    // Delegate collaborators, same shape as BatchCoordinator / InputImagesCoordinator: the
    // workflow state lives here, but the parameters it patches, the capabilities it raises and
    // the attached-image collection it truncates all belong to GeneratorViewModel.
    private readonly Func<ImageGenerationParameters> _parametersAccessor;
    private readonly Func<ModelCapabilities> _capsAccessor;
    private readonly Action<ModelCapabilities> _capsSetter;
    private readonly Action<int> _truncateInputImages;

    public ComfyUiWorkflowCoordinator(
        IComfyUiCheckpointService checkpointService,
        IUiStateStore uiStateStore,
        ILogger logger,
        Func<ImageGenerationParameters> parametersAccessor,
        Func<ModelCapabilities> capsAccessor,
        Action<ModelCapabilities> capsSetter,
        Action<int> truncateInputImages,
        IComfyUiLoraService? comfyUiLoraService = null)
    {
        _checkpointService = checkpointService ?? throw new ArgumentNullException(nameof(checkpointService));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parametersAccessor = parametersAccessor ?? throw new ArgumentNullException(nameof(parametersAccessor));
        _capsAccessor = capsAccessor ?? throw new ArgumentNullException(nameof(capsAccessor));
        _capsSetter = capsSetter ?? throw new ArgumentNullException(nameof(capsSetter));
        _truncateInputImages = truncateInputImages ?? throw new ArgumentNullException(nameof(truncateInputImages));
        _comfyUiLoraService = comfyUiLoraService;
    }

    /// <summary>
    /// The live parameters instance, re-read on every access. Named <c>Parameters</c> so the
    /// probe bodies that moved here read exactly as they did on GeneratorViewModel; never
    /// capture it once, since Parameters can be swapped (Remix) and a capture would strand the
    /// ComfyUi* writes on the discarded instance.
    /// </summary>
    private ImageGenerationParameters Parameters => _parametersAccessor();

    /// <summary>
    /// Re-hydrate every workflow-derived control for <paramref name="modelValue"/>. Replaces the
    /// five separate fire-and-forget calls RefreshCapabilities used to make; kept as five
    /// independent discards on purpose — see the version-token contract on the class.
    /// </summary>
    internal void RefreshForModel(string? modelValue)
    {
        _ = RefreshBakedModelAsync(modelValue);
        _ = RefreshQualityPresetOptionsAsync(modelValue);
        _ = RefreshKrea2LoraOptionsAsync(modelValue);
        _ = RefreshInputImageSupportAsync(modelValue);
        _ = RefreshUpscaleOptionAsync(modelValue);
    }

    /// <summary>
    /// Awaited by <c>GeneratorViewModel.RefreshModelsAsync</c>: a catalog refresh can change
    /// which ComfyUI workflows exist, and this one has a server round-trip worth surfacing
    /// after the catalog is applied rather than fire-and-forget.
    /// </summary>
    internal Task RefreshLorasForCurrentModelAsync(string? modelValue) =>
        RefreshKrea2LoraOptionsAsync(modelValue);

    /// <summary>
    /// Body of GeneratorViewModel's <c>Parameters.UpscaleAfterRender</c> hook, moved here so
    /// <see cref="_suppressUpscalePersist"/> stays private to this class. Checking the box arms
    /// the upscale factor when the UltimateSDUpscale slot lives in the chain workflow, and the
    /// factor card's visibility follows the same rule.
    /// </summary>
    public void OnUpscaleAfterRenderChanged()
    {
        ApplyUpscaleFactor();
        OnPropertyChanged(nameof(IsUpscaleFactorVisible));
        // Per-workflow persistence; suppressed during the RefreshUpscaleOption
        // restore and the HideUpscaleOption clear (capability consequences, not
        // user picks).
        if (!_suppressUpscalePersist && ModelConstants.ComfyUi.IsId(Parameters.Model))
        {
            _uiStateStore.PersistComfyUiUpscaleAfter(
                Parameters.UpscaleAfterRender,
                ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
        }
    }

    // ComfyUI baked-model display. The workflow file IS the model choice (one provider entry
    // per file), so the loader's model is shown read-only — swapping it inside a tuned
    // workflow would break the settings baked around it. Hidden whenever the workflow has no
    // unambiguous literal loader.
    [ObservableProperty]
    private string _bakedModelName = string.Empty;

    [ObservableProperty]
    private bool _supportsCheckpoint;

    // "Checkpoint" (CheckpointLoaderSimple) or "Diffusion model" (UNETLoader) — set per the
    // selected workflow's loader kind.
    [ObservableProperty]
    private string _checkpointLabel = "Checkpoint";

    // Monotonic token: a slower file probe for a previously selected model must not clobber
    // the display of the model selected after it.
    private int _checkpointRefreshVersion;

    // ComfyUI quality-preset picker (the workflow's single CustomCombo node). Options come
    // from the file itself — option1..option4 — with the baked-in choice first (= no patch);
    // no server fetch. Hidden whenever the workflow has no unambiguous CustomCombo.
    [ObservableProperty]
    private List<string> _qualityPresetOptions = [];

    [ObservableProperty]
    private string? _selectedQualityPreset;

    [ObservableProperty]
    private bool _supportsQualityPreset;

    private string? _workflowDefaultPreset;
    private bool _suppressPresetPersist;
    // Monotonic token, same role as _checkpointRefreshVersion.
    private int _presetRefreshVersion;

    // Krea-2 LoRA picker. The workflow probe is local; options are fetched from the selected
    // ComfyUI host's native /models/loras catalog. "No LoRA" maps to an empty request sentinel.
    // Keep recognizing the original "None" label when restoring preferences written by v2.1.0.
    internal const string NoKrea2LoraOption = "No LoRA";
    private const string LegacyNoKrea2LoraOption = "None";

    [ObservableProperty]
    private List<string> _krea2LoraOptions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKrea2LoraSelected))]
    private string? _selectedKrea2Lora;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKrea2LoraSelected))]
    private bool _supportsKrea2Lora;

    [ObservableProperty]
    private bool _isLoadingKrea2Loras;

    [ObservableProperty]
    private string? _krea2LoraStatusText;

    public bool IsKrea2LoraSelected =>
        SupportsKrea2Lora
        && SelectedKrea2Lora is { Length: > 0 }
        && SelectedKrea2Lora != NoKrea2LoraOption;

    private bool _suppressLoraPersist;
    private int _loraRefreshVersion;
    private string? _activeLoraWorkflow;

    // ComfyUI "Upscale after render" chain: visible only when a designated upscale workflow
    // exists (LoadImage-bearing, stem contains "upscale") and the selected workflow isn't
    // itself that workflow — an upscale never chains another upscale.
    [ObservableProperty]
    private bool _supportsUpscaleAfter;

    [ObservableProperty]
    private string _upscaleWorkflowDisplay = string.Empty;

    private bool _suppressUpscalePersist;
    // Monotonic tokens, same role as _checkpointRefreshVersion.
    private int _upscaleRefreshVersion;
    private int _inputImageRefreshVersion;

    // Upscale-factor picker: patches the UltimateSDUpscale upscale_by of whichever workflow
    // is in play — the selected workflow itself (standalone upscaling) or the chain target.
    // Options come from the workflow's baked value (first, = no patch) plus common factors.
    // The card's IsVisible binds to IsUpscaleFactorVisible, not SupportsUpscaleFactor: a factor
    // whose slot lives in the chain workflow is meaningless while "Upscale after render" is off.
    [ObservableProperty]
    private List<string> _upscaleFactorOptions = [];

    [ObservableProperty]
    private string? _selectedUpscaleFactor;

    [ObservableProperty]
    private bool _supportsUpscaleFactor;

    private double? _workflowDefaultUpscaleFactor;
    // The workflow whose upscale_by the picker patches; doubles as the persistence key.
    private string? _upscaleFactorWorkflow;
    // True when that workflow IS the one being rendered (it owns the UltimateSDUpscale node).
    // False when the slot lives in the chain target, so the factor only takes effect while
    // "Upscale after render" is checked.
    private bool _upscaleFactorOwnsRender;
    // The picker's intent, kept even while it cannot apply (chain off) so re-checking the box
    // re-arms the factor without asking the user to pick it again.
    private double? _pickedUpscaleFactor;
    private bool _suppressUpscaleFactorPersist;

    private static readonly double[] CommonUpscaleFactors = [1.5, 2.0, 3.0, 4.0];

    // Invariant on both sides — a de-DE "1,5×" label would round-trip as garbage.
    private static string FormatUpscaleFactor(double factor) =>
        factor.ToString("0.##", CultureInfo.InvariantCulture) + "×";

    private static double? ParseUpscaleFactor(string? label) =>
        double.TryParse(
            label?.TrimEnd('×'), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
            ? factor
            : null;

    partial void OnSelectedUpscaleFactorChanged(string? value)
    {
        // The WinUI ComboBox pushes SelectedItem=null on ItemsSource swaps; ignore it like
        // OnSelectedQualityPresetChanged does.
        if (value is null) return;

        var factor = ParseUpscaleFactor(value);
        if (factor is null) return;

        // Remembered as intent first: with the chain off this pick is inert, but it must not be
        // forgotten — checking "Upscale after render" later arms it.
        _pickedUpscaleFactor = factor;
        ApplyUpscaleFactor();

        if (!_suppressUpscaleFactorPersist
            && _upscaleFactorWorkflow is { } workflow
            && UpscaleFactorOptions.Contains(value))
        {
            _uiStateStore.PersistComfyUiUpscaleFactor(
                factor.Value.ToString(CultureInfo.InvariantCulture), workflow);
        }
    }

    /// <summary>
    /// The factor the render pipeline must see: the picker's intent, but only when it can
    /// actually change pixels — either the workflow being rendered owns the upscale slot, or
    /// "Upscale after render" is on and the factor targets the chain workflow. Any other
    /// combination patches nothing (the patcher's "no UltimateSDUpscale node" no-op), and
    /// forwarding it would stamp a false "UpscaleBy:" line into the image metadata.
    /// Same sentinel convention as ComfyUiPreset: the baked default means no patch.
    /// </summary>
    private void ApplyUpscaleFactor()
    {
        Parameters.ComfyUiUpscaleFactor =
            _pickedUpscaleFactor is { } picked
            && picked != _workflowDefaultUpscaleFactor
            && (_upscaleFactorOwnsRender || Parameters.UpscaleAfterRender)
                ? picked
                : null;
    }

    /// <summary>
    /// Whether the factor card can do something right now: a slot must exist AND be reachable —
    /// its own workflow, or the chain target with the chain checked. Showing it otherwise is an
    /// editable control with no effect (and the source of the metadata lie this guards).
    /// </summary>
    public bool IsUpscaleFactorVisible =>
        SupportsUpscaleFactor && (_upscaleFactorOwnsRender || Parameters.UpscaleAfterRender);

    partial void OnSelectedQualityPresetChanged(string? value)
    {
        // The WinUI ComboBox pushes SelectedItem=null on ItemsSource swaps; ignore it like
        // OnSelectedModelChanged does — the real selection lands on a later tick.
        if (value is null) return;

        Parameters.ComfyUiPreset =
            value == _workflowDefaultPreset ? string.Empty : value;

        // Display-only: record the resolved preset (default OR picked) so the metadata and job
        // card report which preset rendered the image. Unlike ComfyUiPreset (empty = no patch),
        // this is set even when the pick equals the baked default — mirrors ComfyUiModelDisplay.
        Parameters.ComfyUiPresetDisplay = value;

        if (!_suppressPresetPersist
            && QualityPresetOptions.Contains(value)
            && ModelConstants.ComfyUi.IsId(Parameters.Model))
        {
            _uiStateStore.PersistComfyUiPreset(
                value, ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
        }
    }

    partial void OnSelectedKrea2LoraChanged(string? value)
    {
        // ItemsSource swaps make the Windows Picker push null; preserve the real selection.
        if (value is null) return;

        var loraName = value == NoKrea2LoraOption ? string.Empty : value;
        Parameters.ComfyUiLora = loraName;
        Parameters.ComfyUiLoraDisplay = loraName;

        if (!_suppressLoraPersist
            && Krea2LoraOptions.Contains(value)
            && ModelConstants.ComfyUi.IsId(Parameters.Model))
        {
            _uiStateStore.PersistComfyUiLora(
                value, ModelConstants.ComfyUi.WorkflowName(Parameters.Model));
        }
    }

    /// <summary>
    /// A ComfyUI workflow containing a LoadImage node consumes an input image (img2img /
    /// upscale) — raise the provider's static capabilities for it so the Input Image card
    /// appears and the gallery's "Use as input" flow works.
    /// </summary>
    private async Task RefreshInputImageSupportAsync(string? modelValue)
    {
        var version = ++_inputImageRefreshVersion;
        try
        {
            // Non-ComfyUI models: the registry capabilities applied above are already final.
            if (!ModelConstants.ComfyUi.IsId(modelValue)) return;

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var hasInputImage = await _checkpointService.GetWorkflowHasInputImageAsync(workflowName);
            if (version != _inputImageRefreshVersion) return;

            // This probe owns the ComfyUI image-cap truncation RefreshCapabilities deferred:
            // now the workflow-specific cap (LoadImage → 1, otherwise 0) is actually known.
            DispatchToUi(() =>
            {
                if (version != _inputImageRefreshVersion) return;
                // Capabilities and the attached-image collection stay on GeneratorViewModel
                // (they are model/registry concerns), so this probe reaches them through the
                // injected delegates. _capsAccessor() is read here rather than captured earlier
                // so the `with` expression applies to whatever the capability swap settled on,
                // and writing it still fires GVM's OnCapabilitiesChanged → InputImages plus the
                // NotifyPropertyChangedFor cascade (SupportsInputImage et al.).
                if (hasInputImage)
                {
                    _capsSetter(_capsAccessor() with { ImagePrompt = true, MaxImageInputs = 1 });
                }
                _truncateInputImages(hasInputImage ? 1 : 0);
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Input-image support refresh failed Model={Model}", modelValue);
        }
    }

    internal async Task RefreshUpscaleOptionAsync(string? modelValue)
    {
        var version = ++_upscaleRefreshVersion;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue))
            {
                HideUpscaleOption(version);
                HideUpscaleFactorPicker(version);
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var upscaleWorkflow = await _checkpointService.FindUpscaleWorkflowNameAsync();
            var chainVisible = upscaleWorkflow is not null
                && !string.Equals(upscaleWorkflow, workflowName, StringComparison.OrdinalIgnoreCase);

            // Factor target: the selected workflow's own UltimateSDUpscale (standalone
            // upscaling) wins; otherwise the chain target's. Its name is also the persist key.
            var ownFactor = await _checkpointService.GetWorkflowUpscaleFactorAsync(workflowName);
            var factorWorkflow = ownFactor is not null ? workflowName
                : chainVisible ? upscaleWorkflow
                : null;
            var bakedFactor = ownFactor
                ?? (chainVisible
                    ? await _checkpointService.GetWorkflowUpscaleFactorAsync(upscaleWorkflow!)
                    : null);

            if (version != _upscaleRefreshVersion) return;

            if (!chainVisible)
            {
                HideUpscaleOption(version);
            }
            else
            {
                DispatchToUi(() =>
                {
                    if (version != _upscaleRefreshVersion) return;
                    _suppressUpscalePersist = true;
                    try
                    {
                        Parameters.UpscaleWorkflow = upscaleWorkflow!;
                        Parameters.UpscaleAfterRender = _uiStateStore.LoadComfyUiUpscaleAfter(workflowName);
                        UpscaleWorkflowDisplay = $"via {upscaleWorkflow}";
                        SupportsUpscaleAfter = true;
                    }
                    finally { _suppressUpscalePersist = false; }
                });
            }

            if (bakedFactor is null || factorWorkflow is null)
            {
                HideUpscaleFactorPicker(version);
                return;
            }

            DispatchToUi(() =>
            {
                if (version != _upscaleRefreshVersion) return;
                _suppressUpscaleFactorPersist = true;
                try
                {
                    _workflowDefaultUpscaleFactor = bakedFactor.Value;
                    _upscaleFactorWorkflow = factorWorkflow;
                    // Whose slot gets patched decides when the factor is live: own workflow →
                    // always; chain target → only while the chain is checked.
                    _upscaleFactorOwnsRender = ownFactor is not null;
                    var factors = new List<double> { bakedFactor.Value };
                    factors.AddRange(CommonUpscaleFactors.Where(f => !factors.Contains(f)));
                    UpscaleFactorOptions = [.. factors.Select(FormatUpscaleFactor)];
                    // Restore a previous explicit pick for THIS workflow — but only when it's
                    // still on offer; never invent a selection.
                    var saved = ParseUpscaleFactor(_uiStateStore.LoadComfyUiUpscaleFactor(factorWorkflow));
                    var target = saved is { } s && factors.Contains(s) ? s : bakedFactor.Value;
                    SelectedUpscaleFactor = FormatUpscaleFactor(target);
                    _pickedUpscaleFactor = target;
                    // Explicit, not only via the changed-handler: an unchanged label across a
                    // workflow switch fires no event, but the baked default (= null sentinel)
                    // and the slot ownership may differ between the two workflows.
                    ApplyUpscaleFactor();
                    SupportsUpscaleFactor = true;
                    OnPropertyChanged(nameof(IsUpscaleFactorVisible));
                }
                finally { _suppressUpscaleFactorPersist = false; }
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Upscale option refresh failed Model={Model}", modelValue);
        }
    }

    private void HideUpscaleFactorPicker(int? expectedVersion = null) => DispatchToUi(() =>
    {
        if (expectedVersion is { } v && v != _upscaleRefreshVersion) return;
        _suppressUpscaleFactorPersist = true;
        try
        {
            SupportsUpscaleFactor = false;
            UpscaleFactorOptions = [];
            _workflowDefaultUpscaleFactor = null;
            _upscaleFactorWorkflow = null;
            _upscaleFactorOwnsRender = false;
            _pickedUpscaleFactor = null;
            SelectedUpscaleFactor = null;
            // A stale factor must not linger in Clone() snapshots of other models.
            Parameters.ComfyUiUpscaleFactor = null;
            OnPropertyChanged(nameof(IsUpscaleFactorVisible));
        }
        finally { _suppressUpscaleFactorPersist = false; }
    });

    private void HideUpscaleOption(int? expectedVersion = null) => DispatchToUi(() =>
    {
        if (expectedVersion is { } v && v != _upscaleRefreshVersion) return;
        _suppressUpscalePersist = true;
        try
        {
            SupportsUpscaleAfter = false;
            UpscaleWorkflowDisplay = string.Empty;
            // Stale chain state must not linger in Clone() snapshots of other models.
            Parameters.UpscaleAfterRender = false;
            Parameters.UpscaleWorkflow = string.Empty;
        }
        finally { _suppressUpscalePersist = false; }
    });

    internal async Task RefreshBakedModelAsync(string? modelValue)
    {
        var version = ++_checkpointRefreshVersion;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue))
            {
                HideBakedModel(version);
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var slot = await _checkpointService.GetWorkflowModelSlotAsync(workflowName);
            if (version != _checkpointRefreshVersion) return;
            if (slot is null)
            {
                // Not an error: link-driven loaders, or a multi-UNET pairing with no single
                // model to name — but say so, or a missing model line looks like a bug.
                _logger.LogInformation(
                    "Baked-model display hidden: workflow {Workflow} has no unambiguous model loader "
                    + "(needs CheckpointLoaderSimple or exactly one UNETLoader with a literal name)",
                    workflowName);
                HideBakedModel(version);
                return;
            }

            DispatchToUi(() =>
            {
                if (version != _checkpointRefreshVersion) return;
                BakedModelName = slot.BakedName;
                CheckpointLabel = slot.Kind == ComfyUiLoaderKind.Unet ? "Diffusion model" : "Checkpoint";
                SupportsCheckpoint = true;
                // Display-only: the job card and metadata show which ComfyUI model produced
                // the image, not just the workflow filename.
                Parameters.ComfyUiModelDisplay = slot.BakedName;
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Baked-model refresh failed Model={Model}", modelValue);
        }
    }

    private void HideBakedModel(int? expectedVersion = null) => DispatchToUi(() =>
    {
        if (expectedVersion is { } v && v != _checkpointRefreshVersion) return;
        SupportsCheckpoint = false;
        BakedModelName = string.Empty;
        CheckpointLabel = "Checkpoint";
        // A stale model name must not linger in Clone() snapshots of other models.
        Parameters.ComfyUiModelDisplay = string.Empty;
    });

    internal async Task RefreshQualityPresetOptionsAsync(string? modelValue)
    {
        var version = ++_presetRefreshVersion;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue))
            {
                HideQualityPresetPicker(version);
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            var slot = await _checkpointService.GetWorkflowQualityPresetSlotAsync(workflowName);
            if (version != _presetRefreshVersion) return;
            if (slot is null)
            {
                // Not an error — most workflows simply have no (single) CustomCombo. Say so,
                // or a hidden picker looks like a bug.
                _logger.LogInformation(
                    "Preset picker hidden: workflow {Workflow} does not have exactly one "
                    + "CustomCombo node with a literal choice",
                    workflowName);
                HideQualityPresetPicker(version);
                return;
            }
            var baked = slot.BakedChoice;

            // Single UI update — the options live in the file, so unlike the checkpoint
            // picker there is no async second phase to wait for.
            DispatchToUi(() =>
            {
                if (version != _presetRefreshVersion) return;
                _suppressPresetPersist = true;
                try
                {
                    _workflowDefaultPreset = baked;
                    QualityPresetOptions =
                        [baked, .. slot.Options.Where(o => !string.Equals(o, baked, StringComparison.Ordinal))];
                    // Restore a previous explicit pick for THIS workflow — but only when the
                    // workflow still offers it; never invent a selection.
                    var saved = _uiStateStore.LoadComfyUiPreset(workflowName);
                    SelectedQualityPreset =
                        saved is not null && QualityPresetOptions.Contains(saved) ? saved : baked;
                    SupportsQualityPreset = true;
                }
                finally { _suppressPresetPersist = false; }
            });
        }
        catch (Exception ex)
        {
            // Fire-and-forget caller — an unobserved throw here would be silent at best.
            _logger.LogWarning(ex, "Quality-preset options refresh failed Model={Model}", modelValue);
        }
    }

    private void HideQualityPresetPicker(int? expectedVersion = null) => DispatchToUi(() =>
    {
        if (expectedVersion is { } v && v != _presetRefreshVersion) return;
        _suppressPresetPersist = true;
        try
        {
            SupportsQualityPreset = false;
            QualityPresetOptions = [];
            _workflowDefaultPreset = null;
            SelectedQualityPreset = null;
            // A stale preset must not linger in Clone() snapshots of other models.
            Parameters.ComfyUiPreset = string.Empty;
            Parameters.ComfyUiPresetDisplay = string.Empty;
        }
        finally { _suppressPresetPersist = false; }
    });

    internal async Task RefreshKrea2LoraOptionsAsync(string? modelValue)
    {
        var version = ++_loraRefreshVersion;
        var krea2Confirmed = false;
        try
        {
            if (!ModelConstants.ComfyUi.IsId(modelValue) || _comfyUiLoraService is null)
            {
                HideKrea2LoraPicker(version);
                return;
            }

            var workflowName = ModelConstants.ComfyUi.WorkflowName(modelValue!);
            // Do not let a previous Krea workflow's selection ride in Parameters while the
            // new workflow's local probe / host catalog request is still in flight.
            if (!string.Equals(_activeLoraWorkflow, workflowName, StringComparison.Ordinal))
            {
                DispatchToUi(() =>
                {
                    if (version != _loraRefreshVersion) return;
                    _suppressLoraPersist = true;
                    try
                    {
                        Parameters.ComfyUiLora = string.Empty;
                        Parameters.ComfyUiLoraDisplay = string.Empty;
                        SelectedKrea2Lora = null;
                    }
                    finally { _suppressLoraPersist = false; }
                });
            }
            krea2Confirmed = await _checkpointService.GetWorkflowIsKrea2Async(workflowName);
            if (version != _loraRefreshVersion) return;
            if (!krea2Confirmed)
            {
                HideKrea2LoraPicker(version);
                return;
            }

            DispatchToUi(() =>
            {
                if (version != _loraRefreshVersion) return;
                SupportsKrea2Lora = true;
                IsLoadingKrea2Loras = true;
                Krea2LoraStatusText = "Loading LoRAs from ComfyUI…";
            });

            var names = await _comfyUiLoraService.GetLoraNamesAsync();
            if (version != _loraRefreshVersion) return;

            DispatchToUi(() =>
            {
                if (version != _loraRefreshVersion) return;
                _suppressLoraPersist = true;
                try
                {
                    Krea2LoraOptions = [NoKrea2LoraOption, .. names];
                    var saved = _uiStateStore.LoadComfyUiLora(workflowName);
                    var target = string.Equals(saved, LegacyNoKrea2LoraOption, StringComparison.Ordinal)
                        ? NoKrea2LoraOption
                        : saved is not null && Krea2LoraOptions.Contains(saved)
                            ? saved
                            : NoKrea2LoraOption;

                    SelectedKrea2Lora = target;
                    // Explicit because an unchanged selection label does not raise OnChanged
                    // when switching between two workflows with different saved state.
                    Parameters.ComfyUiLora = target == NoKrea2LoraOption ? string.Empty : target;
                    Parameters.ComfyUiLoraDisplay = Parameters.ComfyUiLora;
                    _activeLoraWorkflow = workflowName;
                    SupportsKrea2Lora = true;
                    IsLoadingKrea2Loras = false;
                    Krea2LoraStatusText = names.Count == 0
                        ? "No LoRAs are installed on this ComfyUI host."
                        : null;
                }
                finally { _suppressLoraPersist = false; }
            });
        }
        catch (Exception ex)
        {
            if (version != _loraRefreshVersion) return;
            _logger.LogWarning(ex, "Krea-2 LoRA catalog refresh failed Model={Model}", modelValue);
            if (!krea2Confirmed)
            {
                HideKrea2LoraPicker(version);
                return;
            }

            DispatchToUi(() =>
            {
                if (version != _loraRefreshVersion) return;
                _suppressLoraPersist = true;
                try
                {
                    SupportsKrea2Lora = true;
                    IsLoadingKrea2Loras = false;
                    Krea2LoraOptions = [NoKrea2LoraOption];
                    SelectedKrea2Lora = NoKrea2LoraOption;
                    Parameters.ComfyUiLora = string.Empty;
                    Parameters.ComfyUiLoraDisplay = string.Empty;
                    Krea2LoraStatusText =
                        "Couldn't load LoRAs. Check the ComfyUI server and authorization settings.";
                }
                finally { _suppressLoraPersist = false; }
            });
        }
    }

    [RelayCommand]
    private Task RefreshKrea2LorasAsync() => RefreshKrea2LoraOptionsAsync(Parameters.Model);

    private void HideKrea2LoraPicker(int? expectedVersion = null) => DispatchToUi(() =>
    {
        if (expectedVersion is { } v && v != _loraRefreshVersion) return;
        _suppressLoraPersist = true;
        try
        {
            SupportsKrea2Lora = false;
            IsLoadingKrea2Loras = false;
            Krea2LoraOptions = [];
            SelectedKrea2Lora = null;
            Krea2LoraStatusText = null;
            Parameters.ComfyUiLora = string.Empty;
            Parameters.ComfyUiLoraDisplay = string.Empty;
            Parameters.ComfyUiLoraStrength = 1.0;
            _activeLoraWorkflow = null;
        }
        finally { _suppressLoraPersist = false; }
    });
}
