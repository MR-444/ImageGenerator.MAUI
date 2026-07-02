using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class AnthropicPromptBuilderServiceTests : IDisposable
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

    private delegate Task<string> LegacyCompletion(
        string apiKey, string systemPrompt, IReadOnlyList<ChatTurn> messages, JsonElement? schema, CancellationToken ct);

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
        var sut = CreateSut(KeyStore(null), (_, _, _, _, _) => { called = true; return Task.FromResult(ProseReply); });

        var result = await sut.BuildProseAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse("the model must not be called without a key");
    }

    [Fact]
    public async Task BuildProseAsync_EmptyIdea_Fails()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _) => Task.FromResult(ProseReply));

        var result = await sut.BuildProseAsync("   ");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildJsonAsync_EmptyProse_Fails()
    {
        var called = false;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.BuildJsonAsync("   ");

        result.Success.Should().BeFalse();
        called.Should().BeFalse("an empty prose input must not reach the model");
    }

    [Fact]
    public async Task BuildProseAsync_LocalTier_UsesOllamaModelAndBaseUrl_AndNeedsNoKey()
    {
        var keyStore = KeyStore(null);
        var uiStore = UiStore("http://host:11434", "qwen3");
        ModelTier? seenTier = null;
        string? seenModel = null;
        string? seenBaseUrl = null;
        string? seenApiKey = "unexpected";

        var sut = CreateRoutedSut(keyStore, uiStore,
            (tier, modelId, apiKey, baseUrl, _, _, schema, _) =>
            {
                seenTier = tier;
                seenModel = modelId;
                seenBaseUrl = baseUrl;
                seenApiKey = apiKey;
                schema.HasValue.Should().BeFalse("the prose pass is schema-less even on Ollama");
                return Task.FromResult(ProseReply);
            });

        var result = await sut.BuildProseAsync("a red fox", tier: ModelTier.Local);

        result.Success.Should().BeTrue("the Local tier needs no Anthropic key");
        seenTier.Should().Be(ModelTier.Local);
        seenModel.Should().Be("qwen3");
        seenBaseUrl.Should().Be("http://host:11434");
        seenApiKey.Should().BeNull();
        keyStore.Verify(s => s.LoadAsync(), Times.Never);
    }

    // ---- Pass 1: VPE prose --------------------------------------------------------------

    [Fact]
    public async Task BuildProseAsync_ValidResponse_ReturnsProseFromASchemaLessCall()
    {
        bool? schemaProvided = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, schema, _) =>
        {
            schemaProvided = schema.HasValue;
            return Task.FromResult("  " + ProseReply + "  ");   // wrapped in whitespace to prove trimming
        });

        var result = await sut.BuildProseAsync("a red fox in snow");

        result.Success.Should().BeTrue();
        result.Prose.Should().Be(ProseReply);
        result.Error.Should().BeNull();
        schemaProvided.Should().BeFalse("the VPE prose pass must be a plain text call — no json_schema format");
    }

    [Fact]
    public async Task BuildProseAsync_EmptyModelText_Fails()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _) => Task.FromResult("   "));

        var result = await sut.BuildProseAsync("a red fox");

        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildProseAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.BuildProseAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach Claude");
    }

    // ---- Pass 2: JSON happy path + structured call --------------------------------------

    [Fact]
    public async Task BuildJsonAsync_ValidResponse_ReturnsParsedPromptFromAStructuredCall()
    {
        bool? schemaProvided = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, schema, _) =>
        {
            schemaProvided = schema.HasValue;
            return Task.FromResult(ValidJson);
        });

        var result = await sut.BuildJsonAsync(ProseReply);

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
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, messages, _, _) =>
        {
            seenMessages.Add(messages);
            return Task.FromResult(call++ == 0 ? ValidationFailJson : ValidJson);
        });

        var result = await sut.BuildJsonAsync(ProseReply);

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
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.BuildJsonAsync(ProseReply);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Background");
        calls.Should().Be(2, "initial attempt + one retry, then it gives up");
    }

    [Fact]
    public async Task BuildJsonAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _, _) => { calls++; return Task.FromResult("this is not json {"); });

        var result = await sut.BuildJsonAsync(ProseReply);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task BuildJsonAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.BuildJsonAsync(ProseReply);

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
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _, _) => { captured = system; return Task.FromResult(ProseReply); });

        await sut.BuildProseAsync("a red fox");

        captured.Should().Be("PRIVATE VPE OVERRIDE");
    }

    [Fact]
    public async Task BuildProseAsync_NoOverrideFile_UsesBundledVpeAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _, _) => { captured = system; return Task.FromResult(ProseReply); });

        await sut.BuildProseAsync("a red fox");

        captured.Should().Be("BUNDLED VPE PROMPT");
    }

    [Fact]
    public async Task BuildJsonAsync_PrivateOverrideFilePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "system-prompt.md"), "PRIVATE OVERRIDE PROMPT");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply);

        captured.Should().Be("PRIVATE OVERRIDE PROMPT");
    }

    [Fact]
    public async Task BuildJsonAsync_NoOverrideFile_UsesBundledJsonAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply);

        captured.Should().Be("BUNDLED JSON PROMPT");
    }

    [Fact]
    public async Task BuildAsync_SeedsOverrideReadme_WithoutTouchingExistingPrompt()
    {
        Directory.CreateDirectory(_tempDir);
        var promptPath = Path.Combine(_tempDir, "system-prompt.md");
        await File.WriteAllTextAsync(promptPath, "PRIVATE OVERRIDE PROMPT");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildJsonAsync(ProseReply);

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

    private AnthropicPromptBuilderService CreateSut(
        Mock<IAnthropicTokenStore> store,
        LegacyCompletion completion) =>
        CreateRoutedSut(store, UiStore(),
            (tier, modelId, apiKey, baseUrl, systemPrompt, messages, schema, ct) =>
                completion(apiKey ?? string.Empty, systemPrompt, messages, schema, ct));

    private AnthropicPromptBuilderService CreateRoutedSut(
        Mock<IAnthropicTokenStore> store,
        Mock<IUiStateStore> uiStore,
        AnthropicPromptBuilderService.StructuredCompletion completion) =>
        new(store.Object,
            uiStore.Object,
            NullLogger<AnthropicPromptBuilderService>.Instance,
            completion,
            promptDirectoryOverride: _tempDir,
            assetOpener: FakeBundledAsset);

    private static Mock<IAnthropicTokenStore> KeyStore(string? key)
    {
        var mock = new Mock<IAnthropicTokenStore>();
        mock.Setup(s => s.LoadAsync()).ReturnsAsync(key);
        return mock;
    }

    private static Mock<IUiStateStore> UiStore(string? ollamaUrl = null, string? ollamaModel = null)
    {
        var mock = new Mock<IUiStateStore>();
        mock.Setup(s => s.LoadOllamaBaseUrl()).Returns(ollamaUrl);
        mock.Setup(s => s.LoadOllamaModel()).Returns(ollamaModel);
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
