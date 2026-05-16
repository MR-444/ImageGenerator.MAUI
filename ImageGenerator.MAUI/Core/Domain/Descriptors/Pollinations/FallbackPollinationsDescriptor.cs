using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;

/// <summary>
/// Handles any "pollinations/*" model id surfaced by Refresh Models that doesn't have a
/// dedicated seed descriptor — e.g. when the live catalog returns "pollinations/gptimage" or
/// "pollinations/seedream5". Strips the prefix to derive the wire-level model name at request
/// time. Capabilities are inherited from the base (same for every Pollinations model today).
///
/// Not registered in DI — the registry holds it internally and returns it for unseeded
/// pollinations ids, mirroring how FallbackReplicateDescriptor handles unseeded owner/name ids.
/// </summary>
public sealed class FallbackPollinationsDescriptor : PollinationsDescriptorBase
{
    // The base ctor wants a display + server name; for the fallback they're never used at
    // request time (ResolveServerModelName below overrides), so sentinel values keep the
    // shape happy without claiming to represent any specific model.
    public FallbackPollinationsDescriptor()
        : base("(pollinations fallback)", "_fallback_pollinations", "(unset)") { }

    protected override string ResolveServerModelName(ImageGenerationParameters p) =>
        p.Model.StartsWith(ModelConstants.Pollinations.PrefixSlash, StringComparison.Ordinal)
            ? p.Model[ModelConstants.Pollinations.PrefixSlash.Length..]
            : p.Model;
}
