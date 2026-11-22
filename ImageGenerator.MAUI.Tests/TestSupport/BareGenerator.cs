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
/// through, because a companion page's model row is built from <c>generator.Settings</c>. One shared
/// copy means a constructor change touches one test file, not every companion-page test.
///
/// Bare mocks are deliberate: nothing here is exercised except the settings the host carries. Write
/// what a test needs afterwards through the returned VM's <see cref="GeneratorViewModel.Settings"/>,
/// which is a real <see cref="AppSettingsCoordinator"/>.
/// </summary>
internal static class BareGenerator
{
    internal static GeneratorViewModel New() =>
        new(new Mock<IJobRunner>().Object,
            TestCoordinators.Tokens(),
            TestCoordinators.Settings(new Mock<IUiStateStore>().Object),
            new Mock<ICivitaiPostingService>().Object,
            new Mock<IUiStateStore>().Object,
            new Mock<IModelCatalogCoordinator>().Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            NullLogger<GeneratorViewModel>.Instance);
}
