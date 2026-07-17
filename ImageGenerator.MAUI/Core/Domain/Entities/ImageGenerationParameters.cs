using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Entities;

// CommunityToolkit.Mvvm is cross-platform and UI-framework-agnostic (it only implements
// INotifyPropertyChanged via source gen). Keeping it in Core is a deliberate pragmatic
// call for this single-.csproj app — revisit if the project is ever split into separate
// Core/Infrastructure/Presentation assemblies, in which case swap for hand-rolled
// INotifyPropertyChanged so Core carries zero external deps.
public partial class ImageGenerationParameters : ObservableObject
{
    [ObservableProperty]
    private string _apiToken = string.Empty;

    [ObservableProperty]
    private string _model = "openai/gpt-image-1.5";

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private long _seed;

    [ObservableProperty]
    private bool _randomizeSeed = true;

    [ObservableProperty]
    private string _aspectRatio = "16:9";

    public ObservableCollection<string> ImagePrompts { get; } = [];

    [ObservableProperty]
    private int _width = ValidationConstants.ImageWidthMax / 2;

    [ObservableProperty]
    private int _height = ValidationConstants.ImageHeightMax / 2;

    partial void OnWidthChanged(int value)
    {
        if (value < ValidationConstants.ImageWidthMin) Width = ValidationConstants.ImageWidthMin;
        else if (value > ValidationConstants.ImageWidthMax) Width = ValidationConstants.ImageWidthMax;
    }

    partial void OnHeightChanged(int value)
    {
        if (value < ValidationConstants.ImageHeightMin) Height = ValidationConstants.ImageHeightMin;
        else if (value > ValidationConstants.ImageHeightMax) Height = ValidationConstants.ImageHeightMax;
    }

    [ObservableProperty]
    private int _safetyTolerance = ValidationConstants.SafetyMax;

    [ObservableProperty]
    private ImageOutputFormat _outputFormat = ImageOutputFormat.Png;

    [ObservableProperty]
    private int _outputQuality = ValidationConstants.OutputQualityMax;

    [ObservableProperty]
    private bool _promptUpsampling;

    // Use by FluxPro Ultra
    [ObservableProperty]
    private double _imagePromptStrength = 0.5; // default init value

    [ObservableProperty]
    private bool _raw; // Optional flag used by Flux Ultra

    // google/nano-banana-2: "1K" | "2K" | "4K".
    [ObservableProperty]
    private string _resolution = "1K";

    // openai/gpt-image-1.5 advanced knobs. Defaults match the API defaults so
    // silence on other models stays equivalent to not sending the field.
    [ObservableProperty]
    private string _gptQuality = "auto";

    [ObservableProperty]
    private string _gptBackground = "auto";

    [ObservableProperty]
    private string _gptModeration = "auto";

    [ObservableProperty]
    private string _gptInputFidelity = "low";

    // Pollinations-specific. Token stored on parameters so PollinationsImageGenerationService
    // can read it directly without taking a SecureStorage dependency, mirroring how ApiToken
    // works for Replicate. VM keeps both fields synced with their respective stores.
    [ObservableProperty]
    private string _pollinationsApiToken = string.Empty;

    [ObservableProperty]
    private bool _safe;

    // Ideogram V4: when set, Prompt is sent as the structured `json_prompt` object instead of `prompt`.
    [ObservableProperty]
    private bool _useJsonPrompt;

    // Ideogram V4: opt into post-generation copyright detection.
    [ObservableProperty]
    private bool _enableCopyrightDetection;

    // ComfyUI only: quality preset to patch into the workflow's single CustomCombo node.
    // Empty means the workflow's own baked-in choice (no patch applied).
    [ObservableProperty]
    private string _comfyUiPreset = string.Empty;

    // ComfyUI only, DISPLAY-ONLY: the workflow's baked-in checkpoint/diffusion model, so the
    // job card and metadata can show "what model produced this" — the workflow filename alone
    // doesn't say. Empty for non-ComfyUI providers. Never patched.
    [ObservableProperty]
    private string _comfyUiModelDisplay = string.Empty;

    // ComfyUI only, DISPLAY-ONLY: the quality preset actually in use, INCLUDING the workflow's
    // baked-in default. Counterpart to ComfyUiModelDisplay for the preset combo: ComfyUiPreset
    // is a patch sentinel (empty = "selection equals baked default, no patch"), so reading it for
    // provenance drops the preset whenever the user picks the baked default. This field records
    // the selected label regardless, so the metadata/job card always report it. Never patched.
    [ObservableProperty]
    private string _comfyUiPresetDisplay = string.Empty;

    // ComfyUI only: run the designated upscale workflow on the rendered image after saving
    // (JobRunner chain step). Persisted per workflow via UiStateStore; always false on the
    // chained pass itself so an upscale can never chain another upscale.
    [ObservableProperty]
    private bool _upscaleAfterRender;

    // ComfyUI only: the workflow stem the chain feeds the render into. Resolved by the VM
    // when the model changes (alphabetically first LoadImage workflow whose stem contains
    // "upscale") and snapshotted here so JobRunner never scans the folder itself.
    [ObservableProperty]
    private string _upscaleWorkflow = string.Empty;

    // Post the saved image to CivitAI as an unpublished draft after generation. Session-only
    // (defaults OFF every launch, never persisted): it triggers an upload side effect, so a
    // sticky-on across sessions would be worse than re-checking it.
    [ObservableProperty]
    private bool _postToCivitai;

    // Attach structured generation metadata (prompt, seed, model) to the CivitAI post.
    // Same session-only rationale as PostToCivitai; the local file is never modified.
    [ObservableProperty]
    private bool _civitaiIncludeMeta;

    // Raw CivitAI model reference (model URL or version id) the post should be associated
    // with — landing it in that model's gallery. Unlike the checkboxes this IS persisted
    // (via UiStateStore; the VM restores it on launch): the target model rarely changes,
    // and the off-by-default checkbox still gates any actual upload.
    [ObservableProperty]
    private string _civitaiModelRef = string.Empty;

    public ImageGenerationParameters Clone()
    {
        var copy = new ImageGenerationParameters
        {
            ApiToken         = ApiToken,
            Model            = Model,
            Prompt           = Prompt,
            Seed             = Seed,
            RandomizeSeed    = RandomizeSeed,
            AspectRatio      = AspectRatio,
            Width            = Width,
            Height           = Height,
            SafetyTolerance  = SafetyTolerance,
            OutputFormat     = OutputFormat,
            OutputQuality    = OutputQuality,
            PromptUpsampling = PromptUpsampling,
            ImagePromptStrength = ImagePromptStrength,
            Raw              = Raw,
            Resolution       = Resolution,
            GptQuality       = GptQuality,
            GptBackground    = GptBackground,
            GptModeration    = GptModeration,
            GptInputFidelity = GptInputFidelity,
            PollinationsApiToken = PollinationsApiToken,
            Safe             = Safe,
            UseJsonPrompt    = UseJsonPrompt,
            EnableCopyrightDetection = EnableCopyrightDetection,
            ComfyUiPreset    = ComfyUiPreset,
            ComfyUiModelDisplay = ComfyUiModelDisplay,
            ComfyUiPresetDisplay = ComfyUiPresetDisplay,
            UpscaleAfterRender = UpscaleAfterRender,
            UpscaleWorkflow  = UpscaleWorkflow,
            PostToCivitai    = PostToCivitai,
            CivitaiIncludeMeta = CivitaiIncludeMeta,
            CivitaiModelRef  = CivitaiModelRef,
        };
        foreach (var p in ImagePrompts) copy.ImagePrompts.Add(p);
        return copy;
    }
}