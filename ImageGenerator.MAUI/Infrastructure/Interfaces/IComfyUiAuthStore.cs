namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the optional ComfyUI Authorization header plus the
/// per-keystroke debounce. The stored value is the FULL header value ("Bearer abc123",
/// "Basic …") sent verbatim on every ComfyUI HTTP request and the progress WebSocket —
/// for reverse-proxied setups. Empty/unset = no header (the LAN default). Shape mirrors
/// IPollinationsTokenStore; a separate interface and SecureStorage key keep it from ever
/// colliding with the real provider tokens.
/// </summary>
public interface IComfyUiAuthStore : ITokenStore
{
}
