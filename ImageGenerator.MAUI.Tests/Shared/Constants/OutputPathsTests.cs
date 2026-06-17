using FluentAssertions;
using ImageGenerator.MAUI.Shared.Constants;

namespace ImageGenerator.MAUI.Tests.Shared.Constants;

[Collection("OutputPathsState")]
public class OutputPathsTests : IDisposable
{
    // Always restore the default after each test — the override is process-global static state.
    public void Dispose() => OutputPaths.SetRootOverride(null);

    [Fact]
    public void GeneratedImagesDirectory_NoOverride_IsThePicturesSubfolderOfTheDefaultRoot()
    {
        OutputPaths.GeneratedImagesDirectory.Should().Be(
            Path.Combine(OutputPaths.DefaultRootDirectory, "pictures"));
    }

    [Fact]
    public void SetOverride_MovesEveryDataFolderUnderTheConfiguredRoot()
    {
        OutputPaths.SetRootOverride(@"D:\out");

        // Images get their own "pictures" subfolder under the root.
        OutputPaths.GeneratedImagesDirectory.Should().Be(Path.Combine(@"D:\out", "pictures"));
        OutputPaths.JsonPromptsDirectory.Should().Be(Path.Combine(@"D:\out", "json-prompts"));
        // comfy-workflows / mutation-library / prompt-builder now follow the root too, so the
        // whole app stays together when the output folder is re-pointed.
        OutputPaths.ComfyWorkflowsDirectory.Should().Be(Path.Combine(@"D:\out", "comfy-workflows"));
        OutputPaths.MutationLibraryDirectory.Should().Be(Path.Combine(@"D:\out", "mutation-library"));
        OutputPaths.PromptBuilderDirectory.Should().Be(Path.Combine(@"D:\out", "prompt-builder"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetOverride_NullOrWhitespace_RestoresDefault(string? value)
    {
        OutputPaths.SetRootOverride(@"D:\out");
        OutputPaths.SetRootOverride(value);

        OutputPaths.RootDirectory.Should().Be(OutputPaths.DefaultRootDirectory);
    }

    [Fact]
    public void SetOverride_TrimsSurroundingWhitespace()
    {
        OutputPaths.SetRootOverride(@"  D:\out  ");

        OutputPaths.RootDirectory.Should().Be(@"D:\out");
    }
}
