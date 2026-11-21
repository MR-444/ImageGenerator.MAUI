using ImageGenerator.MAUI.Infrastructure.External.Anthropic;

namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// WHO answers an LLM call. Replaces <c>ModelTier</c>, which mixed the two orthogonal axes into one
/// enum: <c>Local</c> named a provider while <c>Sonnet</c>/<c>Opus</c> named model IDs, so adding a
/// second provider meant adding a value that was a third kind of thing. Provider and model ID are now
/// separate, and a model ID is always interpreted against its provider — an Ollama tag is not a Claude
/// ID and never was.
/// </summary>
public enum ModelProvider
{
    /// <summary>A model on the user's own Ollama server. No key, no cost, no default model.</summary>
    Local,

    /// <summary>api.anthropic.com. Needs the Anthropic key; the model ID is a Claude model.</summary>
    Anthropic,

    /// <summary>openrouter.ai. Needs the OpenRouter key; the model ID is an OpenRouter slug.</summary>
    OpenRouter
}

/// <summary>
/// WHICH feature is calling. It never reaches a service — a service is told everything it needs by the
/// <see cref="LlmTarget"/> it is handed — it exists to name the persisted slots, so a role's pick can
/// not land in another role's key. That cross-talk was the bug this whole change set exists to kill:
/// four Pickers used to write one key, so choosing a caption-mutation model silently changed what the
/// generate button rendered with.
/// </summary>
/// <remarks>The image-observation role is deliberately absent: it sends pixels and lives on
/// <c>VisionObservationService</c> with its own provider picker. Folding it in here means moving that
/// service onto the same transport seam, which is a separate change (see the plan's Phase C).</remarks>
public enum ModelRole
{
    /// <summary>Idea to Prompt: turn an idea (and optionally a reference image's description) into prose/JSON.</summary>
    PromptWrite,

    /// <summary>Mutation Engine: rewrite a whole structured caption toward a new direction.</summary>
    Mutate,

    /// <summary>Ideogram Structure Editor: rewrite the marked regions of one caption.</summary>
    Enrich
}

/// <summary>
/// The only two ways this app is allowed to spell a provider's name, and why there are two.
/// A picker asks WHO should answer, so it names the thing plainly; a sentence explains WHAT went wrong,
/// so it uses the brand the user would search for. Before this existed both were done by letting an enum
/// render itself, which is how one page came to read "Local" and its neighbour "LocalOllama": a bound
/// enum shows <c>ToString()</c>, and <c>ToString()</c> is the identifier the compiler needed, not the
/// word the user needed. Anything displaying a provider binds through here (see
/// <c>ModelProviderLabelConverter</c>) rather than adding a third spelling.
/// </summary>
public static class ModelProviderLabels
{
    /// <summary>The name as a choice in a list. Local carries its software in parentheses because
    /// "Local" alone does not say what has to be installed for the option to work; the other two are
    /// already the names of the services.</summary>
    public static string ForList(this ModelProvider provider) => provider switch
    {
        ModelProvider.Local => "Local (Ollama)",
        ModelProvider.Anthropic => "Anthropic",
        _ => "OpenRouter"
    };

    /// <summary>The name inside a sentence: "No Claude model selected", "Couldn't reach Ollama". Brand
    /// words, because a user quoting the error to a search engine quotes these.</summary>
    public static string ForProse(this ModelProvider provider) => provider switch
    {
        ModelProvider.Local => "Ollama",
        ModelProvider.Anthropic => "Claude",
        _ => "OpenRouter"
    };
}

/// <summary>
/// Everything one LLM call needs to know about who answers it — built by the caller, read by the
/// service. The point of the type: the choice arrives from outside, so a service stops reaching into
/// app settings to work out which model its role wanted, which is how three services each ended up
/// holding their own copy of "and if nothing is picked, use this Claude model".
/// <see cref="BaseUrl"/> is only meaningful to <see cref="ModelProvider.Local"/> (which server to ask);
/// the hosted providers answer at one known endpoint and authenticate instead.
/// </summary>
public readonly record struct LlmTarget(ModelProvider Provider, string ModelId, string BaseUrl)
{
    /// <summary>Nothing to call. Local has no default model by design (which models exist is
    /// per-server), so an empty ID is a real state the caller must turn into a "pick a model first"
    /// message rather than a request that 404s.</summary>
    public bool HasModel => !string.IsNullOrWhiteSpace(ModelId);

    /// <summary>Short name for status text and log lines.</summary>
    public string ProviderLabel => Provider.ForProse();

    /// <summary>The legacy tier → target translation, so the Sonnet/Opus/Local pickers that still ship
    /// keep their exact behaviour while the pages move over to provider + model one at a time.
    /// Sonnet/Opus carried both the provider AND the model ID; those two facts split here.</summary>
    public static LlmTarget FromLegacyTier(ModelTier tier, string localModelId, string localBaseUrl) => tier switch
    {
        ModelTier.Opus => new LlmTarget(ModelProvider.Anthropic, AnthropicModels.Opus, string.Empty),
        ModelTier.Local => new LlmTarget(ModelProvider.Local, localModelId, localBaseUrl),
        _ => new LlmTarget(ModelProvider.Anthropic, AnthropicModels.Sonnet, string.Empty)
    };

    /// <summary>
    /// The "you haven't chosen a model yet" sentence for this target. It lives on the target because all
    /// three text roles need the same branch — a local miss is worth naming the server for, a hosted
    /// miss is worth naming the provider for — and each one used to hand-write its own version.
    /// </summary>
    /// <param name="localPageHint">Where to pick it, phrased for the local case: only Local can give that
    /// advice, since a hosted model list is the same list on every page.</param>
    public string MissingModelError(string localPageHint) =>
        Provider == ModelProvider.Local
            ? $"No local Ollama model selected — pick one {localPageHint}"
            : $"No {ProviderLabel} model selected — pick one and try again.";
}
