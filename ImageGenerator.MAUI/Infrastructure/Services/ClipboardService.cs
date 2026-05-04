using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace ImageGenerator.MAUI.Infrastructure.Services;

public class ClipboardService : IClipboardService
{
    public Task SetTextAsync(string text) => Clipboard.Default.SetTextAsync(text);
}
