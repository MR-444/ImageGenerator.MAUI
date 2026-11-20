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
            _ => (null, null)
        };
        if (store is null)
            return (null, null);

        var apiKey = await store.LoadAsync();
        return string.IsNullOrWhiteSpace(apiKey)
            ? (null, $"No {name} API key — add it on the Settings page.")
            : (apiKey, null);
    }
}
