using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.External.Anthropic;

/// <summary>
/// The Claude model IDs the app knows by name — the ONE place they are spelled. They used to be
/// duplicated as private constants in the prompt builder, the caption mutator and the region enricher
/// (three copies of <c>"claude-sonnet-4-6"</c> and their "what this role defaults to" switch), which
/// meant a model bump was a three-file scavenger hunt with no test failing if one copy was missed.
/// </summary>
public static class AnthropicModels
{
    public const string Sonnet = "claude-sonnet-4-6";
    public const string Opus = "claude-opus-4-8";

    /// <summary>What each role ran on before a role had a model of its own to remember — kept as the
    /// default so this change does not silently move anyone onto a different (or pricier) model. The
    /// builder has always been an Opus caller; the two editors default to the cheaper Sonnet.</summary>
    public static string DefaultFor(ModelRole role) => role switch
    {
        ModelRole.PromptWrite => Opus,
        _ => Sonnet
    };
}
