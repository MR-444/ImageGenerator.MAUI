using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.ViewModels;

public class ModelCapabilitiesTests
{
    private readonly IModelDescriptorRegistry _registry = ModelDescriptorRegistry.Default();

    private ModelCapabilities For(string model) => _registry.CapabilitiesFor(model).Capabilities;

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11Ultra, true)]
    [InlineData(ModelConstants.Flux.Pro11,      false)]
    [InlineData(ModelConstants.Flux.Klein4b,    false)]
    [InlineData(ModelConstants.Flux.Flex2,      false)]
    [InlineData(ModelConstants.Flux.Pro2,       false)]
    [InlineData(ModelConstants.Flux.Max2,       false)]
    [InlineData(ModelConstants.OpenAI.GptImage15OnReplicate, false)]
    [InlineData(ModelConstants.Google.NanoBanana2,           false)]
    [InlineData("stability-ai/unknown-dynamic",              false)]
    public void ImagePromptStrength_TrueOnlyForFlux11ProUltra(string model, bool expected)
    {
        For(model).ImagePromptStrength.Should().Be(expected);
    }

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11,                   1)]
    [InlineData(ModelConstants.Flux.Pro11Ultra,              1)]
    [InlineData(ModelConstants.Flux.Klein4b,                 1)]
    [InlineData(ModelConstants.Flux.Flex2,                   1)]
    [InlineData(ModelConstants.Flux.Pro2,                    1)]
    [InlineData(ModelConstants.Flux.Max2,                    1)]
    [InlineData(ModelConstants.OpenAI.GptImage15OnReplicate, 10)]
    [InlineData(ModelConstants.Google.NanoBanana2,           14)]
    [InlineData("stability-ai/unknown-dynamic",              1)]
    public void MaxImageInputs_MatchesSchemaCaps(string model, int expected)
    {
        For(model).MaxImageInputs.Should().Be(expected);
    }

    [Theory]
    [InlineData(ModelConstants.Flux.Pro11)]
    [InlineData(ModelConstants.Flux.Pro11Ultra)]
    [InlineData(ModelConstants.Flux.Klein4b)]
    [InlineData(ModelConstants.OpenAI.GptImage15OnReplicate)]
    [InlineData(ModelConstants.Google.NanoBanana2)]
    public void MaxImageInputs_IsPositive_WheneverImagePromptSupported(string model)
    {
        var caps = For(model);
        caps.ImagePrompt.Should().BeTrue();
        caps.MaxImageInputs.Should().BeGreaterThan(0);
    }
}
