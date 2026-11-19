using System.Collections;
using System.Reflection;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;

namespace ImageGenerator.MAUI.Tests.Descriptors;

/// <summary>
/// A descriptor is two hand-written halves about a model that keeps changing underneath both:
/// <c>Capabilities</c>, which decides what the pickers offer and what a saved file may claim, and
/// <c>Build</c>, which decides what Replicate is actually told. Nothing in the production code
/// connects them — ReplicateImageGenerationService's pre-flight guard reads the first half only, so
/// a descriptor that sends a field it does not declare spends money on the 422 that guard exists to
/// prevent, and the UI keeps hiding a knob it is paying for.
///
/// This test is the connection. For every shipped descriptor it builds real payloads and checks each
/// field against that descriptor's own capabilities.
///
/// One case rather than one per descriptor: the invariant is a table-wide one, and a partial pass
/// across the whole shipped set tells you less than one failure listing every mismatch at once.
///
/// Deliberately one-directional — payload → capabilities, never the reverse. A capability may exist
/// without a wire field of that name: Pollinations takes an aspect ratio as width/height, and
/// Ideogram's copyright flag has no counterpart in another model's vocabulary. A knob the user can
/// set that no request ever carries is worth knowing about too, but it is a different question and
/// belongs to the descriptor's owner, not to this rule.
/// </summary>
public class DescriptorPayloadConsistencyTests
{
    /// <summary>
    /// Fields a payload may carry that the capabilities model does not speak about. Each entry is a
    /// decision someone already made: <c>prompt</c> and <c>model</c> exist for every model there is;
    /// <c>raw</c> and <c>image_prompt_strength</c> are Flux 1.1 Ultra-only, whose capabilities name
    /// the pair through nothing more specific, and both are honest per-model extras.
    /// Anything not listed here and not covered by a rule below fails, so a new field cannot join the
    /// payload without someone deciding whether the capabilities model needs a word for it.
    /// </summary>
    private static readonly string[] NoCapabilityCounterpart =
        ["prompt", "model", "raw", "imagepromptstrength"];

    [Fact]
    public void EveryFieldThePayloadWrites_IsDeclaredByThatDescriptorsCapabilities()
    {
        var failures = new List<string>();

        foreach (var descriptor in ModelDescriptorRegistry.ProductionDescriptors())
        {
            if (descriptor is not IPayloadBuilder builder) continue;

            var caps = (descriptor as ICapabilityProvider)?.Capabilities
                ?? throw new InvalidOperationException(
                    $"{descriptor.GetType().Name} builds a payload but declares no capabilities, so nothing can check what it sends");

            foreach (var variant in Variants())
            {
                var parameters = BuildParameters(caps, variant);
                foreach (var (key, value) in Fields(builder.Build(parameters)))
                {
                    if (Check(key, value, caps) is { } failure)
                        failures.Add($"{descriptor.GetType().Name} ({variant}): {failure}");
                }
            }
        }

        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TheDescriptorsUnderReview_AreStillTheOnesBeingChecked()
    {
        // This file's value is that it covers every shipped payload builder. A descriptor added
        // without being an ICapabilityProvider, or a provider that stops being a builder, changes
        // what the check above reads without changing its result — so state the shape here.
        var builders = ModelDescriptorRegistry.ProductionDescriptors().OfType<IPayloadBuilder>().ToList();

        builders.Should().OnlyContain(d => d is ICapabilityProvider);
        builders.Count.Should().Be(19,
            "every shipped Replicate/Pollinations payload builder is meant to be checked above; if a " +
            "descriptor was added or removed, this count is the thing that has to be looked at");
    }

    /// <summary>
    /// Build only writes some fields under some conditions — width/height only for a custom ratio,
    /// json_prompt only when the structured editor is on, an image list only when images are
    /// attached — so one parameter set would leave most of each Build unexamined.
    /// </summary>
    private static IEnumerable<string> Variants() => (string[])["text", "images", "custom", "json", "options", "jpg"];

    /// <summary>
    /// The values this app holds for a model once the picker has settled on it: each knob at the app's
    /// own starting value where that model offers one, and at the model's first option where it does
    /// not — the rule <c>RefreshCapabilities</c> applies. Read off a fresh
    /// <see cref="ImageGenerationParameters"/> rather than repeated here, so this test cannot drift
    /// from the defaults it is describing.
    ///
    /// Settling is what keeps this test about one thing: whether a descriptor's two halves agree. That
    /// the starting values suit every model only where the starting model is concerned is a separate
    /// question with its own guard (<c>StartingValues_AreValuesTheStartingModelActuallyAccepts</c>) —
    /// Ideogram, whose resolutions are <c>Auto</c> and pixel sizes, still has no use for the app's
    /// starting <c>1K</c>, and the picker settles it on selection. Mixing the two questions would hide
    /// the second behind the first.
    /// </summary>
    private static readonly ImageGenerationParameters AppStartingValues = new();

    private static ImageGenerationParameters BuildParameters(ModelCapabilities caps, string variant)
    {
        var p = new ImageGenerationParameters
        {
            Prompt = "a prompt",
            ApiToken = "token",
            AspectRatio = Snap(caps.AspectRatios, AppStartingValues.AspectRatio),
            Resolution = Snap(caps.Resolutions, AppStartingValues.Resolution),
            GptQuality = Snap(caps.GptQualityOptions, AppStartingValues.GptQuality),
            GptBackground = Snap(caps.GptBackgroundOptions, AppStartingValues.GptBackground),
            GptModeration = Snap(caps.GptModerationOptions, AppStartingValues.GptModeration),
            GptInputFidelity = Snap(caps.GptInputFidelityOptions, AppStartingValues.GptInputFidelity),
            GptComplexity = Snap(caps.GptComplexityOptions, AppStartingValues.GptComplexity),
        };

        switch (variant)
        {
            case "images":
                p.ImagePrompts.Add("data:image/png;base64,AAAA");
                break;
            case "custom":
                if (caps.CustomDimensions) p.AspectRatio = "custom";
                break;
            case "json":
                p.UseJsonPrompt = true;
                break;
            case "options":
                p.EnableCopyrightDetection = true;
                break;
            case "jpg":
                p.OutputFormat = ImageOutputFormat.Jpg;
                break;
        }

        return p;
    }

    private static string Snap(IReadOnlyList<string>? options, string appStartingValue) =>
        options is { Count: > 0 }
            ? options.Contains(appStartingValue, StringComparer.Ordinal) ? appStartingValue : options[0]
            : appStartingValue;

    /// <summary>
    /// Payloads are dictionaries for the families added since the refactor and typed records for the
    /// first few; both are fields with values here. Wire names are snake_case, record properties are
    /// PascalCase, and the two are the same field, so compare them with the underscores removed.
    /// </summary>
    private static IEnumerable<(string Key, object? Value)> Fields(object payload) =>
        payload is IDictionary<string, object?> dict
            ? dict.Select(kv => (kv.Key, kv.Value))
            : payload.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(pi => pi.GetIndexParameters().Length == 0)
                .Select(pi => (pi.Name, pi.GetValue(payload)));

    private static string? Check(string rawKey, object? value, ModelCapabilities caps)
    {
        var key = rawKey.Replace("_", string.Empty).ToLowerInvariant();

        // A field sent switched off claims nothing: Flux 1.1 Ultra always sends
        // prompt_upsampling=false while correctly declaring it offers no upsampling, and a null width
        // means "the model's own preset decided the size". Zero is deliberately not off — it is a real
        // seed, and 0 is a real safety tolerance.
        if (IsOff(value)) return null;

        return key switch
        {
            "aspectratio" => caps.AspectRatio
                ? Declares(value, caps.AspectRatios, rawKey, "its aspect ratios")
                : Needs(rawKey, value, "it offers no aspect ratio"),
            "seed" => caps.Seed ? null : Needs(rawKey, value, "it offers no seed"),
            "safetytolerance" => caps.SafetyTolerance ? null : Needs(rawKey, value, "it offers no safety tolerance"),
            "promptupsampling" => caps.PromptUpsampling ? null : Needs(rawKey, value, "it offers no prompt upsampling"),
            "outputquality" or "outputcompression" => caps.OutputQuality ? null : Needs(rawKey, value, "it offers no output quality"),
            "outputformat" => caps.OutputFormatSelectable ? null : Needs(rawKey, value, "its output format is fixed"),
            "width" or "height" => caps.CustomDimensions ? null : Needs(rawKey, value, "it takes preset sizes only"),
            "images" or "inputimages" or "imageinput" or "imageprompt" => caps.ImagePrompt ? null : Needs(rawKey, value, "it takes no reference image"),
            "safe" => caps.Safe ? null : Needs(rawKey, value, "it has no safety switch"),
            "quality" => Declares(value, caps.GptQualityOptions, rawKey, "its quality values"),
            "background" => Declares(value, caps.GptBackgroundOptions, rawKey, "its background values"),
            "moderation" => Declares(value, caps.GptModerationOptions, rawKey, "its moderation values"),
            "inputfidelity" => Declares(value, caps.GptInputFidelityOptions, rawKey, "its input-fidelity values"),
            "complexity" => Declares(value, caps.GptComplexityOptions, rawKey, "its complexity values"),
            "resolution" => Declares(value, caps.Resolutions, rawKey, "its resolutions"),
            "jsonprompt" => caps.JsonPromptEditor ? null : Needs(rawKey, value, "no structured prompt editor"),
            "enablecopyrightdetection" => caps.IdeogramOptions ? null : Needs(rawKey, value, "it declares no Ideogram-only options"),
            _ => NoCapabilityCounterpart.Contains(key, StringComparer.Ordinal)
                ? null
                : $"'{rawKey}' is not covered by any rule: give the capabilities model a word for it or list it as needing no counterpart",
        };
    }

    private static bool IsOff(object? value) => value is null or false or "" || value is IList { Count: 0 };

    private static string Needs(string key, object? value, string because) =>
        $"sends {key}={Format(value)} although {because}";

    private static string? Declares(object? value, IReadOnlyList<string>? declared, string key, string what) =>
        declared is not null && declared.Contains(Format(value), StringComparer.Ordinal)
            ? null
            : $"sends {key}={Format(value)}, which is not among {what}";

    private static string Format(object? value) =>
        value is IList list ? $"<{list.Count}>" : value?.ToString() ?? "null";
}
