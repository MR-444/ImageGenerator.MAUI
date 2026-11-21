using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.TestSupport;

/// <summary>
/// A real <see cref="GeneratorViewModel"/> wired from bare mocks. Every companion-page test (Idea to
/// Prompt, Mutation Engine, the Ideogram structure editor, the gallery) needs a host to read settings
/// through, because a companion page's model row is built from <c>generator.Settings</c> — and each of
/// those files had grown its own fourteen-mock copy of this, which meant that adding a constructor
/// parameter broke four test files instead of one.
///
/// Bare mocks are deliberate: nothing here is exercised except the settings the host carries. Write
/// what a test needs afterwards through the returned VM's <see cref="GeneratorViewModel.Settings"/>,
/// which is a real <see cref="AppSettingsCoordinator"/>.
/// </summary>
internal static class BareGenerator
{
    internal static GeneratorViewModel New() =>
        new(new Mock<IJobRunner>().Object,
            new Mock<IApiTokenStore>().Object,
            new Mock<IPollinationsTokenStore>().Object,
            new Mock<IComfyUiAuthStore>().Object,
            new Mock<ICivitaiTokenStore>().Object,
            new Mock<IAnthropicTokenStore>().Object,
            new Mock<ICivitaiPostingService>().Object,
            new Mock<IUiStateStore>().Object,
            new Mock<IModelCatalogCoordinator>().Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            new Mock<IFolderPicker>().Object,
            NullLogger<GeneratorViewModel>.Instance);
}
