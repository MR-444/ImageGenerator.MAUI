using System.Diagnostics;
using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public class FileLauncher : IFileLauncher
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
        // explorer.exe /select,<path> opens the parent folder with the file highlighted.
        // ArgumentList handles quoting/escaping internally, so a future filename containing
        // a quote can't break out of the argument.
        var psi = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            UseShellExecute = true
        };
        psi.ArgumentList.Add($"/select,{path}");
        Process.Start(psi);
    }
}
