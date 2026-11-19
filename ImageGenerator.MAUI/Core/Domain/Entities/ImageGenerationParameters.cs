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

    // The starting model, named once. It used to be repeated here as a raw string and again in
    // GeneratorViewModel's constructor, so a change here moved the default for nobody.
    //
    // It is also the model the rest of these starting values have to suit: ReplicateImageGenerationService
    // refuses to send a value the selected model's descriptor does not declare, so a starting value
    // outside the starting model's vocabulary is an app that refuses its own first call. Pinned by
    // ImageGenerationParametersDefaultsTests and by the service's starting-parameters test.
    [ObservableProperty]
    private string _model = ModelConstants.OpenAI.GptImage26SunburstOnReplicate;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private long _seed;

    [ObservableProperty]
    private bool _randomizeSeed = true;

    // Square, because that is a value every model family in this app offers — the three gpt-image
    // hosts, both Flux generations, nano-banana and Pollinations all list 1:1, and Ideogram, which
    // lists none, is handed a starting value it ignores. 16:9 was not: the starting model's own list
    // excluded it until this line moved.
    [ObservableProperty]
    private string _aspectRatio = "1:1";

    public ObservableCollection<string> ImagePrompts { get; } = [];

    // Only meaningful for models with custom dimensions; preset models ignore them and render at
    // whatever their selected aspect ratio means. They are the app's real starting values, which
    // until now lived in GeneratorViewModel on top of these — two defaults for one field, disagreeing.
    // Square so the pair means the ratio the app starts on: the first custom-dimension model opens at
    // the shape the rest of the UI just said was chosen.
    [ObservableProperty]
    private int _width = 1080;

    [ObservableProperty]
    private int _height = 1080;

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

    // Starting values for the gpt-image knobs, named because the view model has to be able to return
    // to them. A Picker whose ItemsSource is swapped writes null into whatever SelectedItem is bound
    // to, so a knob the current model does not offer loses its value on the way to the next one; the
    // snap that follows must land on what this app chose, not on the schema's first enum value — for
    // quality and moderation that first value is "auto", which is the priced end.
    public const string DefaultGptQuality = "medium";
    public const string DefaultGptBackground = "auto";
    public const string DefaultGptModeration = "low";
    public const string DefaultGptInputFidelity = "low";
    public const string DefaultGptComplexity = "low";

    // openai/gpt-image advanced knobs. These deliberately do NOT mirror the API defaults: the
    // server's defaults are the model owner's quality choice (complexity "high", quality "auto",
    // and "auto" may resolve to "max"). Only quality is priced — the models' own pages say "The
    // quality setting controls rendering detail and price", and "Complexity doesn't change the
    // price" — but every one of them is a decision the app should make on its user's behalf.
    // All the values below appear in every hosted gpt-image schema, so the picker never has to snap
    // them elsewhere; the comment in GptImageSchema records where each vocabulary came from.
    [ObservableProperty]
    private string _gptQuality = DefaultGptQuality;

    [ObservableProperty]
    private string _gptBackground = DefaultGptBackground;

    // "low" filters less. Not a price knob: nothing in the model docs prices moderation. The app
    // starts here on the owner's instruction; "auto" is the server default.
    [ObservableProperty]
    private string _gptModeration = DefaultGptModeration;

    [ObservableProperty]
    private string _gptInputFidelity = DefaultGptInputFidelity;

    // openai/gpt-image-2.6 only. The field does not exist on gpt-image-1.5 / -2, so the descriptor
    // omits it there. Cheap end rather than the server's "high": higher complexity plans more
    // before it renders, so it answers slower and "can change parts of the image you didn't ask
    // about". It costs nothing extra.
    [ObservableProperty]
    private string _gptComplexity = DefaultGptComplexity;

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

    // ComfyUI Krea-2 only: optional host LoRA injected as a LoraLoaderModelOnly node between
    // the workflow's single model loader and its consumers. Empty = no LoRA node. The name is
    // the exact relative path returned by GET /models/loras (subfolders included).
    [ObservableProperty]
    private string _comfyUiLora = string.Empty;

    [ObservableProperty]
    private double _comfyUiLoraStrength = 1.0;

    // Display-only provenance counterpart to ComfyUiLora. Empty when the picker is on None.
    [ObservableProperty]
    private string _comfyUiLoraDisplay = string.Empty;

    // ComfyUI only: override for the upscale workflow's UltimateSDUpscale upscale_by factor.
    // Null = keep the workflow's baked-in value (no patch) — same sentinel convention as
    // ComfyUiPreset. Applies to the workflow being generated AND rides the Clone() into a
    // chained upscale pass.
    [ObservableProperty]
    private double? _comfyUiUpscaleFactor;

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
            GptComplexity    = GptComplexity,
            PollinationsApiToken = PollinationsApiToken,
            Safe             = Safe,
            UseJsonPrompt    = UseJsonPrompt,
            EnableCopyrightDetection = EnableCopyrightDetection,
            ComfyUiPreset    = ComfyUiPreset,
            ComfyUiModelDisplay = ComfyUiModelDisplay,
            ComfyUiPresetDisplay = ComfyUiPresetDisplay,
            ComfyUiLora      = ComfyUiLora,
            ComfyUiLoraStrength = ComfyUiLoraStrength,
            ComfyUiLoraDisplay = ComfyUiLoraDisplay,
            ComfyUiUpscaleFactor = ComfyUiUpscaleFactor,
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
