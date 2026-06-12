using System.Globalization;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ImageGenerator.MAUI.Presentation.Converters;

// Detail-page sibling of ShellThumbnailConverter. Same OS-shell delivery (no in-process
// WIC decode — the previous direct-binding approach occasionally crashed natively when
// MAUI tried to decode certain large or in-flight PNGs), but uses SingleItem mode at a
// larger requested size so the image keeps its aspect ratio and renders sharp at
// detail-page sizes (~500 px). PicturesView would center-crop into a square here.
public class ShellPreviewConverter : IValueConverter
{
    private const uint RequestedSize = 1024;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;

        // Task.Run is load-bearing — same UI-thread COM-pumping fail-fast as
        // ShellThumbnailConverter (see the comment there; confirmed by crash-dump analysis
        // 2026-06-13). The brokered GetFileFromPathAsync call must not start on the STA thread.
        return ImageSource.FromStream(ct => Task.Run(async () =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
                var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    RequestedSize,
                    ThumbnailOptions.UseCurrentScale).AsTask(ct);

                if (thumb is null || thumb.Size == 0)
                {
                    return Stream.Null;
                }

                return thumb.AsStreamForRead();
            }
            catch
            {
                return Stream.Null;
            }
        }, ct));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
