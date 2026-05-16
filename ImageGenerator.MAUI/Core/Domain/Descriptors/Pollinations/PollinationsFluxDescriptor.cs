using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;

public sealed class PollinationsFluxDescriptor : PollinationsDescriptorBase
{
    public PollinationsFluxDescriptor()
        : base("Flux (Pollinations)", ModelConstants.Pollinations.Flux, "flux") { }
}
