using FluentAssertions;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Shared.Constants;

[Collection("OutputPathsState")]
public class OutputPathsTests : IDisposable
{
    // Always restore the default after each test — the override is process-global static state.
    public void Dispose() => OutputPaths.SetGeneratedImagesOverride(null);

    [Fact]
    public void GeneratedImagesDirectory_NoOverride_IsTheDefaultPicturesPath()
    {
        OutputPaths.GeneratedImagesDirectory.Should().Be(OutputPaths.DefaultGeneratedImagesDirectory);
    }

    [Fact]
    public void SetOverride_MovesImagesAndJsonPrompts_ButNotComfyWorkflowsOrMutationLibrary()
    {
        OutputPaths.SetGeneratedImagesOverride(@"D:\out");

        OutputPaths.GeneratedImagesDirectory.Should().Be(@"D:\out");
        OutputPaths.JsonPromptsDirectory.Should().Be(Path.Combine(@"D:\out", "json-prompts"));
        // comfy-workflows are inputs (picker models) — anchored at the default, never moved.
        OutputPaths.ComfyWorkflowsDirectory.Should().Be(
            Path.Combine(OutputPaths.DefaultGeneratedImagesDirectory, "comfy-workflows"));
        // the mutation library is hand-edited input too — also anchored at the default.
        OutputPaths.MutationLibraryDirectory.Should().Be(
            Path.Combine(OutputPaths.DefaultGeneratedImagesDirectory, "mutation-library"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetOverride_NullOrWhitespace_RestoresDefault(string? value)
    {
        OutputPaths.SetGeneratedImagesOverride(@"D:\out");
        OutputPaths.SetGeneratedImagesOverride(value);

        OutputPaths.GeneratedImagesDirectory.Should().Be(OutputPaths.DefaultGeneratedImagesDirectory);
    }

    [Fact]
    public void SetOverride_TrimsSurroundingWhitespace()
    {
        OutputPaths.SetGeneratedImagesOverride(@"  D:\out  ");

        OutputPaths.GeneratedImagesDirectory.Should().Be(@"D:\out");
    }
}
