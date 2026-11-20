namespace ImageGenerator.MAUI.Infrastructure.External;

/// <summary>
/// Post-processing shared by the chat transports whose models sometimes wrap their answer in prose.
/// Extracted from <c>OllamaChatTransport</c> when the OpenRouter text path needed the same tolerance:
/// schema enforcement is per-endpoint there (a model whose provider cannot enforce the schema is still
/// served, and answers with a fenced block), so "the schema was requested" never means "the reply is
/// bare JSON". The Anthropic path needs none of this — its Messages API returns content blocks.
/// </summary>
internal static class ModelReplyText
{
    /// <summary>Strip a leading <c>```json</c> line and a trailing <c>```</c> so the JSON parses. Leaves
    /// any other reply untouched apart from surrounding whitespace.</summary>
    public static string StripJsonFences(string content)
    {
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline >= 0)
            trimmed = trimmed[(firstNewline + 1)..];   // drop the opening ``` / ```json line
        if (trimmed.EndsWith("```", StringComparison.Ordinal))
            trimmed = trimmed[..^3];

        return trimmed.Trim();
    }
}
