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
/// replaces the network so the request build, provider routing, validate-retry loop, and override
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
            (_, _, _, _, schema, _) => { schemaKind = schema.ValueKind; return Task.FromResult(ValidJson); });

        var result = await sut.MutateAsync(Base, "make it winter", 0, Sonnet);

        result.Success.Should().BeTrue();
        result.Prompt.Should().NotBeNull();
        result.Label.Should().NotBeNullOrEmpty();
        schemaKind.Should().Be(JsonValueKind.Object, "every mutation call is constrained to the V4 schema");
    }

    // ---- What the service is told to call ------------------------------------------------
    //
    // Provider and model are the page's decision; the service's contract is "call exactly this", so a
    // model picked from a catalog is never replaced by one the service derives itself.

    [Theory]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-opus-4-8")]
    [InlineData("a-model-invented-tomorrow")]
    public async Task MutateAsync_AnthropicTarget_CallsTheModelItWasHanded(string modelId)
    {
        string? seenModel = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (target, _, _, _, _, _) => { seenModel = target.ModelId; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, Claude(modelId));

        seenModel.Should().Be(modelId);
    }

    [Fact]
    public async Task MutateAsync_LocalTarget_UsesTheHandedModelAndEndpoint_AndNeedsNoKey()
    {
        LlmTarget seenTarget = default;
        string? seenKey = "unset";
        var sut = CreateSut(KeyStore(null), // no Anthropic key at all
            (target, apiKey, _, _, _, _) =>
            {
                seenTarget = target;
                seenKey = apiKey;
                return Task.FromResult(ValidJson);
            });

        var result = await sut.MutateAsync(Base, "x", 0, Local("llama3.1", "http://host:11434"));

        result.Success.Should().BeTrue("a local call needs no provider key");
        seenTarget.Provider.Should().Be(ModelProvider.Local);
        seenTarget.ModelId.Should().Be("llama3.1");
        seenTarget.BaseUrl.Should().Be("http://host:11434");
        seenKey.Should().BeNull("there is nothing to authenticate to on the LAN");
    }

    [Fact]
    public async Task MutateAsync_NoModelSelected_RefusesAndNamesOllama()
    {
        var sut = CreateSut(KeyStore(null), NeverCalled);

        var result = await sut.MutateAsync(Base, "x", 0, Local(model: ""));

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Ollama model selected",
            "an empty pick is a UI state, and the sentence has to say where to fix it");
    }

    [Fact]
    public async Task MutateAsync_OpenRouterTarget_WithoutAnOpenRouterKey_NamesOpenRouter()
    {
        var sut = CreateSut(KeyStore("sk-ant"), NeverCalled);

        var result = await sut.MutateAsync(
            Base, "x", 0, new LlmTarget(ModelProvider.OpenRouter, "some-model", string.Empty));

        result.Error.Should().Contain("OpenRouter API key",
            "the mutator used to read only the Anthropic store; an Anthropic key is not an OpenRouter key");
    }

    [Fact]
    public async Task MutateAsync_AnthropicTier_NoApiKey_FailsWithoutCallingTheModel()
    {
        var called = false;
        var sut = CreateSut(KeyStore(null),
            (_, _, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.MutateAsync(Base, "x", 0, Sonnet);

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
            (_, _, _, messages, _, _) => { seen.Add(messages); return Task.FromResult(call++ == 0 ? ValidationFailJson : ValidJson); });

        var result = await sut.MutateAsync(Base, "x", 0, Sonnet);

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
            (_, _, _, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.MutateAsync(Base, "x", 0, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Background");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task MutateAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _) => { calls++; return Task.FromResult("this is not json {"); });

        var result = await sut.MutateAsync(Base, "x", 0, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task MutateAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.MutateAsync(Base, "x", 0, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach the model");
    }

    // ---- Diversity by index --------------------------------------------------------------

    [Fact]
    public async Task MutateAsync_VariesTheUserTurnByIndex()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, messages, _, _) => { captured = messages[0].Content; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, Sonnet);
        captured.Should().Contain("variation #1");

        await sut.MutateAsync(Base, "x", 4, Sonnet);
        captured.Should().Contain("variation #5");
    }

    [Fact]
    public async Task MutateAsync_NoSteer_SeedsADistinctCreativeLensPerIndex()
    {
        var captured = new List<string>();
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, messages, _, _) => { captured.Add(messages[0].Content); return Task.FromResult(ValidJson); });

        // No steer ⇒ a blind call can't "be distinct from the others", so each index gets a concrete lens.
        await sut.MutateAsync(Base, "", 0, Sonnet);
        await sut.MutateAsync(Base, "", 1, Sonnet);

        captured[0].Should().Contain("TIME OF DAY");
        captured[1].Should().Contain("SEASON");
        captured[0].Should().NotBe(captured[1]);
    }

    [Fact]
    public async Task MutateAsync_WithSteer_DefersTheLens_SoItCannotContradict()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, messages, _, _) => { captured = messages[0].Content; return Task.FromResult(ValidJson); });

        // A steer dominates; the index-0 lens ("different TIME OF DAY") must NOT appear to fight "3am".
        await sut.MutateAsync(Base, "3am, high drama", 0, Sonnet);

        captured.Should().Contain("3am, high drama");
        captured.Should().NotContain("TIME OF DAY");
        captured.Should().Contain("without ever contradicting");
    }

    // ---- Breeding ------------------------------------------------------------------------

    [Fact]
    public async Task BreedAsync_NoWinners_FailsWithoutCalling()
    {
        var called = false;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.BreedAsync([], "x", 0, Sonnet);

        result.Success.Should().BeFalse();
        called.Should().BeFalse();
    }

    [Fact]
    public async Task BreedAsync_WithParents_SendsParentsAndOffspringIndex()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, messages, _, _) => { captured = messages[0].Content; return Task.FromResult(ValidJson); });

        var result = await sut.BreedAsync([Base, Base], "blend them", 0, Sonnet);

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
            (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, Sonnet);

        captured.Should().Be("PRIVATE MUTATION OVERRIDE");
    }

    [Fact]
    public async Task MutateAsync_NoOverride_UsesBundledAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.MutateAsync(Base, "x", 0, Sonnet);

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
        string? openRouterKey = null) =>
        new(tokenStore.Object,
            OpenRouterKeyStore(openRouterKey).Object,
            NullLogger<CaptionMutationLlmService>.Instance,
            completion,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    /// <summary>A completion that fails the test if the service reaches the model at all.</summary>
    private static Task<string> NeverCalled(
        LlmTarget target, string? apiKey, string systemPrompt,
        IReadOnlyList<ChatTurn> messages, JsonElement schema, CancellationToken ct) =>
        throw new InvalidOperationException("the service must refuse before calling a model");

    /// <summary>The target the mutation page hands the service for a Claude pick.</summary>
    private static LlmTarget Sonnet => new(ModelProvider.Anthropic, AnthropicModels.Sonnet, string.Empty);

    private static LlmTarget Claude(string modelId) =>
        new(ModelProvider.Anthropic, modelId, string.Empty);

    private static LlmTarget Local(string model, string baseUrl = "http://127.0.0.1:11434") =>
        new(ModelProvider.Local, model, baseUrl);

    private static Mock<IOpenRouterTokenStore> OpenRouterKeyStore(string? key)
    {
        var mock = new Mock<IOpenRouterTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

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
