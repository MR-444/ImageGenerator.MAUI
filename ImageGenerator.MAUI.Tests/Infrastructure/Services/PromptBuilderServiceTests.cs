using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class PromptBuilderServiceTests : IDisposable
{
    // A plausible prose prompt the fake "Claude" can return for the VPE pass.
    private const string ProseReply = "A russet-red fox mid-step through fresh snow at dawn, breath fogging the cold air.";

    // A schema-valid V4 prompt the fake "Claude" can return for the JSON pass.
    private const string ValidJson =
        """{"high_level_description":"A red fox stepping through fresh snow","compositional_deconstruction":{"background":"a quiet snowy field at dawn","elements":[{"type":"obj","desc":"a russet-red fox mid-step, breath fogging"}]}}""";

    // Parses fine but fails V4JsonPromptValidator (background is blank).
    private const string ValidationFailJson =
        """{"high_level_description":"x","compositional_deconstruction":{"background":"","elements":[{"type":"obj","desc":"y"}]}}""";

    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "prompt-builder-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    // ---- No key / empty input -----------------------------------------------------------

    [Fact]
    public async Task BuildProseAsync_NoApiKey_FailsWithoutCallingTheModel()
    {
        var called = false;
        var sut = CreateSut(KeyStore(null), (_, _, _, _, _, _) => { called = true; return Task.FromResult(ProseReply); });

        var result = await sut.BuildProseAsync("a red fox", Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse("the model must not be called without a key");
    }

    [Fact]
    public async Task BuildProseAsync_EmptyIdea_Fails()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _, _) => Task.FromResult(ProseReply));

        var result = await sut.BuildProseAsync("   ", Sonnet);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildJsonAsync_EmptyProse_Fails()
    {
        var called = false;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.BuildJsonAsync("   ", Sonnet);

        result.Success.Should().BeFalse();
        called.Should().BeFalse("an empty prose input must not reach the model");
    }

    [Fact]
    public async Task BuildProseAsync_LocalTarget_UsesTheHandedModelAndEndpoint_AndNeedsNoKey()
    {
        var keyStore = KeyStore(null);
        LlmTarget seenTarget = default;
        string? seenApiKey = "unexpected";

        var sut = CreateSut(keyStore,
            (target, apiKey, _, _, _, _) =>
            {
                seenTarget = target;
                seenApiKey = apiKey;
                return Task.FromResult(ProseReply);
            });

        var result = await sut.BuildProseAsync("a red fox", Local("qwen3", "http://host:11434"));

        result.Success.Should().BeTrue("a local call needs no provider key");
        seenTarget.Provider.Should().Be(ModelProvider.Local);
        seenTarget.ModelId.Should().Be("qwen3");
        seenTarget.BaseUrl.Should().Be("http://host:11434");
        seenApiKey.Should().BeNull();
        keyStore.Verify(s => s.LoadAsync(), Times.Never, "no Anthropic key is even read for a local call");
    }

    [Fact]
    public async Task BuildProseAsync_NoModelSelected_RefusesAndNamesOllama()
    {
        var sut = CreateSut(KeyStore(null), NeverCalled);

        var result = await sut.BuildProseAsync("a red fox", Local(model: ""));

        result.Error.Should().Contain("Ollama model selected");
    }

    [Fact]
    public async Task BuildProseAsync_OpenRouterTarget_WithoutAnOpenRouterKey_NamesOpenRouter()
    {
        var sut = CreateSut(KeyStore("sk-ant"), NeverCalled);

        var result = await sut.BuildProseAsync(
            "a red fox", new LlmTarget(ModelProvider.OpenRouter, "some-model", string.Empty));

        result.Error.Should().Contain("OpenRouter API key");
    }

    // ---- Pass 1: VPE prose --------------------------------------------------------------

    [Fact]
    public async Task BuildProseAsync_ValidResponse_ReturnsProseFromASchemaLessCall()
    {
        bool? schemaProvided = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, schema, _) =>
        {
            schemaProvided = schema.HasValue;
            return Task.FromResult("  " + ProseReply + "  ");   // wrapped in whitespace to prove trimming
        });

        var result = await sut.BuildProseAsync("a red fox in snow", Sonnet);

        result.Success.Should().BeTrue();
        result.Prose.Should().Be(ProseReply);
        result.Error.Should().BeNull();
        schemaProvided.Should().BeFalse("the VPE prose pass must be a plain text call — no json_schema format");
    }

    [Fact]
    public async Task BuildProseAsync_EmptyModelText_Fails()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _, _) => Task.FromResult("   "));

        var result = await sut.BuildProseAsync("a red fox", Sonnet);

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildProseAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.BuildProseAsync("a red fox", Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach Claude");
    }

    // ---- Pass 2: JSON happy path + structured call --------------------------------------

    [Fact]
    public async Task BuildJsonAsync_ValidResponse_ReturnsParsedPromptFromAStructuredCall()
    {
        bool? schemaProvided = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, schema, _) =>
        {
            schemaProvided = schema.HasValue;
            return Task.FromResult(ValidJson);
        });

        var result = await sut.BuildJsonAsync(ProseReply, Sonnet);

        result.Success.Should().BeTrue();
        result.Prompt.Should().NotBeNull();
        result.Prompt!.HighLevelDescription.Should().Be("A red fox stepping through fresh snow");
        result.Error.Should().BeNull();
        schemaProvided.Should().BeTrue("the JSON pass uses structured outputs (json_schema)");
    }

    // ---- Pass 2: validate + retry -------------------------------------------------------

    [Fact]
    public async Task BuildJsonAsync_InvalidThenValid_RetriesWithFeedbackAndSucceeds()
    {
        var seenMessages = new List<IReadOnlyList<ChatTurn>>();
        var call = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, messages, _, _) =>
        {
            seenMessages.Add(messages);
            return Task.FromResult(call++ == 0 ? ValidationFailJson : ValidJson);
        });

        var result = await sut.BuildJsonAsync(ProseReply, Sonnet);

        result.Success.Should().BeTrue();
        seenMessages.Should().HaveCount(2, "the first attempt failed validation, so it retries once");
        // The retry turn fed the bad attempt back plus the validator's complaint.
        seenMessages[1].Should().Contain(t => t.Role == "assistant");
        seenMessages[1].Should().Contain(t => t.Role == "user" && t.Content.Contains("problems"));
    }

    [Fact]
    public async Task BuildJsonAsync_ValidationFailsTwice_FailsWithTheErrors()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.BuildJsonAsync(ProseReply, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Background");
        calls.Should().Be(2, "initial attempt + one retry, then it gives up");
    }

    [Fact]
    public async Task BuildJsonAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _, _) => { calls++; return Task.FromResult("this is not json {"); });

        var result = await sut.BuildJsonAsync(ProseReply, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task BuildJsonAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.BuildJsonAsync(ProseReply, Sonnet);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach Claude");
    }

    // ---- System-prompt override precedence (each pass has its own file) ------------------

    [Fact]
    public async Task BuildProseAsync_VpeOverridePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "vpe-prompt.md"), "PRIVATE VPE OVERRIDE");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ProseReply); });

        await sut.BuildProseAsync("a red fox", Sonnet);

        captured.Should().Be("PRIVATE VPE OVERRIDE");
    }

    [Fact]
    public async Task BuildProseAsync_NoOverrideFile_UsesBundledVpeAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ProseReply); });

        await sut.BuildProseAsync("a red fox", Sonnet);

        captured.Should().Be("BUNDLED VPE PROMPT");
    }

    [Fact]
    public async Task BuildJsonAsync_PrivateOverrideFilePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "system-prompt.md"), "PRIVATE OVERRIDE PROMPT");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply, Sonnet);

        captured.Should().Be("PRIVATE OVERRIDE PROMPT");
    }

    [Fact]
    public async Task BuildJsonAsync_NoOverrideFile_UsesBundledJsonAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply, Sonnet);

        captured.Should().Be("BUNDLED JSON PROMPT");
    }

    [Fact]
    public async Task BuildAsync_SeedsOverrideReadme_WithoutTouchingExistingPrompt()
    {
        Directory.CreateDirectory(_tempDir);
        var promptPath = Path.Combine(_tempDir, "system-prompt.md");
        await File.WriteAllTextAsync(promptPath, "PRIVATE OVERRIDE PROMPT");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply, Sonnet);

        // README.txt seeded for discoverability...
        File.Exists(Path.Combine(_tempDir, "README.txt")).Should().BeTrue();
        // ...without disturbing the user's override, which is still the one used.
        (await File.ReadAllTextAsync(promptPath)).Should().Be("PRIVATE OVERRIDE PROMPT");
        captured.Should().Be("PRIVATE OVERRIDE PROMPT");
    }

    // ---- Shipped clean-room prompt guards -----------------------------------------------

    [Fact]
    public void ShippedJsonSystemPrompt_ExistsAndIsNonEmpty()
    {
        var path = ShippedPromptPath("v4-builder-system.md");
        File.Exists(path).Should().BeTrue($"the bundled clean-room JSON prompt should ship at {path}");
        File.ReadAllText(path).Trim().Should().NotBeEmpty();
    }

    [Fact]
    public void ShippedVpeSystemPrompt_ExistsAndIsNonEmpty()
    {
        var path = ShippedPromptPath("vpe-system.md");
        File.Exists(path).Should().BeTrue($"the bundled clean-room VPE prompt should ship at {path}");
        File.ReadAllText(path).Trim().Should().NotBeEmpty();
    }

    // ---- Helpers / fakes ----------------------------------------------------------------

    private PromptBuilderService CreateSut(
        Mock<IAnthropicTokenStore> store,
        PromptBuilderService.StructuredCompletion completion,
        string? openRouterKey = null) =>
        new(store.Object,
            OpenRouterKeyStore(openRouterKey).Object,
            NullLogger<PromptBuilderService>.Instance,
            completion,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Mock<IAnthropicTokenStore> KeyStore(string? key)
    {
        var mock = new Mock<IAnthropicTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

    /// <summary>A completion that fails the test if the service reaches the model at all.</summary>
    private static Task<string> NeverCalled(
        LlmTarget target, string? apiKey, string systemPrompt,
        IReadOnlyList<ChatTurn> messages, JsonElement? schema, CancellationToken ct) =>
        throw new InvalidOperationException("the service must refuse before calling a model");

    /// <summary>The target the page hands this service for a Claude pick.</summary>
    private static LlmTarget Sonnet => new(ModelProvider.Anthropic, AnthropicModels.Sonnet, string.Empty);

    private static LlmTarget Local(string model, string baseUrl = "http://127.0.0.1:11434") =>
        new(ModelProvider.Local, model, baseUrl);

    private static Mock<IOpenRouterTokenStore> OpenRouterKeyStore(string? key)
    {
        var mock = new Mock<IOpenRouterTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

    // Distinguish the two bundled assets so tests can assert which pass loaded which prompt.
    private static Task<Stream> FakeBundledAsset(string assetName)
    {
        var body = assetName.Contains("vpe", StringComparison.OrdinalIgnoreCase)
            ? "BUNDLED VPE PROMPT"
            : "BUNDLED JSON PROMPT";
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
    }

    // Locate the real shipped prompt relative to THIS source file (compile-time path), robust to bin
    // depth and CI checkout location — mirrors MutationLibraryServiceTests.SeedFolder.
    private static string ShippedPromptPath(string fileName, [CallerFilePath] string? thisFile = null)
    {
        var servicesDir = Path.GetDirectoryName(thisFile)!;                 // ...\Tests\Infrastructure\Services
        var repoRoot = Path.GetFullPath(Path.Combine(servicesDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "ImageGenerator.MAUI", "Resources", "Raw", "PromptBuilder", fileName);
    }
}
