using System.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public sealed class FileLauncher : IFileLauncher
{
    public void Launch(string path)
    {
        // UseShellExecute hands the file off to whatever is registered as the default
        // viewer for that extension (Photos, IrfanView, etc.) — same shape as the
        // existing OpenOutputFolderAsync path that delegates to explorer.exe.
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void RevealInFolder(string path)
    {
        // explorer.exe /select,<path> opens the parent folder with the file highlighted, but explorer
        // does its OWN non-standard command-line parsing: the path MUST be wrapped in double quotes
        // (/select,"<path>"), or any path containing a comma or space is mis-parsed and explorer
        // silently falls back to a default folder (the reported "opens at the top level" bug — our
        // generated filenames embed the prompt and can contain commas). ArgumentList escapes embedded
        // quotes the wrong way for explorer, so build the argument string directly. This is
        // injection-safe: '"' is an illegal Windows path character, so a path can never break out.
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        };
        Process.Start(psi);
    }
}
