namespace ImageGenerator.MAUI.Core.Domain.Enums;

// No JsonStringEnumConverter / EnumMember — the wire string is always built explicitly via
// `ToString().ToLowerInvariant()` in ImageModelFactory (and `Jpg→jpeg` is handled there for
// the OpenAI-on-Replicate branch). The enum is never directly JSON-serialized.
public enum ImageOutputFormat
{
    Jpg,
    Png,
    Webp
}
