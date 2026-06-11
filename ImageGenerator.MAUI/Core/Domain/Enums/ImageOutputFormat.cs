namespace ImageGenerator.MAUI.Core.Domain.Enums;

// No JsonStringEnumConverter / EnumMember — each model descriptor builds the wire string
// explicitly via `ToString().ToLowerInvariant()` (the GPT/NanoBanana descriptors also map
// `Jpg→jpeg`). The enum is never directly JSON-serialized.
public enum ImageOutputFormat
{
    Jpg,
    Png,
    Webp
}
