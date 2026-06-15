using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ImageGenerator.MAUI.Tests.ViewModels;

/// <summary>
/// The prompt-builder front door: the VM builds a caption via <see cref="IPromptBuilderService"/> and,
/// on success, drops it into the generator through the same Parameters handoff the structure editor
/// uses. These tests pin the gating, the success handoff, and the failure surface with a fake service.
/// </summary>
[Collection("OutputPathsState")]
public class IdeaToPromptViewModelTests
{
    [Fact]
    public void BuildCommand_DisabledUntilAnIdeaIsTyped()
    {
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(PromptBuilderResult.Ok(MutationTestData.BaseCaption())),
            NullLogger<IdeaToPromptViewModel>.Instance);

        vm.BuildCommand.CanExecute(null).Should().BeFalse("no idea entered yet");

        vm.Idea = "a red fox in snow";

        vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task Build_Success_AppliesPromptToGeneratorAndReportsSuccess()
    {
        var prompt = MutationTestData.BaseCaption();
        var generator = BuildGenerator();
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(PromptBuilderResult.Ok(prompt)),
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator)
        {
            Idea = "a red fox in snow"
        };

        await vm.BuildCommand.ExecuteAsync(null);

        generator.Parameters.UseJsonPrompt.Should().BeTrue();
        generator.Parameters.Prompt.Should().Be(V4JsonPromptSerializer.Serialize(prompt));
        vm.StatusKind.Should().Be(StatusKind.Success);
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Build_Failure_SurfacesErrorAndLeavesGeneratorUntouched()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(PromptBuilderResult.Fail("No Anthropic API key — add it on the Settings page.")),
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator)
        {
            Idea = "a red fox in snow"
        };

        await vm.BuildCommand.ExecuteAsync(null);

        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("API key");
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
        generator.Parameters.Prompt.Should().Be(originalPrompt);
    }

    // ---- Helpers / fakes ----------------------------------------------------------------

    private sealed class FakePromptBuilder(PromptBuilderResult result) : IPromptBuilderService
    {
        public Task<PromptBuilderResult> BuildAsync(string idea, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    // A GeneratorViewModel built from bare mocks — mirrors GeneratorViewModelTests; we only touch
    // Parameters, so no mock setups are needed.
    private static GeneratorViewModel BuildGenerator() =>
        new(new Mock<IJobRunner>().Object,
            new Mock<IApiTokenStore>().Object,
            new Mock<IPollinationsTokenStore>().Object,
            new Mock<IComfyUiAuthStore>().Object,
            new Mock<ICivitaiTokenStore>().Object,
            new Mock<IAnthropicTokenStore>().Object,
            new Mock<ICivitaiPostingService>().Object,
            new Mock<IUiStateStore>().Object,
            new Mock<IModelCatalogCoordinator>().Object,
            ModelDescriptorRegistry.Default(),
            new Mock<IPromptBatchParser>().Object,
            new Mock<IComfyUiCheckpointService>().Object,
            new Mock<IGalleryService>().Object,
            new Mock<IFolderPicker>().Object,
            NullLogger<GeneratorViewModel>.Instance);
}
