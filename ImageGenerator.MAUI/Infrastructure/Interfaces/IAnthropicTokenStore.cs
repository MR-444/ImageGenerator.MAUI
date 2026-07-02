namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the Anthropic API key plus the per-keystroke debounce.
/// Shape mirrors ICivitaiTokenStore / IApiTokenStore so the VM persists all provider tokens
/// identically; kept as a separate interface (and a separate SecureStorage key) so the
/// providers' tokens never collide. Used by Claude tiers in the prompt builder and AI caption tools.
/// </summary>
public interface IAnthropicTokenStore : ITokenStore
{
}
