namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the Replicate API token plus the per-keystroke
/// debounce. Lifted out of GeneratorViewModel so the VM no longer knows about SecureStorage.
/// </summary>
public interface IApiTokenStore : ITokenStore
{
}
