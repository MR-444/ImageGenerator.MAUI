namespace ImageGenerator.MAUI.Core.Domain.Services;

/// <summary>
/// Turns a base64-encoded image payload into a data URI (content-type sniffed from magic bytes).
/// Pure string/byte math — no HTTP or Replicate specifics, so it belongs in Domain.
/// </summary>
public static class ImageDataUriEncoder
{
    public static string BuildDataUri(string base64)
    {
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return base64;
        return $"data:{DetectImageMimeType(base64)};base64,{base64}";
    }

    public static string DetectImageMimeType(string base64Data)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64Data[..Math.Min(base64Data.Length, 20)]);
        }
        catch (FormatException)
        {
            return "image/png";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";

        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";

        if (bytes.Length >= 12 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";

        return "image/png";
    }
}
