namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Owns SecureStorage interactions for the Replicate API token plus the per-keystroke
/// debounce. Lifted out of GeneratorViewModel so the VM no longer knows about SecureStorage.
/// </summary>
public interface IApiTokenStore
{
    Task<string?> LoadAsync();

    /// <summary>Schedules a debounced write — repeated calls within the debounce window collapse into one.</summary>
    void Persist(string value);

    void Forget();
}
