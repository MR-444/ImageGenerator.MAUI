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
/// The prompt-builder front door is a two-pass flow: pass 1 always builds a prose prompt; pass 2
/// (gated by the <see cref="IdeaToPromptViewModel.BuildJson"/> checkbox) builds the Ideogram V4 JSON.
/// The prose is never discarded — the user copies it, applies it as the prompt, or applies the JSON.
/// These tests pin the gating, both apply handoffs, prose preservation on JSON failure, and copy.
/// </summary>
[Collection("OutputPathsState")]
public class IdeaToPromptViewModelTests
{
    private const string Prose = "A russet-red fox mid-step through fresh snow at dawn, breath fogging.";

    private readonly Mock<IClipboardService> _clipboard = new();

    [Fact]
    public void BuildCommand_DisabledUntilAnIdeaIsTyped()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        vm.BuildCommand.CanExecute(null).Should().BeFalse("no idea entered yet");

        vm.Idea = "a red fox in snow";

        vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    // ---- Pass 1 only (checkbox off) -----------------------------------------------------

    [Fact]
    public async Task Build_JsonUnchecked_ShowsProseAndLeavesGeneratorUntouched()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()), generator);
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.Prose.Should().Be(Prose);
        vm.HasProse.Should().BeTrue();
        vm.HasJson.Should().BeFalse("the JSON pass was not requested");
        vm.StatusKind.Should().Be(StatusKind.Success);
        vm.IsBusy.Should().BeFalse();
        // No handoff happens on Build now — only when the user clicks "Use ...".
        generator.Parameters.Prompt.Should().Be(originalPrompt);
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
    }

    [Fact]
    public async Task UseProse_AppliesProseAndDisablesJsonMode()
    {
        var generator = BuildGenerator();
        generator.Parameters.UseJsonPrompt = true;   // prove the command flips it back off
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()), generator);
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";
        await vm.BuildCommand.ExecuteAsync(null);

        await vm.UseProseCommand.ExecuteAsync(null);

        generator.Parameters.Prompt.Should().Be(Prose);
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
    }

    // ---- Pass 2 (checkbox on) -----------------------------------------------------------

    [Fact]
    public async Task Build_JsonChecked_BuildsJson_AndUseJsonAppliesCompactJson()
    {
        var prompt = MutationTestData.BaseCaption();
        var generator = BuildGenerator();
        var vm = NewVm(ProseOk(), JsonOk(prompt), generator);
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.Prose.Should().Be(Prose, "the prose is kept even when JSON is also built");
        vm.HasJson.Should().BeTrue();
        vm.StatusKind.Should().Be(StatusKind.Success);

        await vm.UseJsonCommand.ExecuteAsync(null);

        generator.Parameters.UseJsonPrompt.Should().BeTrue();
        generator.Parameters.Prompt.Should().Be(V4JsonPromptSerializer.Serialize(prompt));
    }

    [Fact]
    public async Task Build_JsonChecked_JsonFails_KeepsProseVisibleAndSurfacesError()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseOk(), PromptBuilderResult.Fail("Claude's prompt didn't satisfy the schema"), generator);
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.Prose.Should().Be(Prose, "the prose stays usable even when the JSON pass fails");
        vm.HasProse.Should().BeTrue();
        vm.HasJson.Should().BeFalse();
        vm.UseJsonCommand.CanExecute(null).Should().BeFalse();
        vm.StatusKind.Should().Be(StatusKind.Error);
        generator.Parameters.Prompt.Should().Be(originalPrompt);
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
    }

    // ---- Failure + copy -----------------------------------------------------------------

    [Fact]
    public async Task Build_ProseFails_SurfacesErrorAndLeavesGeneratorUntouched()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseResult.Fail("No Anthropic API key — add it on the Settings page."),
            JsonOk(MutationTestData.BaseCaption()), generator);
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("API key");
        vm.HasProse.Should().BeFalse();
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
        generator.Parameters.Prompt.Should().Be(originalPrompt);
    }

    [Fact]
    public async Task CopyProse_PutsTheProseOnTheClipboard()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";
        await vm.BuildCommand.ExecuteAsync(null);

        await vm.CopyProseCommand.ExecuteAsync(null);

        _clipboard.Verify(c => c.SetTextAsync(Prose), Times.Once);
    }

    // ---- Helpers / fakes ----------------------------------------------------------------

    private static ProseResult ProseOk() => ProseResult.Ok(Prose);

    private static PromptBuilderResult JsonOk(V4JsonPrompt prompt) => PromptBuilderResult.Ok(prompt);

    private IdeaToPromptViewModel NewVm(ProseResult prose, PromptBuilderResult json, GeneratorViewModel? generator = null) =>
        new(new FakePromptBuilder(prose, json),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator);

    private sealed class FakePromptBuilder(ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public Task<ProseResult> BuildProseAsync(string idea, CancellationToken cancellationToken = default) =>
            Task.FromResult(prose);

        public Task<PromptBuilderResult> BuildJsonAsync(string prose, CancellationToken cancellationToken = default) =>
            Task.FromResult(json);
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
