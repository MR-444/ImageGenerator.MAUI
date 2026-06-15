using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.External.Mutation;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

/// <summary>
/// The AI caption mutator's seam: a fake <see cref="CaptionMutationLlmService.MutationCompletion"/>
/// replaces the network so the request build, tier→model routing, validate-retry loop, and override
/// precedence run without disk or HTTP.
/// </summary>
public sealed class CaptionMutationLlmServiceTests : IDisposable
{
    private const string ValidJson =
        """{"high_level_description":"A red fox stepping through fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","desc":"a russet-red fox mid-step, breath fogging"}]}}""";

    // Parses fine but fails V4JsonPromptValidator (background is blank).
    private const string ValidationFailJson =
        """{"high_level_description":"x","compositional_deconstruction":{"background":"","elements":[{"type":"obj","desc":"y"}]}}""";

    private static readonly V4JsonPrompt Base = V4JsonPromptSerializer.Deserialize(ValidJson);

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "mutation-llm-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    // ---- Happy path + structured call ----------------------------------------------------

    [Fact]
    public async Task MutateAsync_ValidResponse_ReturnsValidatedPromptFromASchemaConstrainedCall()
    {
        var schemaKind = JsonValueKind.Undefined;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, schema, _) => { schemaKind = schema.ValueKind; return Task.FromResult(ValidJson); });

        var result = await sut.MutateAsync(Base, "make it winter", 0, ModelTier.Sonnet);

        result.Success.Should().BeTrue();
        result.Prompt.Should().NotBeNull();
        result.Label.Should().NotBeNullOrEmpty();
        schemaKind.Should().Be(JsonValueKind.Object, "every mutation call is constrained to the V4 schema");
    }

    // ---- Model routing (Sonnet / Opus / Local) -------------------------------------------

    [Theory]
    [InlineData(ModelTier.Sonnet, "claude-sonnet-4-6")]
    [InlineData(ModelTier.Opus, "claude-opus-4-8")]
    public async Task MutateAsync_AnthropicTiers_PassTheExpectedModelId(ModelTier tier, string expectedModel)
    {
        string? seenModel = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, modelId, _, _, _, _, _, _) => { seenModel = modelId; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, tier);

        seenModel.Should().Be(expectedModel);
    }

    [Fact]
    public async Task MutateAsync_LocalTier_UsesOllamaModelAndBaseUrl_AndNeedsNoKey()
    {
        var uiStore = new Mock<IUiStateStore>();
        uiStore.Setup(s => s.LoadOllamaModel()).Returns("llama3.1");
        uiStore.Setup(s => s.LoadOllamaBaseUrl()).Returns("http://host:11434");

        ModelTier seenTier = ModelTier.Sonnet;
        string? seenModel = null, seenBaseUrl = null, seenKey = "unset";
        var sut = CreateSut(KeyStore(null), // no Anthropic key at all
            (tier, modelId, apiKey, baseUrl, _, _, _, _) =>
            {
                seenTier = tier; seenModel = modelId; seenBaseUrl = baseUrl; seenKey = apiKey;
                return Task.FromResult(ValidJson);
            },
            uiStore);

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Local);

        result.Success.Should().BeTrue("the Local tier needs no Anthropic key");
        seenTier.Should().Be(ModelTier.Local);
        seenModel.Should().Be("llama3.1");
        seenBaseUrl.Should().Be("http://host:11434");
        seenKey.Should().BeNull();
    }

    [Fact]
    public async Task MutateAsync_AnthropicTier_NoApiKey_FailsWithoutCallingTheModel()
    {
        var called = false;
        var sut = CreateSut(KeyStore(null),
            (_, _, _, _, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse();
    }

    // ---- Validate + retry ----------------------------------------------------------------

    [Fact]
    public async Task MutateAsync_InvalidThenValid_RetriesWithFeedbackAndSucceeds()
    {
        var seen = new List<IReadOnlyList<ChatTurn>>();
        var call = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, messages, _, _) => { seen.Add(messages); return Task.FromResult(call++ == 0 ? ValidationFailJson : ValidJson); });

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeTrue();
        seen.Should().HaveCount(2);
        seen[1].Should().Contain(t => t.Role == "assistant");
        seen[1].Should().Contain(t => t.Role == "user" && t.Content.Contains("problems"));
    }

    [Fact]
    public async Task MutateAsync_ValidationFailsTwice_FailsWithTheErrors()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Background");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task MutateAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { calls++; return Task.FromResult("this is not json {"); });

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task MutateAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach the model");
    }

    // ---- Diversity by index --------------------------------------------------------------

    [Fact]
    public async Task MutateAsync_VariesTheUserTurnByIndex()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, messages, _, _) => { captured = messages[0].Content; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);
        captured.Should().Contain("variation #1");

        await sut.MutateAsync(Base, "x", 4, ModelTier.Sonnet);
        captured.Should().Contain("variation #5");
    }

    // ---- Breeding ------------------------------------------------------------------------

    [Fact]
    public async Task BreedAsync_NoWinners_FailsWithoutCalling()
    {
        var called = false;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.BreedAsync(Array.Empty<V4JsonPrompt>(), "x", 0, ModelTier.Sonnet);

        result.Success.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task BreedAsync_WithParents_SendsParentsAndOffspringIndex()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, messages, _, _) => { captured = messages[0].Content; return Task.FromResult(ValidJson); });

        var result = await sut.BreedAsync(new[] { Base, Base }, "blend them", 0, ModelTier.Sonnet);

        result.Success.Should().BeTrue();
        captured.Should().Contain("PARENT CAPTIONS").And.Contain("[2]").And.Contain("offspring #1");
    }

    // ---- System-prompt override precedence -----------------------------------------------

    [Fact]
    public async Task MutateAsync_OverridePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "mutation-prompt.md"), "PRIVATE MUTATION OVERRIDE");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        captured.Should().Be("PRIVATE MUTATION OVERRIDE");
    }

    [Fact]
    public async Task MutateAsync_NoOverride_UsesBundledAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, ModelTier.Sonnet);

        captured.Should().Be("BUNDLED MUTATION PROMPT");
    }

    [Fact]
    public void ShippedMutationSystemPrompt_ExistsAndIsNonEmpty()
    {
        var path = ShippedPromptPath();
        File.Exists(path).Should().BeTrue($"the bundled clean-room mutation prompt should ship at {path}");
        File.ReadAllText(path).Trim().Should().NotBeEmpty();
    }

    // ---- Helpers / fakes -----------------------------------------------------------------

    private CaptionMutationLlmService CreateSut(
        Mock<IAnthropicTokenStore> tokenStore,
        CaptionMutationLlmService.MutationCompletion completion,
        Mock<IUiStateStore>? uiStore = null) =>
        new(tokenStore.Object,
            (uiStore ?? new Mock<IUiStateStore>()).Object,
            NullLogger<CaptionMutationLlmService>.Instance,
            completion,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Mock<IAnthropicTokenStore> KeyStore(string? key)
    {
        var mock = new Mock<IAnthropicTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

    private static Task<Stream> FakeBundledAsset(string assetName) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("BUNDLED MUTATION PROMPT")));

    private static string ShippedPromptPath([CallerFilePath] string? thisFile = null)
    {
        var servicesDir = Path.GetDirectoryName(thisFile)!;                 // ...\Tests\Infrastructure\Services
        var repoRoot = Path.GetFullPath(Path.Combine(servicesDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "ImageGenerator.MAUI", "Resources", "Raw", "Mutation", "mutation-system.md");
    }
}
