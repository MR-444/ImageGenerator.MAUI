using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class AnthropicPromptBuilderServiceTests : IDisposable
{
    // A schema-valid V4 prompt the fake "Claude" can return.
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

    // ---- No key -------------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_NoApiKey_FailsWithoutCallingTheModel()
    {
        var called = false;
        var sut = CreateSut(KeyStore(null), (_, _, _, _) => { called = true; return Task.FromResult(ValidJson); });

        var result = await sut.BuildAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("API key");
        called.Should().BeFalse("the model must not be called without a key");
    }

    [Fact]
    public async Task BuildAsync_EmptyIdea_Fails()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _) => Task.FromResult(ValidJson));

        var result = await sut.BuildAsync("   ");

        result.Success.Should().BeFalse();
    }

    // ---- Happy path ---------------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_ValidResponse_ReturnsParsedPrompt()
    {
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _) => Task.FromResult(ValidJson));

        var result = await sut.BuildAsync("a red fox in snow");

        result.Success.Should().BeTrue();
        result.Prompt.Should().NotBeNull();
        result.Prompt!.HighLevelDescription.Should().Be("A red fox stepping through fresh snow");
        result.Error.Should().BeNull();
    }

    // ---- Validate + retry ---------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_InvalidThenValid_RetriesWithFeedbackAndSucceeds()
    {
        var seenMessages = new List<IReadOnlyList<AnthropicPromptBuilderService.ChatTurn>>();
        var call = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, messages, _) =>
        {
            seenMessages.Add(messages);
            return Task.FromResult(call++ == 0 ? ValidationFailJson : ValidJson);
        });

        var result = await sut.BuildAsync("a red fox");

        result.Success.Should().BeTrue();
        seenMessages.Should().HaveCount(2, "the first attempt failed validation, so it retries once");
        // The retry turn fed the bad attempt back plus the validator's complaint.
        seenMessages[1].Should().Contain(t => t.Role == "assistant");
        seenMessages[1].Should().Contain(t => t.Role == "user" && t.Content.Contains("problems"));
    }

    [Fact]
    public async Task BuildAsync_ValidationFailsTwice_FailsWithTheErrors()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _) => { calls++; return Task.FromResult(ValidationFailJson); });

        var result = await sut.BuildAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Background");
        calls.Should().Be(2, "initial attempt + one retry, then it gives up");
    }

    [Fact]
    public async Task BuildAsync_UnparseableTwice_Fails()
    {
        var calls = 0;
        var sut = CreateSut(KeyStore("sk-ant"), (_, _, _, _) => { calls++; return Task.FromResult("this is not json {"); });

        var result = await sut.BuildAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("structured prompt");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task BuildAsync_TransportThrows_FailsGracefully()
    {
        var sut = CreateSut(KeyStore("sk-ant"),
            (_, _, _, _) => throw new HttpRequestException("network down"));

        var result = await sut.BuildAsync("a red fox");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("couldn't reach Claude");
    }

    // ---- System-prompt override precedence ----------------------------------------------

    [Fact]
    public async Task BuildAsync_PrivateOverrideFilePresent_UsesItInsteadOfBundled()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "system-prompt.md"), "PRIVATE OVERRIDE PROMPT");

        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildAsync("a red fox");

        captured.Should().Be("PRIVATE OVERRIDE PROMPT");
    }

    [Fact]
    public async Task BuildAsync_NoOverrideFile_UsesBundledAsset()
    {
        string? captured = null;
        var sut = CreateSut(KeyStore("sk-ant"), (_, system, _, _) => { captured = system; return Task.FromResult(ValidJson); });

        await sut.BuildAsync("a red fox");

        captured.Should().Be("BUNDLED SYSTEM PROMPT");
    }

    // ---- Shipped clean-room prompt guard ------------------------------------------------

    [Fact]
    public void ShippedSystemPrompt_ExistsAndIsNonEmpty()
    {
        var path = ShippedPromptPath();
        File.Exists(path).Should().BeTrue($"the bundled clean-room system prompt should ship at {path}");
        File.ReadAllText(path).Trim().Should().NotBeEmpty();
    }

    // ---- Helpers / fakes ----------------------------------------------------------------

    private AnthropicPromptBuilderService CreateSut(
        Mock<IAnthropicTokenStore> store,
        AnthropicPromptBuilderService.StructuredCompletion completion) =>
        new(store.Object,
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

    private static Task<Stream> FakeBundledAsset(string assetName) =>
        Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("BUNDLED SYSTEM PROMPT")));

    // Locate the real shipped prompt relative to THIS source file (compile-time path), robust to bin
    // depth and CI checkout location — mirrors MutationLibraryServiceTests.SeedFolder.
    private static string ShippedPromptPath([CallerFilePath] string? thisFile = null)
    {
        var servicesDir = Path.GetDirectoryName(thisFile)!;                 // ...\Tests\Infrastructure\Services
        var repoRoot = Path.GetFullPath(Path.Combine(servicesDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "ImageGenerator.MAUI", "Resources", "Raw", "PromptBuilder", "v4-builder-system.md");
    }
}
