using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Descriptors.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// What happens when a model offers no aspect ratios at all.
///
/// Every shipped descriptor declares a non-empty list, but <see cref="ModelCapabilities"/> is a record
/// with no guard, so a descriptor for a model with no aspect-ratio field must not throw on the UI
/// thread when it is picked.
/// Part of <see cref="GeneratorViewModelTests"/> to reuse its mocks.
/// </summary>
public partial class GeneratorViewModelTests
{
    [Fact]
    public void SelectingAModelWithNoAspectRatios_KeepsTheCurrentRatioInsteadOfThrowing()
    {
        var noAspectRatios = new NoAspectRatioDescriptor("no-ar");

        var act = () =>
        {
            var vm = CreateViewModel(new ModelDescriptorRegistry(
                [noAspectRatios], [noAspectRatios], [], [noAspectRatios]));
            vm.Parameters.AspectRatio = "3:2";

            vm.ProviderFilter.SelectedModel =
                vm.ProviderFilter.AllModels.First(m => m.Value == "no-ar");

            return vm;
        };

        // The fallback for "the current value is not in the new list" was AspectRatios[0], which with
        // an empty list is an IndexOutOfRangeException out of a property setter on the UI thread.
        var selected = act.Should().NotThrow("a model with no aspect-ratio field is a descriptor " +
                                             "someone will eventually write, not a crash").Subject;

        // Keeping the value beats inventing one: any size picked from a list this model does not have
        // is a size the model never declared. The row is hidden by the capability anyway.
        selected.Parameters.AspectRatio.Should().Be("3:2");
        selected.AspectRatioOptions.Should().BeEmpty();
    }

    /// <summary>A model that has no aspect-ratio knob.</summary>
    private sealed class NoAspectRatioDescriptor(string modelId)
        : IPayloadBuilder, ICapabilityProvider, ICatalogSeedEntry
    {
        public string ModelId => modelId;

        public ModelCapabilities Capabilities => new(
            SafetyTolerance: false, PromptUpsampling: false, OutputQuality: false,
            AspectRatio: false, CustomDimensions: false, Seed: false, ImagePrompt: false,
            AspectRatioLabel: "Aspect ratio", AspectRatios: []);

        public ModelOption Seed => new(modelId, modelId, ProviderConstants.Replicate);

        public object Build(ImageGenerationParameters parameters) => new Dictionary<string, object?>();
    }
}
