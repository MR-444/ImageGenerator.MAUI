namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the CivitAI API key plus the per-keystroke debounce.
/// Shape mirrors IApiTokenStore / IPollinationsTokenStore so the VM persists all provider
/// tokens identically; kept as a separate interface (and a separate SecureStorage key) so
/// the providers' tokens never collide.
/// </summary>
public interface ICivitaiTokenStore : ITokenStore
{
}
