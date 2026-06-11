namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

/// <summary>
/// Shared shape every provider-specific token store conforms to. Used by TokenProviderViewModel
/// so the VM can drive an arbitrary number of provider tokens through the same UI without
/// special-casing each one.
/// </summary>
public interface ITokenStore
{
    Task<string?> LoadAsync();

    /// <summary>Schedules a debounced write — repeated calls within the debounce window collapse into one.</summary>
    void Persist(string value);

    void Forget();

    /// <summary>Writes any still-pending debounced value immediately. Call on app shutdown.</summary>
    void FlushPendingWrites();
}
