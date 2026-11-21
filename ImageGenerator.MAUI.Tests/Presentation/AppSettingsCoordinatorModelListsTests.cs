using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.Converters;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Presentation;

/// <summary>
/// The two model lists the text roles will pick from, and the words every provider picker uses.
///
/// <para>
/// These are the first tests to construct <see cref="AppSettingsCoordinator"/> directly. Until now its
/// 471 lines were only ever reached through <see cref="GeneratorViewModel"/>, which meant a rule about
/// the settings surface had to be asserted through a 21-parameter constructor — the reason three of its
/// refresh paths could report to a status bar nobody was looking at.
/// </para>
/// </summary>
public class AppSettingsCoordinatorModelListsTests
{
    private readonly Mock<IUiStateStore> _store = new();
    private readonly List<(string Message, StatusKind Kind)> _reported = [];
    private readonly Mock<IOpenRouterModelCatalog> _openRouter = new();
    private readonly Mock<IAnthropicModelCatalog> _anthropic = new();
    private readonly Mock<IAnthropicTokenStore> _anthropicKey = new();

    /// <summary>Everything wired. A missing dependency is built explicitly by <see cref="BuildWithoutOpenRouter"/>
    /// and <see cref="BuildWithoutAnthropic"/>, never by passing null here: an <c>a ?? fallback</c> in a
    /// helper signature makes "no catalog" indistinguishable from "don't care", and the null-dependency
    /// tests below then silently exercise the wired one.</summary>
    private AppSettingsCoordinator Build()
        => BuildCore(_openRouter.Object, _anthropic.Object, _anthropicKey.Object);

    private AppSettingsCoordinator BuildWithoutOpenRouter() => BuildCore(null, _anthropic.Object, _anthropicKey.Object);

    private AppSettingsCoordinator BuildWithoutAnthropic() => BuildCore(_openRouter.Object, null, _anthropicKey.Object);

    private AppSettingsCoordinator BuildCore(
        IOpenRouterModelCatalog? openRouterCatalog,
        IAnthropicModelCatalog? anthropicCatalog,
        IAnthropicTokenStore? anthropicKey)
        => new(
            _store.Object,
            new Mock<IFolderPicker>().Object,
            ollamaModelCatalog: null,
            openRouterModelCatalog: openRouterCatalog,
            anthropicModelCatalog: anthropicCatalog,
            anthropicTokenStore: anthropicKey,
            setStatus: (message, kind) => _reported.Add((message, kind)),
            logger: NullLogger.Instance);

    // ── provider labels ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ModelProvider.Local, "Local (Ollama)")]
    [InlineData(ModelProvider.Anthropic, "Anthropic")]
    [InlineData(ModelProvider.OpenRouter, "OpenRouter")]
    public void ListLabel_SaysWhoAnswersInWordsAChoiceIsMadeOf(ModelProvider provider, string expected)
        => provider.ForList().Should().Be(expected);

    [Theory]
    [InlineData(ModelProvider.Local, "Ollama")]
    [InlineData(ModelProvider.Anthropic, "Claude")]
    [InlineData(ModelProvider.OpenRouter, "OpenRouter")]
    public void ProseLabel_KeepsTheBrandSentencesAlreadyUse(ModelProvider provider, string expected)
    {
        // Pinned because every "No Claude model selected" in the app already quotes these words; the
        // label rule was moved into ModelProviderLabels, not reworded.
        provider.ForProse().Should().Be(expected);
        new LlmTarget(provider, "m", string.Empty).ProviderLabel.Should().Be(expected);
    }

    [Fact]
    public void NoPickerLabelSpellsAProviderTheWayTheEnumDoes()
    {
        // The report that started this: one page showed "Local", its neighbour "LocalOllama", both
        // straight from ToString(). Neither string is allowed to come back through the label rule.
        var labels = new[] { ModelProvider.Local, ModelProvider.Anthropic, ModelProvider.OpenRouter }
            .Select(p => p.ForList());

        labels.Should().NotContain("LocalOllama");
        ModelProvider.Local.ForList().Should().NotBe("Local", "a bare \"Local\" does not say what to install");
    }

    // ── OpenRouter's text list ────────────────────────────────────────────────────────

    [Fact]
    public async Task TextList_AsksWithTheChatFilterAndNeverTheVisionOne()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("acme/writer", "Writer", false)]);
        var sut = Build();

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        _openRouter.Verify(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()), Times.Once);
        _openRouter.Verify(c => c.ListVisionModelsAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "an embedding or image-only model is a fine writer and the vision filter hides it");
    }

    [Fact]
    public async Task TextList_FillsThePickerAndReportsWhatItFound()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new OpenRouterModelInfo("acme/writer", "Writer", false),
                new OpenRouterModelInfo("acme/other", "Other", true)
            ]);
        var sut = Build();

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        sut.OpenRouterTextModels.Should().Equal("acme/writer", "acme/other");
        sut.OpenRouterTextStatus.Should().Be("Found 2 OpenRouter model(s) that answer in text.");
        _reported.Should().ContainSingle(r => r.Kind == StatusKind.Success);
    }

    [Fact]
    public async Task TextList_AppliesNoFreeOnlyFilterBecauseTheSwitchIsOnAnotherCard()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("acme/free", "Free", true)]);
        var sut = Build();
        sut.OpenRouterVisionFreeOnly = true; // the observer's switch, persisted, defaults the vision card

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        sut.OpenRouterTextModels.Should().BeEmpty(
            "honouring a switch that is not on this page would hide paid models silently");
        _openRouter.Verify(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TextList_SaysSoWhenTheServerRefuses()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("unreachable"));
        var sut = Build();

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        sut.OpenRouterTextStatus.Should().Contain("Couldn't refresh OpenRouter models")
            .And.Contain("unreachable");
        _reported.Should().Contain(r => r.Kind == StatusKind.Error,
            "a catch that only writes a log line is the bug this card exists to fix");
    }

    [Fact]
    public async Task TextList_WithoutACatalogExplainsItselfInsteadOfSilentlyDoingNothing()
    {
        var sut = BuildWithoutOpenRouter();

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        sut.OpenRouterTextStatus.Should().Contain("unavailable");
    }

    [Fact]
    public async Task TextList_LeavesTheVisionCardsStatusAlone()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));
        var sut = Build();

        await sut.RefreshOpenRouterTextModelsCommand.ExecuteAsync(null);

        sut.OpenRouterVisionStatus.Should().BeNull(
            "two cards, two answers: overwriting one makes the user read a result for a button they did not press");
    }

    // ── Anthropic's list ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnthropicList_AsksWithTheKeyThatIsStored()
    {
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync("sk-ant-test");
        _anthropic.Setup(c => c.ListTextModelsAsync("sk-ant-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Active("claude-sonnet-4-6")]);
        var sut = Build();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        sut.AnthropicTextModels.Should().Equal("claude-sonnet-4-6");
    }

    [Fact]
    public async Task AnthropicList_HandsTheCatalogAnEmptyKeyRatherThanANull()
    {
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync((string?)null);
        _anthropic.Setup(c => c.ListTextModelsAsync("", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var sut = Build();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        // The catalog owns the "no key" sentence; the coordinator's job is not to invent a second one
        // or let a null reach a non-nullable parameter.
        _anthropic.Verify(c => c.ListTextModelsAsync("", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnthropicList_KeepsDeprecatedModelsAndSaysHowMany()
    {
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync("sk-ant-test");
        _anthropic.Setup(c => c.ListTextModelsAsync("sk-ant-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Active("claude-sonnet-4-6"),
                new AnthropicModelInfo("claude-opus-4-0", "Claude Opus 4", AnthropicModelInfo.Deprecated, true)
            ]);
        var sut = Build();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        sut.AnthropicTextModels.Should().Equal(["claude-sonnet-4-6", "claude-opus-4-0"],
            "a deprecated model still answers for the keys allowed to use it; hiding it is a guess about who is here");
        sut.AnthropicStatus.Should().Contain("1 of them deprecated");
    }

    [Fact]
    public async Task AnthropicList_ReportsAMissingKeyAsTheSentencesAlreadySayIt()
    {
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync(null as string);
        _anthropic.Setup(c => c.ListTextModelsAsync("", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No Anthropic API key — add it on the Settings page."));
        var sut = Build();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        sut.AnthropicStatus.Should().Contain("No Anthropic API key")
            .And.Contain("Settings page", "the row must repeat the instruction, not paraphrase it");
        _reported.Should().Contain(r => r.Kind == StatusKind.Error);
    }

    [Fact]
    public async Task AnthropicList_WithoutACatalogExplainsItself()
    {
        var sut = BuildWithoutAnthropic();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        sut.AnthropicStatus.Should().Contain("unavailable");
    }

    [Fact]
    public async Task AnthropicList_SaysSoWhenTheServerRefuses()
    {
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync("sk-ant-test");
        _anthropic.Setup(c => c.ListTextModelsAsync("sk-ant-test", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("429"));
        var sut = Build();

        await sut.RefreshAnthropicModelsCommand.ExecuteAsync(null);

        sut.AnthropicStatus.Should().Contain("Couldn't refresh Anthropic models").And.Contain("429");
    }

    private static AnthropicModelInfo Active(string id)
        => new(id, id, AnthropicModelInfo.Active, false);

    // ── the sources a ModelChoiceState is built from ───────────────────────────────
    //
    // Five entries in two dictionaries, each naming a list, a status property (as a string, because the
    // statuses are source-generated) and a refresh command. That shape is an invitation to a copy-paste
    // error in which one provider's row refreshes another's, which is why each entry is checked rather
    // than assumed.

    [Fact]
    public void TheSourcesHandOverTheVeryCollectionsThePagesBind()
    {
        var sut = Build();

        ReferenceEquals(sut.TextModelSources[ModelProvider.Local].Models, sut.OllamaModels).Should().BeTrue();
        ReferenceEquals(sut.TextModelSources[ModelProvider.OpenRouter].Models, sut.OpenRouterTextModels).Should().BeTrue();
        ReferenceEquals(sut.TextModelSources[ModelProvider.Anthropic].Models, sut.AnthropicTextModels).Should().BeTrue();
        ReferenceEquals(sut.VisionModelSources[ModelProvider.Local].Models, sut.OllamaVisionModels).Should().BeTrue();
        ReferenceEquals(sut.VisionModelSources[ModelProvider.OpenRouter].Models, sut.OpenRouterVisionModels).Should().BeTrue();
    }

    [Fact]
    public void TheVisionRoleIsNotOfferedAnthropic()
    {
        var sut = Build();

        sut.VisionModelSources.Should().NotContainKey(ModelProvider.Anthropic,
            "an Anthropic vision call is a decision nobody has taken, and a source in this dictionary is an offer in the UI");
        sut.TextModelSources.Should().ContainKey(ModelProvider.Anthropic);
    }

    [Fact]
    public void ReadingTheSourcesTwiceGivesTheSameObjects()
    {
        var sut = Build();

        ReferenceEquals(sut.TextModelSources, sut.TextModelSources).Should().BeTrue();
        ReferenceEquals(
            sut.TextModelSources[ModelProvider.Anthropic],
            sut.TextModelSources[ModelProvider.Anthropic]).Should().BeTrue();
    }

    [Theory]
    [InlineData(ModelProvider.Local)]
    [InlineData(ModelProvider.Anthropic)]
    [InlineData(ModelProvider.OpenRouter)]
    public void AStatusWrittenForOneProviderReachesOnlyThatProvidersSource(ModelProvider provider)
    {
        var sut = Build();
        var text = sut.TextModelSources;
        var reached = new List<ModelProvider>();
        foreach (var (key, source) in text)
            source.StatusChanged += (_, _) => reached.Add(key);

        // The names below are the string coupling in ModelSource: if one of these properties is renamed,
        // the write still compiles and the row silently stops reporting. This is the only check.
        switch (provider)
        {
            case ModelProvider.Local: sut.OllamaServerStatus = "local says hello"; break;
            case ModelProvider.Anthropic: sut.AnthropicStatus = "anthropic says hello"; break;
            default: sut.OpenRouterTextStatus = "openrouter says hello"; break;
        }

        reached.Should().Equal([provider]);
        text[provider].Status.Should().Contain("says hello");
    }

    [Fact]
    public async Task RefreshingThroughATextSourceFillsThatSourceAndNotItsNeighbour()
    {
        _openRouter.Setup(c => c.ListChatModelsAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OpenRouterModelInfo("acme/writer", "Writer", false)]);
        _anthropicKey.Setup(s => s.LoadAsync()).ReturnsAsync("sk-ant-test");
        _anthropic.Setup(c => c.ListTextModelsAsync("sk-ant-test", It.IsAny<CancellationToken>()))
            .ReturnsAsync([Active("claude-sonnet-4-6")]);
        var sut = Build();

        await sut.TextModelSources[ModelProvider.OpenRouter].RefreshAsync();

        sut.OpenRouterTextModels.Should().Equal("acme/writer");
        sut.AnthropicTextModels.Should().BeEmpty();
        sut.OpenRouterVisionModels.Should().BeEmpty();

        await sut.TextModelSources[ModelProvider.Anthropic].RefreshAsync();

        sut.AnthropicTextModels.Should().Equal("claude-sonnet-4-6");
    }
}
