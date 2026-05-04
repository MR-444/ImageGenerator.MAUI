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

    /// <summary>
    /// Convenience for descriptors that ship a bounded array of data URIs (image-prompt inputs).
    /// Returns null when the collection is empty so the JSON serializer can omit the field.
    /// </summary>
    public static string[]? BuildDataUris(IReadOnlyCollection<string> prompts, int maxCount)
        => prompts.Count == 0
            ? null
            : prompts.Take(maxCount).Select(BuildDataUri).ToArray();

    public static string DetectImageMimeType(string base64Data)
    {
        // 16 base64 chars decode to 12 bytes — exactly enough for WebP's RIFF...WEBP signature
        // at offsets 8-11, which is the longest of the four magic-byte checks below.
        Span<byte> buffer = stackalloc byte[12];
        var sliceLen = Math.Min(base64Data.Length, 16);
        if (!Convert.TryFromBase64Chars(base64Data.AsSpan(0, sliceLen), buffer, out var written))
            return "image/png";

        if (written >= 8 && buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
            return "image/png";

        if (written >= 3 && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return "image/jpeg";

        if (written >= 6 && buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46)
            return "image/gif";

        if (written >= 12 && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
            return "image/webp";

        return "image/png";
    }
}
