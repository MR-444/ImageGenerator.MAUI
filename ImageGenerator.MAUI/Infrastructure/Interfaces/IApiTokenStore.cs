namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the Replicate API token plus the per-keystroke
/// debounce, so the VM doesn't know about SecureStorage.
/// </summary>
public interface IApiTokenStore : ITokenStore;
