namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the Pollinations Bearer token plus the per-keystroke
/// debounce. Shape mirrors IApiTokenStore so the VM persists both tokens identically; kept
/// as a separate interface (and a separate SecureStorage key) so the two providers' tokens
/// never collide.
/// </summary>
public interface IPollinationsTokenStore : ITokenStore
{
}
