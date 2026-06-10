using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Services;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class JsonPromptFileServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "json-prompt-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime FrozenNow = new(2026, 6, 10, 14, 30, 0);

    private JsonPromptFileService CreateSut() => new(_tempDir, () => FrozenNow);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (DirectoryNotFoundException) { }
    }

    [Fact]
    public async Task SaveAsync_WritesContent_AndReturnsPathInsideDirectory()
    {
        const string json = "{\n  \"high_level_description\": \"x\"\n}";

        var path = await CreateSut().SaveAsync("A poster", json);

        path.Should().StartWith(_tempDir);
        (await File.ReadAllTextAsync(path)).Should().Be(json);
    }

    [Fact]
    public async Task SaveAsync_FileName_UsesTimestampAndSafeName()
    {
        var path = await CreateSut().SaveAsync("A poster", "{}");

        Path.GetFileName(path).Should().Be("20260610_143000_A_poster.json");
    }

    [Fact]
    public async Task SaveAsync_Collision_AppendsUniqueSuffix()
    {
        var sut = CreateSut();

        var first = await sut.SaveAsync("A poster", "{}");
        var second = await sut.SaveAsync("A poster", "{}");

        Path.GetFileName(first).Should().Be("20260610_143000_A_poster.json");
        Path.GetFileName(second).Should().Be("20260610_143000_A_poster_1.json");
    }

    [Fact]
    public async Task SaveAsync_SanitizesInvalidChars_AndTruncatesLongNames()
    {
        var path = await CreateSut().SaveAsync("an: extremely<long>description that just keeps going on", "{}");

        var stem = Path.GetFileNameWithoutExtension(path)["20260610_143000_".Length..];
        stem.Should().HaveLength(30);
        stem.Should().NotContainAny(":", "<", ">", " ");
    }

    [Fact]
    public async Task SaveAsync_BlankDescription_FallsBackToDefaultName()
    {
        var path = await CreateSut().SaveAsync("   ", "{}");

        Path.GetFileName(path).Should().Be("20260610_143000_structured-prompt.json");
    }

    [Fact]
    public async Task SaveAsync_CreatesDirectoryOnDemand()
    {
        Directory.Exists(_tempDir).Should().BeFalse();

        await CreateSut().SaveAsync("x", "{}");

        Directory.Exists(_tempDir).Should().BeTrue();
    }
}
