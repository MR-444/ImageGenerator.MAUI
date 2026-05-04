namespace ImageGenerator.MAUI.Infrastructure.Interfaces;

// Thin seam around Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard so the detail VM
// stays unit-testable. The real implementation forwards to MAUI Essentials' Clipboard.
public interface IClipboardService
{
    Task SetTextAsync(string text);
}
