namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

// Thin seam around the native folder-browse dialog so VM tests can verify "the user picked a
// folder" (or cancelled) without opening the OS picker. Mirrors IFileLauncher's role.
public interface IFolderPicker
{
    /// <summary>
    /// Opens the OS folder-browse dialog. Returns the chosen folder's full path, or null when the
    /// user cancels (or no picker is available on this platform).
    /// </summary>
    /// <param name="initialPath">Where the dialog should start, when the platform honours it.</param>
    Task<string?> PickFolderAsync(string? initialPath = null);
}
