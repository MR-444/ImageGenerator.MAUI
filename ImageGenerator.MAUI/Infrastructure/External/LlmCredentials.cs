using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.External;

/// <summary>
/// The one place an LLM service works out which key its target authenticates with — and, more
/// importantly, the one place it decides what to say when that key is missing. Three services used to
/// each carry their own "if the tier isn't Local, load the Anthropic key and complain if it's empty",
/// which was fine while Anthropic was the only hosted provider and became a silent wrong answer the
/// moment a second one appeared: an OpenRouter call would have demanded an Anthropic key.
/// </summary>
internal static class LlmCredentials
{
    /// <summary>The key for <paramref name="provider"/>, or the user-facing sentence telling them where
    /// to add it. <see cref="ModelProvider.Local"/> needs neither — an Ollama server on the LAN takes no
    /// auth, and inventing a key check there would only produce a message with nothing to act on.</summary>
    public static async Task<(string? ApiKey, string? Error)> ResolveAsync(
        ModelProvider provider,
        IAnthropicTokenStore anthropic,
        IOpenRouterTokenStore openRouter)
    {
        var (store, name) = provider switch
        {
            ModelProvider.Anthropic => (store: (ITokenStore?)anthropic, name: "Anthropic"),
            ModelProvider.OpenRouter => (store: (ITokenStore?)openRouter, name: "OpenRouter"),
            _ => (store: (ITokenStore?)null, name: (string?)null)
        };
        if (store is null || name is null)
            return (null, null);

        var apiKey = await store.LoadAsync();
        return string.IsNullOrWhiteSpace(apiKey)
            ? (null, MissingKeyMessage(name))
            : (apiKey, null);
    }

    /// <summary>
    /// The one wording for "this provider needs a key and this machine has none". The model list needs
    /// to say it too, and a second sentence meaning the same thing is how a user ends up reading two
    /// different instructions for one problem.
    /// </summary>
    public static string MissingKeyMessage(string providerName)
        => $"No {providerName} API key — add it on the Settings page.";
}
