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

    private readonly Mock<IOpenRouterModelCatalog> _openRouter = new();

    private AppSettingsCoordinator Build() => new(
        _store, new Mock<IFolderPicker>().Object, _ollama.Object,
        _openRouter.Object, anthropicModelCatalog: null, anthropicTokenStore: null,
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

    // ---- The observer: the same overwrite, through the Settings page's vision pickers -------------
    // Observe's row writes the very keys the Settings vision pickers wrote through the coordinator's
    // copies, so a refresh (or the free-only switch) put the stale copy back over the row's pick.

    [Fact]
    public async Task AnObserverLocalPickSurvivesAnOllamaRefresh()
    {
        _store.PersistLlmModel(ModelRole.Observe, ModelProvider.Local, "llava");
        var settings = Build();
        settings.LoadSavedSettings();
        var row = new ModelChoiceState(ModelRole.Observe, _store, settings.VisionModelSources, ModelProvider.Local);

        row.SelectedModel = "moondream";
        await settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        _store.LoadLlmModel(ModelRole.Observe, ModelProvider.Local).Should().Be("moondream");
    }

    [Fact]
    public async Task AnObserverOpenRouterPickSurvivesAnOpenRouterRefresh()
    {
        _openRouter.Setup(c => c.ListVisionModelsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("a/one:free", "One", true), new OpenRouterModelInfo("b/two:free", "Two", true)]);
        _store.PersistLlmModel(ModelRole.Observe, ModelProvider.OpenRouter, "a/one:free");
        var settings = Build();
        settings.LoadSavedSettings();
        var row = new ModelChoiceState(ModelRole.Observe, _store, settings.VisionModelSources, ModelProvider.OpenRouter);

        row.SelectedModel = "b/two:free";
        await settings.RefreshOpenRouterVisionModelsCommand.ExecuteAsync(null);

        _store.LoadLlmModel(ModelRole.Observe, ModelProvider.OpenRouter).Should().Be("b/two:free");
    }

    [Fact]
    public void TheFreeOnlySwitchLeavesTheObserversPickAlone()
    {
        // A paid pick is made with the filter off; turning the filter on must not clear it.
        _store.PersistOpenRouterFreeOnly(false);
        _store.PersistLlmModel(ModelRole.Observe, ModelProvider.OpenRouter, "paid/vision");
        var settings = Build();
        settings.LoadSavedSettings();

        settings.OpenRouterFreeOnly = true;

        _store.LoadLlmModel(ModelRole.Observe, ModelProvider.OpenRouter).Should().Be("paid/vision",
            "a filter on a list is not a decision about the role's model");
    }

    [Fact]
    public async Task ARefreshDropsAnObserverModelThatLostVision()
    {
        // llama3 is still installed, but not as a vision model: it can no longer serve an observation.
        _store.PersistLlmModel(ModelRole.Observe, ModelProvider.Local, "llama3");
        var settings = Build();
        settings.LoadSavedSettings();

        await settings.RefreshOllamaModelsCommand.ExecuteAsync(null);

        _store.LoadLlmModel(ModelRole.Observe, ModelProvider.Local).Should().BeNullOrEmpty();
        settings.OllamaServerStatus.Should().Contain("llama3");
    }

    // ---- A long list reaches a row as one change -------------------------------------------------
    // OpenRouter lists hundreds of models. Filling the shared list one Add at a time made every attached
    // row rebuild its whole copy per Add - hundreds squared picker updates on the UI thread, which froze
    // the idea page the moment the writer was switched to OpenRouter.

    [Fact]
    public async Task AHundredsLongRefreshReachesARowAsASingleChange()
    {
        var many = Enumerable.Range(0, 300)
            .Select(i => new OpenRouterModelInfo($"lab/model-{i}:free", $"Model {i}", true)).ToArray();
        _openRouter.Setup(c => c.ListChatModelsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(many);
        var settings = Build();
        settings.LoadSavedSettings();
        var row = new ModelChoiceState(ModelRole.PromptWrite, _store, settings.TextModelSources, ModelProvider.OpenRouter);
        var changes = 0;
        row.Models.CollectionChanged += (_, _) => changes++;

        await settings.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        row.Models.Should().HaveCount(300);
        changes.Should().BeLessThanOrEqualTo(2, "a picker redraws on every change it is told about");
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
