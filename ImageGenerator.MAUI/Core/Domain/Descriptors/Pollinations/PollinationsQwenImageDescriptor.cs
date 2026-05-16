using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Core.Domain.Descriptors.Pollinations;

public sealed class PollinationsQwenImageDescriptor : PollinationsDescriptorBase
{
    public PollinationsQwenImageDescriptor()
        : base("Qwen Image Plus (Pollinations)", ModelConstants.Pollinations.QwenImage, "qwen-image") { }
}
