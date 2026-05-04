using System.Globalization;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ImageGenerator.MAUI.Presentation.Converters;

// Lets the OS hand us a thumbnail instead of decoding the full PNG ourselves. Windows
// reuses any embedded JPEG/EXIF thumbnail and otherwise serves a synthesised bitmap from
// its shell thumbnail cache (the same one Explorer uses), so a directory of 4-15 MB
// images costs only the small cached thumbnails — no per-tile pixel decode in-process.
public class ShellThumbnailConverter : IValueConverter
{
    private const uint RequestedSize = 256;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path)) return null;

        return ImageSource.FromStream(async ct =>
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path).AsTask(ct);
                var thumb = await file.GetThumbnailAsync(
                    ThumbnailMode.PicturesView,
                    RequestedSize,
                    ThumbnailOptions.UseCurrentScale).AsTask(ct);

                if (thumb is null || thumb.Size == 0)
                {
                    return Stream.Null;
                }

                // Hand MAUI a managed stream. The WinRT thumbnail object owns the underlying
                // buffer and is disposed when the managed wrapper is collected.
                return thumb.AsStreamForRead();
            }
            catch
            {
                // Permissions, vanished file, unsupported format — show the broken-image
                // placeholder rather than crashing the whole page.
                return Stream.Null;
            }
        });
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
