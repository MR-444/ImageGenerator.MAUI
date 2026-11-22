using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging;
using ImageGenerationParameters = ImageGenerator.MAUI.Core.Domain.Entities.ImageGenerationParameters;

namespace ImageGenerator.MAUI.Presentation.ViewModels;

/// <summary>
/// The generator's model-dependent Pickers: aspect ratio, resolution and the five GPT image
/// options. Each is a <see cref="PickerChoice"/>, so only a user pick writes Parameters and is
/// saved; every other change to Parameters is mirrored in with <see cref="ShowFromParameters"/>.
/// On a model switch <see cref="ApplyCapabilities"/> replaces the lists and moves each value to
/// one the new model offers. Created and held by GeneratorViewModel; MainPage binds it as Pickers.*.
/// </summary>
public sealed class ModelPickersCoordinator
{
    private readonly Func<ImageGenerationParameters> _parametersAccessor;
    private readonly IUiStateStore _uiStateStore;
    private readonly Func<string?> _preferredAspectRatio;
    private readonly Action<string> _recordAspectRatioPick;
    private readonly ILogger _logger;

    public ModelPickersCoordinator(
        Func<ImageGenerationParameters> parametersAccessor,
        IUiStateStore uiStateStore,
        Func<string?> preferredAspectRatio,
        Action<string> recordAspectRatioPick,
        ILogger logger)
    {
        _parametersAccessor = parametersAccessor ?? throw new ArgumentNullException(nameof(parametersAccessor));
        _uiStateStore = uiStateStore ?? throw new ArgumentNullException(nameof(uiStateStore));
        _preferredAspectRatio = preferredAspectRatio ?? throw new ArgumentNullException(nameof(preferredAspectRatio));
        _recordAspectRatioPick = recordAspectRatioPick ?? throw new ArgumentNullException(nameof(recordAspectRatioPick));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        AspectRatio = new PickerChoice(OnAspectRatioPicked, _logger, nameof(AspectRatio));
        Resolution = new PickerChoice(OnResolutionPicked, _logger, nameof(Resolution));
        GptQuality = GptChoice(GptFields.Quality, v => Parameters.GptQuality = v);
        GptBackground = GptChoice(GptFields.Background, v => Parameters.GptBackground = v);
        GptModeration = GptChoice(GptFields.Moderation, v => Parameters.GptModeration = v);
        GptInputFidelity = GptChoice(GptFields.InputFidelity, v => Parameters.GptInputFidelity = v);
        GptComplexity = GptChoice(GptFields.Complexity, v => Parameters.GptComplexity = v);
    }

    // Read on every access: the owner's parameters instance is replaceable.
    private ImageGenerationParameters Parameters => _parametersAccessor();

    public PickerChoice AspectRatio { get; }
    public PickerChoice Resolution { get; }
    public PickerChoice GptQuality { get; }
    public PickerChoice GptBackground { get; }
    public PickerChoice GptModeration { get; }
    public PickerChoice GptInputFidelity { get; }
    // gpt-image-2.6-sunburst only; the list is empty (picker hidden) on every other model.
    public PickerChoice GptComplexity { get; }

    /// <summary>
    /// Replaces every list with the new model's and moves each value to one it offers. Nothing
    /// here is persisted: a fallback is not a pick.
    /// </summary>
    public void ApplyCapabilities(ModelCapabilities caps, string? modelValue)
    {
        AspectRatio.SetOptions(caps.AspectRatios);
        // Sticky AR, each step only if this model offers it: the pick saved for this model, else
        // the user's last pick this session, else the current value, else the model's first
        // ratio. With no ratios at all, keep the current value rather than invent a size the
        // model never offered.
        var current = Parameters.AspectRatio;
        var savedRatio = _uiStateStore.LoadAspectRatio(modelValue);
        var preferred = _preferredAspectRatio();
        var fallback = caps.AspectRatios.Count > 0 ? caps.AspectRatios[0] : current;
        var targetRatio =
            savedRatio is not null && caps.AspectRatios.Contains(savedRatio) ? savedRatio :
            preferred is not null && caps.AspectRatios.Contains(preferred) ? preferred :
            caps.AspectRatios.Contains(current) ? current :
            fallback;
        _logger.LogDebug(
            "ApplyCapabilities({Model}): aspect ratio current=\"{Current}\" saved=\"{Saved}\" preferred=\"{Preferred}\" -> \"{Target}\" ({Count} options)",
            modelValue, current, savedRatio, preferred, targetRatio, caps.AspectRatios.Count);
        Parameters.AspectRatio = targetRatio;

        Resolution.SetOptions(caps.Resolutions);
        var resolutions = Resolution.Options;
        if (resolutions.Count > 0)
        {
            // Sticky resolution: the current value if offered, else the saved choice for this
            // model, else the first option.
            var previous = Parameters.Resolution;
            var saved = _uiStateStore.LoadResolution(modelValue);
            var target =
                resolutions.Contains(previous) ? previous :
                saved is not null && resolutions.Contains(saved) ? saved :
                resolutions[0];
            _logger.LogDebug(
                "ApplyCapabilities({Model}): resolution prev=\"{Prev}\" saved=\"{Saved}\" -> \"{Target}\" ({Count} options)",
                modelValue, previous, saved, target, resolutions.Count);
            Parameters.Resolution = target;
        }

        // The GPT option enums differ per model and host (2.6-sunburst adds quality xhigh/max and
        // a complexity field), and a value the new model does not offer comes back as a 422. Take
        // the user's saved pick for this model, else keep the current value, else the app's
        // default, else the first option. The first option is no default: quality and moderation
        // lists open with "auto", the priced end.
        void Gpt(PickerChoice choice, IReadOnlyList<string>? offered, string field,
            string? currentValue, string appDefault, Action<string> select)
        {
            choice.SetOptions(offered);
            var options = choice.Options;
            if (options.Count == 0) return;
            var savedValue = _uiStateStore.LoadGptOption(field, modelValue);
            select(
                savedValue is not null && options.Contains(savedValue) ? savedValue :
                currentValue is not null && options.Contains(currentValue) ? currentValue :
                options.Contains(appDefault, StringComparer.Ordinal) ? appDefault :
                options[0]);
        }

        Gpt(GptQuality, caps.GptQualityOptions, GptFields.Quality, Parameters.GptQuality,
            ImageGenerationParameters.DefaultGptQuality, v => Parameters.GptQuality = v);
        Gpt(GptBackground, caps.GptBackgroundOptions, GptFields.Background, Parameters.GptBackground,
            ImageGenerationParameters.DefaultGptBackground, v => Parameters.GptBackground = v);
        Gpt(GptModeration, caps.GptModerationOptions, GptFields.Moderation, Parameters.GptModeration,
            ImageGenerationParameters.DefaultGptModeration, v => Parameters.GptModeration = v);
        Gpt(GptInputFidelity, caps.GptInputFidelityOptions, GptFields.InputFidelity, Parameters.GptInputFidelity,
            ImageGenerationParameters.DefaultGptInputFidelity, v => Parameters.GptInputFidelity = v);
        Gpt(GptComplexity, caps.GptComplexityOptions, GptFields.Complexity, Parameters.GptComplexity,
            ImageGenerationParameters.DefaultGptComplexity, v => Parameters.GptComplexity = v);
    }

    /// <summary>Shows every value Parameters holds, e.g. after the instance was replaced.</summary>
    public void ShowAll()
    {
        AspectRatio.Show(Parameters.AspectRatio);
        Resolution.Show(Parameters.Resolution);
        GptQuality.Show(Parameters.GptQuality);
        GptBackground.Show(Parameters.GptBackground);
        GptModeration.Show(Parameters.GptModeration);
        GptInputFidelity.Show(Parameters.GptInputFidelity);
        GptComplexity.Show(Parameters.GptComplexity);
    }

    /// <summary>Mirrors one changed Parameters property into its Picker; others are ignored.</summary>
    public void ShowFromParameters(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(ImageGenerationParameters.AspectRatio): AspectRatio.Show(Parameters.AspectRatio); break;
            case nameof(ImageGenerationParameters.Resolution): Resolution.Show(Parameters.Resolution); break;
            case nameof(ImageGenerationParameters.GptQuality): GptQuality.Show(Parameters.GptQuality); break;
            case nameof(ImageGenerationParameters.GptBackground): GptBackground.Show(Parameters.GptBackground); break;
            case nameof(ImageGenerationParameters.GptModeration): GptModeration.Show(Parameters.GptModeration); break;
            case nameof(ImageGenerationParameters.GptInputFidelity): GptInputFidelity.Show(Parameters.GptInputFidelity); break;
            case nameof(ImageGenerationParameters.GptComplexity): GptComplexity.Show(Parameters.GptComplexity); break;
        }
    }

    private void OnAspectRatioPicked(string aspectRatio)
    {
        Parameters.AspectRatio = aspectRatio;
        _recordAspectRatioPick(aspectRatio);
        // "custom" is not persisted: its width and height aren't either.
        if (aspectRatio != "custom") _uiStateStore.PersistAspectRatio(aspectRatio, Parameters.Model);
    }

    private void OnResolutionPicked(string resolution)
    {
        Parameters.Resolution = resolution;
        _logger.LogDebug("Resolution picked -> \"{Value}\"", resolution);
        _uiStateStore.PersistResolution(resolution, Parameters.Model);
    }

    // The store's field names for the GPT options; saved per model (see IUiStateStore.LoadGptOption).
    private static class GptFields
    {
        public const string Quality = "quality";
        public const string Background = "background";
        public const string Moderation = "moderation";
        public const string InputFidelity = "input_fidelity";
        public const string Complexity = "complexity";
    }

    private PickerChoice GptChoice(string field, Action<string> write) => new(value =>
    {
        write(value);
        _uiStateStore.PersistGptOption(field, value, Parameters.Model);
    }, _logger, $"Gpt.{field}");
}
