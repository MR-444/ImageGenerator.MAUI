using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.External.ComfyUi;
using ImageGenerator.MAUI.Shared.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Services.ComfyUi;

public sealed class ComfyUiWorkflowCatalogServiceTests : IDisposable
{
    private readonly string _directory;
    private readonly ComfyUiWorkflowCatalogService _sut;

    public ComfyUiWorkflowCatalogServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "imggen-comfy-catalog-" + Guid.NewGuid().ToString("N"));
        _sut = new ComfyUiWorkflowCatalogService(
            NullLogger<ComfyUiWorkflowCatalogService>.Instance,
            directoryOverride: _directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task FetchAsync_MissingDirectory_CreatesItAndReturnsEmpty()
    {
        var options = await _sut.FetchAsync();

        options.Should().BeEmpty();
        Directory.Exists(_directory).Should().BeTrue("the user needs a folder to drop exports into");
    }

    [Fact]
    public async Task FetchAsync_MapsJsonStemsToModelOptions_OrderedAndIgnoringOtherFiles()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "zz_last.json"), "{}");
        File.WriteAllText(Path.Combine(_directory, "Ideogram workflow_MR.json"), "{}");
        File.WriteAllText(Path.Combine(_directory, "readme.txt"), "not a workflow");

        var options = await _sut.FetchAsync();

        options.Should().HaveCount(2);
        options[0].Display.Should().Be("Ideogram workflow_MR (ComfyUI)");
        options[0].Value.Should().Be("comfyui/Ideogram workflow_MR");
        options[0].Provider.Should().Be(ProviderConstants.ComfyUi);
        options[1].Value.Should().Be("comfyui/zz_last");
    }
}
