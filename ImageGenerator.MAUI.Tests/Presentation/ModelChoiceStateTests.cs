using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Moq;

namespace ImageGenerator.MAUI.Tests.Presentation;

/// <summary>
/// The rules a model picker has always had and never stated.
///
/// <para>
/// Every one of these used to live in a page's code-behind or a view model's unload handler, where the
/// only way to check it was to read markup and hope. Four pages got some of them wrong in four different
/// ways — most expensively, a pick restored to a picker on unload and then persisted as though the user
/// had chosen it. <see cref="ModelChoiceState"/> touches no MAUI type, so all of it is testable here.
/// </para>
/// </summary>
public class ModelChoiceStateTests
{
    private const ModelRole Role = ModelRole.Mutate;

    private readonly Mock<IUiStateStore> _store = new();
    private readonly Dictionary<(ModelRole Role, ModelProvider Provider), string?> _models = new();
    private readonly Dictionary<ModelRole, ModelProvider> _providers = new();

    private readonly FakeSource _local = new(["llama3:8b", "qwen2.5:7b"], "Found 2 model(s) on Ollama.");
    private readonly FakeSource _anthropic = new(["claude-sonnet-4-6"], "Found 1 Anthropic model.");
    private readonly FakeSource _openRouter = new(["acme/writer"], "Found 1 OpenRouter model.");

    public ModelChoiceStateTests()
    {
        // The fake store honours the caller's fallback, so a test that passes Local and gets Anthropic
        // knows it is reading a rule, not a coincidence of the fake.
        _store.Setup(s => s.LoadLlmProvider(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>()))
            .Returns((ModelRole r, ModelProvider fallback) =>
                _providers.TryGetValue(r, out var stored) ? stored : fallback);
        _store.Setup(s => s.LoadLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>()))
            .Returns((ModelRole r, ModelProvider p) => _models.GetValueOrDefault((r, p)));
        _store.Setup(s => s.PersistLlmProvider(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>()))
            .Callback((ModelRole r, ModelProvider p) => _providers[r] = p);
        _store.Setup(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()))
            .Callback((ModelRole r, ModelProvider p, string? m) => _models[(r, p)] = m);
    }

    private ModelChoiceState Build(
        ModelProvider defaultProvider = ModelProvider.Anthropic,
        IReadOnlyDictionary<ModelProvider, IModelSource>? sources = null,
        Func<ModelProvider, string?>? helperFor = null,
        Func<ModelProvider, string?>? baseUrlFor = null)
        => new(Role, _store.Object, sources ?? TextSources(), defaultProvider, helperFor, baseUrlFor);

    private Dictionary<ModelProvider, IModelSource> TextSources() => new()
    {
        [ModelProvider.Local] = _local,
        [ModelProvider.Anthropic] = _anthropic,
        [ModelProvider.OpenRouter] = _openRouter,
    };

    private Dictionary<ModelProvider, IModelSource> VisionSources() => new()
    {
        [ModelProvider.Local] = _local,
        [ModelProvider.OpenRouter] = _openRouter,
    };

    // ── what a role is offered ────────────────────────────────────────────────────────

    [Fact]
    public void ARoleIsOfferedExactlyTheProvidersItWasGivenSourcesFor()
    {
        var sut = Build(sources: VisionSources());

        sut.Providers.Should().Equal([ModelProvider.Local, ModelProvider.OpenRouter],
            "an observer sends images and a text-only role does not; the two are not offered the same list");
        sut.Providers.Should().NotContain(ModelProvider.Anthropic);
    }

    [Fact]
    public void ProvidersAreListedInOneOrderEveryTime()
    {
        var first = Build();
        var second = Build();

        second.Providers.Should().Equal(first.Providers);
        first.Providers.Should().Equal([ModelProvider.Local, ModelProvider.Anthropic, ModelProvider.OpenRouter],
            "a picker whose entries reshuffle between launches is a picker the user cannot learn");
    }

    [Fact]
    public void WithoutAnyProviderItRefusesInsteadOfRenderingAnEmptyRow()
    {
        var act = () => Build(sources: new Dictionary<ModelProvider, IModelSource>());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AProviderTheRoleNoLongerHasIsNotRememberedAsTheChoice()
    {
        _providers[Role] = ModelProvider.Anthropic;

        var sut = Build(sources: VisionSources(), defaultProvider: ModelProvider.Local);

        sut.SelectedProvider.Should().Be(ModelProvider.Local);
        sut.Models.Should().Contain("llama3:8b");
        sut.Status.Should().Be("Found 2 model(s) on Ollama.", "the row must show the provider it fell back to");
    }

    [Fact]
    public void TheFallbackOnlyAppliesWhenNothingIsStored()
    {
        _providers[Role] = ModelProvider.OpenRouter;

        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.SelectedProvider.Should().Be(ModelProvider.OpenRouter);
    }

    // ── opening a page must not change anything ───────────────────────────────────────

    [Fact]
    public void BuildingARowPersistsNothing()
    {
        _models[(Role, ModelProvider.Anthropic)] = "claude-sonnet-4-6";

        var sut = Build();

        sut.SelectedModel.Should().Be("claude-sonnet-4-6");
        _store.Verify(s => s.PersistLlmProvider(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>()), Times.Never);
        _store.Verify(s => s.PersistLlmModel(It.IsAny<ModelRole>(), It.IsAny<ModelProvider>(), It.IsAny<string?>()),
            Times.Never, "opening a page is not the user making a choice");
    }

    [Fact]
    public void ASavedPickIsListedBeforeAnythingHasBeenRefreshed()
    {
        _models[(Role, ModelProvider.Anthropic)] = "claude-haiku-4-5";

        var sut = Build();

        sut.Models.Should().Contain("claude-haiku-4-5",
            "the row has to be able to say what would be used, and a blank picker cannot");
        sut.SelectedModel.Should().Be("claude-haiku-4-5");
    }

    // ── choosing a provider ───────────────────────────────────────────────────────────

    [Fact]
    public void OpeningAPageAsksNobodyEvenWhenThereIsNothingToList()
    {
        var quiet = new FakeSource([]);

        var sut = Build(
            defaultProvider: ModelProvider.Anthropic,
            sources: new Dictionary<ModelProvider, IModelSource> { [ModelProvider.Anthropic] = quiet });

        quiet.Refreshes.Should().Be(0,
            "a page being opened is not a question to a provider; only the user choosing one is");
        sut.Models.Should().BeEmpty();
    }

    [Fact]
    public void ChoosingAProviderSavesItAndSwitchesToThatProvidersMemory()
    {
        _models[(Role, ModelProvider.Local)] = "llama3:8b";
        _models[(Role, ModelProvider.OpenRouter)] = "acme/writer";
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.SelectedProvider = ModelProvider.OpenRouter;

        _providers[Role].Should().Be(ModelProvider.OpenRouter);
        sut.SelectedModel.Should().Be("acme/writer");
        sut.Models.Should().Contain("acme/writer").And.NotContain("llama3:8b");
        sut.Status.Should().Be("Found 1 OpenRouter model.");
    }

    [Fact]
    public void ChoosingAProviderNeverCarriesAnotherProvidersModelOver()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);
        sut.SelectedModel = "llama3:8b";

        sut.SelectedProvider = ModelProvider.Anthropic;

        sut.SelectedModel.Should().NotBe("llama3:8b",
            "an Ollama tag is not a Claude id, and a name the endpoint cannot parse fails far from this row");
        _models[(Role, ModelProvider.Local)].Should().Be("llama3:8b",
            "and coming back must find the tag where it was left");
    }

    [Fact]
    public void SwitchingProvidersDoesNotWriteTheModelItJustLoaded()
    {
        _models[(Role, ModelProvider.OpenRouter)] = "acme/writer";
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.SelectedProvider = ModelProvider.OpenRouter;

        _store.Verify(s => s.PersistLlmModel(Role, ModelProvider.OpenRouter, It.IsAny<string?>()), Times.Never,
            "loading a saved value and choosing one are different events and must not look alike to the store");
    }

    [Fact]
    public void AProviderTheRoleWasNotGivenIsRejected()
    {
        var sut = Build(sources: VisionSources(), defaultProvider: ModelProvider.Local);

        var act = () => sut.SelectedProvider = ModelProvider.Anthropic;

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SwitchingToAProviderWithNothingToListAsksIt()
    {
        var nothingYet = new FakeSource([]);
        var sut = Build(
            defaultProvider: ModelProvider.Local,
            sources: new Dictionary<ModelProvider, IModelSource>
            {
                [ModelProvider.Local] = _local,
                [ModelProvider.Anthropic] = nothingYet,
            });

        sut.SelectedProvider = ModelProvider.Anthropic;

        nothingYet.Refreshes.Should().Be(1,
            "an empty list is not an answer, and a list nobody asked for looks exactly like one that came back empty");
    }

    [Fact]
    public void SwitchingToAProviderThatAlreadyHasModelsAsksNobody()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.SelectedProvider = ModelProvider.Anthropic;

        _anthropic.Refreshes.Should().Be(0,
            "those rows are the answer to a question already asked; asking twice to see the same list is a way to earn a rate limit");
    }

    [Fact]
    public void AnAutomaticRefreshThatThrowsSaysSoOnTheRow()
    {
        var throws = new FakeSource([]) { RefreshBody = () => throw new HttpRequestException("no route to host") };
        var sut = Build(
            defaultProvider: ModelProvider.Local,
            sources: new Dictionary<ModelProvider, IModelSource>
            {
                [ModelProvider.Local] = _local,
                [ModelProvider.Anthropic] = throws,
            });

        sut.SelectedProvider = ModelProvider.Anthropic;

        sut.Status.Should().Contain("Couldn't ask for models").And.Contain("no route to host",
            "a refresh nobody pressed can still fail, and a failure nobody can see is the bug this row is meant to end");
    }

    // ── choosing a model ──────────────────────────────────────────────────────────────

    [Fact]
    public void ChoosingAModelSavesItAgainstRoleAndProvider()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.SelectedModel = "qwen2.5:7b";

        _models[(Role, ModelProvider.Local)].Should().Be("qwen2.5:7b");
    }

    [Fact]
    public void TwoRolesNeverShareAModelValue()
    {
        var mutate = Build(defaultProvider: ModelProvider.Local);
        var writer = new ModelChoiceState(
            ModelRole.PromptWrite, _store.Object, TextSources(), ModelProvider.Local);

        mutate.SelectedModel = "qwen2.5:7b";
        writer.SelectedModel = "llama3:8b";

        _models[(ModelRole.Mutate, ModelProvider.Local)].Should().Be("qwen2.5:7b");
        _models[(ModelRole.PromptWrite, ModelProvider.Local)].Should().Be("llama3:8b");
        mutate.SelectedModel.Should().Be("qwen2.5:7b",
            "this is the defect the whole plan started from: one setting written by four pickers");
    }

    // ── the provider answering behind the user's back ─────────────────────────────────

    [Fact]
    public void StatusFollowsWhicheverProviderIsSelected()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);

        _local.SetStatus("Ollama is unreachable.");
        sut.Status.Should().Be("Ollama is unreachable.");

        sut.SelectedProvider = ModelProvider.Anthropic;
        _local.SetStatus("Found 2 model(s) on Ollama.");

        sut.Status.Should().NotContain("Ollama",
            "an Ollama answer must not be readable as the result of the Anthropic card");
    }

    [Fact]
    public void AListRefreshKeepsAPickTheProviderHasDropped()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);
        sut.SelectedModel = "mistral:latest";

        _local.SetModels("llama3:8b", "qwen2.5:7b");

        sut.SelectedModel.Should().Be("mistral:latest");
        sut.Models.Should().Contain("mistral:latest",
            "silently selecting something else shows the user a model they never chose");
    }

    [Fact]
    public void AProviderThatListedNothingDoesNotClearThePick()
    {
        _models[(Role, ModelProvider.Local)] = "llama3:8b";
        var sut = Build(defaultProvider: ModelProvider.Local);

        _local.SetModels();

        sut.SelectedModel.Should().Be("llama3:8b",
            "nothing installed and a wrong URL look identical, and a connection problem must not cost a choice");
    }

    [Fact]
    public void RefreshAsksTheSelectedProviderAndNobodyElse()
    {
        var sut = Build(defaultProvider: ModelProvider.Anthropic);

        sut.RefreshCommand.ExecuteAsync(null).Wait();

        _anthropic.Refreshes.Should().Be(1);
        _local.Refreshes.Should().Be(0);
    }

    [Fact]
    public async Task RefreshCompletesWhenTheProviderHasAnswered()
    {
        var stillThinking = new TaskCompletionSource();
        _anthropic.RefreshBody = () => stillThinking.Task;
        var sut = Build(defaultProvider: ModelProvider.Anthropic);

        var refreshing = sut.RefreshCommand.ExecuteAsync(null);

        refreshing.IsCompleted.Should().BeFalse(
            "a caller that awaits a refresh and then reads the list must not be reading the previous answer");
        stillThinking.SetResult();
        await refreshing;
    }

    [Fact]
    public void HelperDependsOnTheProviderBecauseWhatIsWorthWarningAboutDiffers()
    {
        var sut = Build(
            defaultProvider: ModelProvider.Local,
            helperFor: p => p == ModelProvider.Local ? "Free, local, not guaranteed." : "Bills per call.");

        sut.Helper.Should().Be("Free, local, not guaranteed.");

        var helperChanges = 0;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModelChoiceState.Helper)) helperChanges++;
        };

        sut.SelectedProvider = ModelProvider.Anthropic;

        sut.Helper.Should().Be("Bills per call.");
        helperChanges.Should().Be(1, "the row shows the sentence and has to be told it changed");
    }

    [Fact]
    public void LeavingAProviderStopsListeningToIt()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);
        _local.Listeners.Should().Be(2, "one delegate for the list, one for the status line");

        sut.SelectedProvider = ModelProvider.Anthropic;

        _local.Listeners.Should().Be(0,
            "the sources belong to the coordinator and outlive every page: a row that keeps listening to a "
            + "provider it left gains two delegates per navigation and nothing ever removes them");
        _anthropic.Listeners.Should().Be(2);

        sut.SelectedProvider = ModelProvider.Local;

        _anthropic.Listeners.Should().Be(0);
        _local.Listeners.Should().Be(2, "coming back listens again rather than twice");
    }

    // ── what the selection amounts to when something asks to run ────────────────────

    [Fact]
    public void TheTargetIsTheSelection()
    {
        var urls = new Dictionary<ModelProvider, string?> { [ModelProvider.Local] = "http://box:11434" };
        var sut = Build(defaultProvider: ModelProvider.Local, baseUrlFor: p => urls.GetValueOrDefault(p));
        sut.SelectedModel = "llama3:8b";

        sut.Target.Provider.Should().Be(ModelProvider.Local);
        sut.Target.ModelId.Should().Be("llama3:8b");
        sut.Target.BaseUrl.Should().Be("http://box:11434");
    }

    [Fact]
    public void TheTargetAsksForTheAddressOfWhicheverProviderIsSelected()
    {
        var sut = Build(defaultProvider: ModelProvider.Local, baseUrlFor: p => p == ModelProvider.Local ? "http://box:11434" : null);
        sut.SelectedModel = "llama3:8b";

        sut.SelectedProvider = ModelProvider.Anthropic;
        sut.SelectedModel = "claude-sonnet-4-6";

        sut.Target.BaseUrl.Should().BeEmpty("the hosted providers answer at one address the product knows");
        sut.Target.Provider.Should().Be(ModelProvider.Anthropic);
    }

    [Fact]
    public void NothingPickedStaysNothingPicked()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.Target.HasModel.Should().BeFalse();
        sut.Target.ModelId.Should().BeEmpty(
            "the services already turn a missing pick into the right sentence for the provider; a second "
            + "path that guesses a model instead is how a 404 from a server that never had it gets written");
    }

    [Fact]
    public void ATargetWrittenUnderOneProviderIsNotTheTargetUnderTheNext()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);
        sut.SelectedModel = "llama3:8b";
        var local = sut.Target;

        sut.SelectedProvider = ModelProvider.OpenRouter;

        local.Provider.Should().Be(ModelProvider.Local,
            "the run that started with Ollama keeps calling Ollama even if the picker moves underneath it");
        sut.Target.Provider.Should().Be(ModelProvider.OpenRouter);
    }

    // ── the picker's position, which is how a Picker talks ──────────────────────────

    [Fact]
    public void ChoosingByPositionChoosesTheProviderAtThatPosition()
    {
        var sut = Build(defaultProvider: ModelProvider.Local);

        sut.IndexOfProvider(ModelProvider.Local).Should().Be(0);
        sut.IndexOfProvider(ModelProvider.OpenRouter).Should().Be(2);

        sut.SelectedProvider = ModelProvider.OpenRouter;
        sut.IndexOfProvider(sut.SelectedProvider).Should().Be(2, "a picker that reopens on the wrong row lies about what is picked");
    }

    private sealed class FakeSource(IEnumerable<string> models, string? status = null) : IModelSource
    {
        private readonly List<string> _models = [.. models];

        public string? Status { get; private set; } = status;

        public int Refreshes { get; private set; }

        /// <summary>How many rows are watching this source. A source outlives the pages watching it, so
        /// this number is a leak meter, and detaching on switch is the only thing keeping it honest.</summary>
        public int Listeners
            => (ModelsChanged?.GetInvocationList().Length ?? 0) + (StatusChanged?.GetInvocationList().Length ?? 0);

        public Func<Task>? RefreshBody { get; set; }

        public IReadOnlyList<string> Models => _models;

        public event EventHandler? ModelsChanged;
        public event EventHandler? StatusChanged;

        public Task RefreshAsync()
        {
            Refreshes++;
            return RefreshBody?.Invoke() ?? Task.CompletedTask;
        }

        public void SetModels(params string[] models)
        {
            _models.Clear();
            _models.AddRange(models);
            ModelsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetStatus(string? status)
        {
            Status = status;
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
