using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.TestSupport;

/// <summary>
/// The two GeneratorViewModel collaborators that DI builds in the app. Each argument a test does
/// not care about gets a bare mock (or null where the app also allows one), so a test only names
/// the stores it verifies.
/// </summary>
internal static class TestCoordinators
{
    internal static TokenCoordinator Tokens(
        IApiTokenStore? replicate = null,
        IPollinationsTokenStore? pollinations = null,
        IComfyUiAuthStore? comfyUi = null,
        ICivitaiTokenStore? civitai = null,
        IAnthropicTokenStore? anthropic = null,
        IOpenRouterTokenStore? openRouter = null,
        ICivitaiPostingService? civitaiPosting = null) => new(
        replicate ?? new Mock<IApiTokenStore>().Object,
        pollinations ?? new Mock<IPollinationsTokenStore>().Object,
        comfyUi ?? new Mock<IComfyUiAuthStore>().Object,
        civitai ?? new Mock<ICivitaiTokenStore>().Object,
        anthropic ?? new Mock<IAnthropicTokenStore>().Object,
        openRouter,
        civitaiPosting ?? new Mock<ICivitaiPostingService>().Object);

    internal static AppSettingsCoordinator Settings(
        IUiStateStore uiStateStore,
        IFolderPicker? folderPicker = null,
        IOllamaModelCatalog? ollama = null,
        IOpenRouterModelCatalog? openRouter = null,
        IAnthropicModelCatalog? anthropic = null,
        IAnthropicTokenStore? anthropicTokenStore = null) => new(
        uiStateStore,
        folderPicker ?? new Mock<IFolderPicker>().Object,
        ollama,
        openRouter,
        anthropic,
        anthropicTokenStore ?? new Mock<IAnthropicTokenStore>().Object,
        NullLogger<AppSettingsCoordinator>.Instance);
}
