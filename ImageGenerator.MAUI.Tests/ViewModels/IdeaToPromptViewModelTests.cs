using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.Views;
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
        vm.SelectedModelTier = ModelTier.Local;

        vm.BuildCommand.CanExecute(null).Should().BeFalse("no idea entered yet");

        vm.Idea = "a red fox in snow";

        vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void BuildCommand_RequiresExplicitPromptWriter()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        vm.Idea = "a red fox in snow";

        vm.SelectedModelTier.Should().BeNull();
        vm.BuildCommand.CanExecute(null).Should().BeFalse("paid prompt writers must not be selected silently");
        vm.ModelSummary.Should().Contain("Pick a prompt writer");
    }

    // ---- Pass 1 only (checkbox off) -----------------------------------------------------

    [Fact]
    public async Task Build_JsonUnchecked_ShowsProseAndLeavesGeneratorUntouched()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()), generator);
        vm.SelectedModelTier = ModelTier.Local;
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
        vm.SelectedModelTier = ModelTier.Local;
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
        vm.SelectedModelTier = ModelTier.Local;
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
    public async Task Build_SelectedLocalTier_IsPassedToBothPromptBuilderPasses()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: null);
        vm.SelectedModelTier = ModelTier.Local;
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseTiers.Should().Equal(ModelTier.Local);
        builder.JsonTiers.Should().Equal(ModelTier.Local);
    }

    [Fact]
    public void BuildCommand_ImageMode_RequiresReferenceImage()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.SelectedModelTier = ModelTier.Local;

        vm.SourceMode = IdeaSourceMode.Image;

        vm.BuildCommand.CanExecute(null).Should().BeFalse("image mode needs an image, not typed text");

        vm.SetReferenceImageForTest("ref.png", [1, 2, 3, 4]);

        vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SetReferenceImageFromPath_LoadsVisionReferenceImage()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var filePath = Path.Combine(Path.GetTempPath(), $"idea-reference-{Guid.NewGuid():N}.png");
        vm.SelectedModelTier = ModelTier.Local;
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ObservedImageDescription = "old observation";

        try
        {
            await File.WriteAllBytesAsync(filePath, [1, 2, 3, 4]);

            await vm.SetReferenceImageFromPathAsync(filePath);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String([1, 2, 3, 4]));
        vm.ReferenceImageFileName.Should().Be(Path.GetFileName(filePath));
        vm.ObservedImageDescription.Should().BeEmpty();
        vm.HasReferenceImage.Should().BeTrue();
        vm.BuildCommand.CanExecute(null).Should().BeTrue();
        vm.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public async Task Build_ImageMode_ObservesImageBeforeVpe_AndKeepsObservationVisible()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var observer = new FakeVisionObserver(VisionObservationResult.Ok("A boy on a playground swing in warm sun."));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: null, visionObserver: observer);
        vm.SelectedModelTier = ModelTier.Local;
        vm.SourceMode = IdeaSourceMode.Image;
        vm.Idea = "make it uplifting";
        vm.BuildJson = false;
        vm.SetReferenceImageForTest("swing.png", [1, 2, 3, 4]);

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().ContainSingle();
        observer.Requests[0].Provider.Should().Be(VisionObservationProvider.LocalOllama);
        observer.Requests[0].FileName.Should().Be("swing.png");
        vm.ObservedImageDescription.Should().Be("A boy on a playground swing in warm sun.");
        vm.HasObservation.Should().BeTrue();
        builder.ProseInputs.Should().ContainSingle()
            .Which.Should().Contain("A boy on a playground swing")
            .And.Contain("make it uplifting");
        vm.Prose.Should().Be(Prose);
        vm.StatusKind.Should().Be(StatusKind.Success);
    }

    [Fact]
    public async Task Build_ImageMode_ObservationFailure_StopsBeforeVpe()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var observer = new FakeVisionObserver(VisionObservationResult.Fail("not a vision model"));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: null, visionObserver: observer);
        vm.SelectedModelTier = ModelTier.Local;
        vm.SourceMode = IdeaSourceMode.Image;
        vm.SetReferenceImageForTest("ref.png", [1, 2, 3, 4]);

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().ContainSingle();
        builder.ProseInputs.Should().BeEmpty("a failed observation must not spend a later model call");
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("not a vision model");
        vm.HasProse.Should().BeFalse();
    }

    [Fact]
    public async Task Build_JsonChecked_JsonFails_KeepsProseVisibleAndSurfacesError()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseOk(), PromptBuilderResult.Fail("Claude's prompt didn't satisfy the schema"), generator);
        vm.SelectedModelTier = ModelTier.Local;
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
        vm.SelectedModelTier = ModelTier.Local;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("API key");
        vm.HasProse.Should().BeFalse();
        generator.Parameters.UseJsonPrompt.Should().BeFalse();
        generator.Parameters.Prompt.Should().Be(originalPrompt);
    }

    // ---- Cancellation -------------------------------------------------------------------

    [Fact]
    public async Task Cancel_AbortsAnInFlightBuild_AndReportsCancelled()
    {
        var builder = new CancellableProseBuilder();
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: null);
        vm.SelectedModelTier = ModelTier.Local;
        vm.Idea = "a red fox in snow";

        // Start the build; it parks at the (infinite) prose call until the token fires.
        var build = vm.BuildCommand.ExecuteAsync(null);
        vm.IsBusy.Should().BeTrue();
        vm.CancelCommand.CanExecute(null).Should().BeTrue("a build is in flight");

        vm.CancelCommand.Execute(null);
        await build;

        builder.WasCancelled.Should().BeTrue("the token must reach the underlying call, not just hide the button");
        vm.StatusKind.Should().Be(StatusKind.Info);
        vm.StatusMessage.Should().Be("Build cancelled.");
        vm.IsBusy.Should().BeFalse();
        vm.CancelCommand.CanExecute(null).Should().BeFalse("no build is running anymore");
    }

    [Fact]
    public async Task CopyProse_PutsTheProseOnTheClipboard()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.SelectedModelTier = ModelTier.Local;
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";
        await vm.BuildCommand.ExecuteAsync(null);

        await vm.CopyProseCommand.ExecuteAsync(null);

        _clipboard.Verify(c => c.SetTextAsync(Prose), Times.Once);
    }

    [Theory]
    [InlineData(ModelTier.Opus)]
    [InlineData(ModelTier.Sonnet)]
    public async Task Build_ExplicitPaidTier_IsPassedToPromptBuilder(ModelTier tier)
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: null);
        vm.SelectedModelTier = tier;
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseTiers.Should().Equal(tier);
    }

    [Fact]
    public async Task Build_OpenRouterImageMode_WithBlankModel_FailsBeforeObservation()
    {
        var generator = BuildGenerator();
        generator.OpenRouterVisionModel = string.Empty;
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var observer = new FakeVisionObserver(VisionObservationResult.Ok("not reached"));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator, visionObserver: observer);
        vm.SelectedModelTier = ModelTier.Local;
        vm.SourceMode = IdeaSourceMode.Image;
        vm.SelectedVisionProvider = VisionObservationProvider.OpenRouter;
        vm.SetReferenceImageForTest("ref.png", [1, 2, 3, 4]);

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().BeEmpty();
        builder.ProseInputs.Should().BeEmpty();
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("OpenRouter vision model");
    }

    [Fact]
    public async Task SetReferenceImageFromUrl_DownloadsVisionReferenceImage()
    {
        var downloader = new FakeReferenceImageDownloader(ReferenceImageDownloadResult.Ok("browser.png", [1, 2, 3, 4]));
        var vm = new IdeaToPromptViewModel(new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            referenceImageDownloader: downloader);

        await vm.SetReferenceImageFromUrlAsync("https://example.test/image.png?token=secret");

        downloader.Requests.Should().ContainSingle().Which.Should().Be("https://example.test/image.png?token=secret");
        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String([1, 2, 3, 4]));
        vm.ReferenceImageFileName.Should().Be("browser.png");
        vm.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void SetReferenceImageFromBytes_LoadsBrowserBitmapReferenceImage()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = vm.SetReferenceImageFromBytes("browser-reference.png", [1, 2, 3, 4], "browser bitmap");

        loaded.Should().BeTrue();
        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String([1, 2, 3, 4]));
        vm.ReferenceImageFileName.Should().Be("browser-reference.png");
        vm.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public void DropHelpers_ExtractHttpUrlFromBrowserHtml()
    {
        var ok = IdeaToPromptPage.TryExtractImageSrc(
            """<div><img alt="x" src="https://cdn.example.test/image.webp?size=large"></div>""",
            out var url);

        ok.Should().BeTrue();
        url.Should().Be("https://cdn.example.test/image.webp?size=large");
    }

    [Fact]
    public void DropHelpers_RejectBlobUrl()
    {
        var ok = IdeaToPromptPage.TryExtractImageSrc(
            """<img src="blob:https://example.test/123">""",
            out _);

        ok.Should().BeFalse();
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
        public Task<ProseResult> BuildProseAsync(
            string idea,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus) =>
            Task.FromResult(prose);

        public Task<PromptBuilderResult> BuildJsonAsync(
            string prose,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus) =>
            Task.FromResult(json);
    }

    private sealed class TrackingPromptBuilder(ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public List<string> ProseInputs { get; } = [];
        public List<ModelTier> ProseTiers { get; } = [];
        public List<ModelTier> JsonTiers { get; } = [];

        public Task<ProseResult> BuildProseAsync(
            string idea,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus)
        {
            ProseInputs.Add(idea);
            ProseTiers.Add(tier);
            return Task.FromResult(prose);
        }

        public Task<PromptBuilderResult> BuildJsonAsync(
            string prose,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus)
        {
            JsonTiers.Add(tier);
            return Task.FromResult(json);
        }
    }

    // Parks pass 1 until the token is cancelled, then propagates the cancellation — lets a test prove
    // the token actually flows to the underlying call rather than the button merely being disabled.
    private sealed class CancellableProseBuilder : IPromptBuilderService
    {
        public bool WasCancelled { get; private set; }

        public async Task<ProseResult> BuildProseAsync(
            string idea,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }

            return ProseResult.Ok(Prose);
        }

        public Task<PromptBuilderResult> BuildJsonAsync(
            string prose,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus) =>
            Task.FromResult(PromptBuilderResult.Fail("not reached in the cancellation test"));
    }

    private sealed class FakeVisionObserver(VisionObservationResult result) : IVisionObservationService
    {
        public List<VisionObservationRequest> Requests { get; } = [];

        public Task<VisionObservationResult> ObserveAsync(
            VisionObservationRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }

    private sealed class FakeReferenceImageDownloader(ReferenceImageDownloadResult result) : IReferenceImageDownloadService
    {
        public List<string> Requests { get; } = [];

        public Task<ReferenceImageDownloadResult> DownloadAsync(Uri uri, long maxBytes, CancellationToken ct = default)
        {
            maxBytes.Should().Be(20L * 1024 * 1024);
            Requests.Add(uri.ToString());
            return Task.FromResult(result);
        }
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
