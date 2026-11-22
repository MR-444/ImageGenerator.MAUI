using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Shared.Constants;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// Picker sequences run against the real <see cref="UiStateStore"/> on in-memory preferences, so
/// the storage keys take part instead of a mock that answers whatever a test sets up. A new
/// view model on the same preferences is a restart.
/// </summary>
public partial class GeneratorViewModelTests
{
    private GeneratorViewModel RealStoreVm(StubPreferences preferences) => new(
        _mockJobRunner.Object, _mockTokenStore.Object, _mockPollinationsTokenStore.Object,
        _mockComfyUiAuthStore.Object, _mockCivitaiTokenStore.Object, _mockAnthropicTokenStore.Object,
        _mockCivitaiPostingService.Object,
        new UiStateStore(NullLogger<UiStateStore>.Instance, preferences, TimeSpan.FromMilliseconds(1)),
        _mockCatalogCoordinator.Object, ModelDescriptorRegistry.Default(), _mockPromptBatchParser.Object,
        _mockCheckpointService.Object, _mockGalleryService.Object, _mockFolderPicker.Object,
        NullLogger<GeneratorViewModel>.Instance);

    private static void Select(GeneratorViewModel vm, string modelId) =>
        vm.ProviderFilter.SelectedModel = vm.ProviderFilter.AllModels.First(m => m.Value == modelId);

    [Fact]
    public void RealStore_IdeogramResolutionPick_DoesNotCostSunburstItsRatio()
    {
        var preferences = new StubPreferences();
        var vm = RealStoreVm(preferences);
        Select(vm, ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        vm.Pickers.AspectRatio.Selected = "16:9";

        Select(vm, ModelConstants.Ideogram.V4Turbo);
        vm.Pickers.Resolution.Selected = "3168x1296"; // shown as "22:9 (3168x1296)"
        Select(vm, ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        vm.Parameters.AspectRatio.Should().Be("16:9");
    }

    [Fact]
    public void RealStore_AfterARestartOnIdeogram_SunburstGetsItsRatioBack()
    {
        var preferences = new StubPreferences();
        var before = RealStoreVm(preferences);
        Select(before, ModelConstants.OpenAI.GptImage26SunburstOnReplicate);
        before.Pickers.AspectRatio.Selected = "16:9";
        Select(before, ModelConstants.Ideogram.V4Turbo);
        before.Pickers.Resolution.Selected = "3168x1296";

        var after = RealStoreVm(preferences);
        after.LoadSavedUiState();
        after.Parameters.Model.Should().Be(ModelConstants.Ideogram.V4Turbo);
        Select(after, ModelConstants.OpenAI.GptImage26SunburstOnReplicate);

        after.Parameters.AspectRatio.Should().Be("16:9");
    }
}
