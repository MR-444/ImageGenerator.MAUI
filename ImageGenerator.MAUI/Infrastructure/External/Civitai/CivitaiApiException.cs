namespace ImageGenerator.MAUI.Infrastructure.External.Civitai;

/// <summary>
/// A CivitAI-side failure (MCP tool error, JSON-RPC error, or tRPC error response) with a
/// message already shaped for the user. Distinguishes "the server said no" from transport
/// exceptions in CivitaiPostingService's catch blocks.
/// </summary>
public sealed class CivitaiApiException(string message) : Exception(message);
