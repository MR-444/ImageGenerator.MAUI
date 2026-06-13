using ImageGenerator.MAUI.Infrastructure.Interfaces;

namespace ImageGenerator.MAUI.Infrastructure.Services;

// Native folder-browse dialog. Windows-only (the app's only target); everywhere else this
// returns null so callers degrade to the editable path field.
public sealed class FolderPickerService : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string? initialPath = null)
    {
#if WINDOWS
        // The WinRT picker must be created and shown on the UI thread, and initialised with the
        // app window's HWND (WinUI 3 desktop requirement — without it PickSingleFolderAsync
        // throws E_ACCESSDENIED).
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // WinRT FolderPicker only accepts a PickerLocationId, not an arbitrary path, so the
            // requested initialPath can't seed it — Pictures is the closest sensible start.
            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            };
            // PickSingleFolderAsync throws unless at least one filter is present.
            picker.FileTypeFilter.Add("*");

            var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows
                .FirstOrDefault(w => w.Handler?.PlatformView is not null) ?? Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
            if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                return null;

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        });
#else
        _ = initialPath;
        return await Task.FromResult<string?>(null);
#endif
    }
}
