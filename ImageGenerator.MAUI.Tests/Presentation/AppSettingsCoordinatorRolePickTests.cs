using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Presentation;

/// <summary>
/// A pick made in a row survives an Ollama refresh. Runs against the real <see cref="UiStateStore"/>,
/// because the defect is two writers sharing one preference key, which a mock cannot show.
/// </summary>
public class AppSettingsCoordinatorRolePickTests
{
    private readonly StubPreferences _preferences = new();
    private readonly UiStateStore _store;
    private readonly Mock<IOllamaModelCatalog> _ollama = new();

    public AppSettingsCoordinatorRolePickTests()
    {
        _store = new UiStateStore(NullLogger<UiStateStore>.Instance, _preferences);
        _ollama.Setup(c => c.ListModelInfosAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OllamaModelInfo("llama3", ["completion"]),
                new OllamaModelInfo("qwen", ["completion"]),
                new OllamaModelInfo("llava", ["completion", "vision"]),
                new OllamaModelInfo("moondream", ["completion", "vision"]),
            ]);
    }

    private AppSettingsCoordinator Build() => new(
        _store, new Mock<IFolderPicker>().Object, _ollama.Object,
        openRouterModelCatalog: null, anthropicModelCatalog: null, anthropicTokenStore: null,
        setStatus: (_, _) => { }, logger: NullLogger.Instance);

    [Theory]
    [InlineData(ModelRole.PromptWrite)]
    [InlineData(ModelRole.Mutate)]
    [InlineData(ModelRole.Enrich)]
    public async Task ARowPickSurvivesAnOllamaRefresh(ModelRole role)
    {
        _store.PersistLlmModel(role, ModelProvider.Local, "llama3");
        var settings = Build();
        settings.LoadSavedSettings();
        var row = new ModelChoiceState(role, _store, settings.TextModelSources, ModelProvider.Local);

        row.SelectedModel = "qwen";
        await settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        _store.LoadLlmModel(role, ModelProvider.Local).Should().Be("qwen");
    }

    [Fact]
    public async Task ARefreshStillDropsAModelTheServerNoLongerHas()
    {
        _store.PersistLlmModel(ModelRole.Mutate, ModelProvider.Local, "gone");
        var settings = Build();
        settings.LoadSavedSettings();

        await settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        _store.LoadLlmModel(ModelRole.Mutate, ModelProvider.Local).Should().BeNullOrEmpty();
    }
}
