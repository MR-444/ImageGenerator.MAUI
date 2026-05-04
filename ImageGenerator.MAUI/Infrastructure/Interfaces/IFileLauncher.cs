namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

// Thin seam around Process.Start so VM tests can verify "the user clicked a tile" without
// actually launching Explorer or the OS image viewer.
public interface IFileLauncher
{
    void Launch(string path);

    // Open Explorer with the given file pre-selected (Windows: `explorer.exe /select,"path"`).
    // Distinct from Launch on a directory, which just opens the folder.
    void RevealInFolder(string path);
}
