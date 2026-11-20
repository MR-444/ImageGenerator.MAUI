using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SixLabors.ImageSharp;
using ImageGenerator.MAUI.Infrastructure.Imaging;

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

    // ---- Prompt-writer tier persistence -------------------------------------------------

    [Fact]
    public void Construct_WithSavedTier_RestoresTheSelection()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadPromptWriterTier()).Returns(ModelTier.Opus);

        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            uiStateStore: store.Object);

        vm.SelectedModelTier.Should().Be(ModelTier.Opus, "the last-used tier survives an app restart");
    }

    [Fact]
    public void Construct_WithNoSavedTier_LeavesPickerEmpty()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadPromptWriterTier()).Returns((ModelTier?)null);

        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            uiStateStore: store.Object);

        vm.SelectedModelTier.Should().BeNull("first launch keeps the 'Pick a prompt writer…' placeholder");
    }

    [Fact]
    public void ChangingTier_PersistsTheSelection()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadPromptWriterTier()).Returns((ModelTier?)null);
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            uiStateStore: store.Object);

        vm.SelectedModelTier = ModelTier.Sonnet;

        store.Verify(s => s.PersistPromptWriterTier(ModelTier.Sonnet), Times.Once);
    }

    // ---- BuildJson checkbox persistence -------------------------------------------------

    [Fact]
    public void Construct_WithSavedBuildJson_RestoresTheCheckboxOverAnyDefault()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadIdeaBuildJson()).Returns(true);

        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            uiStateStore: store.Object);

        vm.BuildJson.Should().BeTrue("the stored checkbox choice wins over the model-based default");
    }

    [Fact]
    public void ToggleBuildJson_PersistsTheChoice()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadIdeaBuildJson()).Returns((bool?)null);
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            uiStateStore: store.Object);

        vm.BuildJson = true;

        store.Verify(s => s.PersistIdeaBuildJson(true), Times.Once);
    }

    // ---- Idea box hygiene on source switch ----------------------------------------------

    [Fact]
    public void SwitchingSourceMode_ClearsTheIdeaBox()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.Idea = "a red fox in snow";

        vm.SourceMode = IdeaSourceMode.Image;

        vm.Idea.Should().BeEmpty("a text-mode idea left behind silently steers the image build as stale notes");

        vm.Idea = "make it uplifting";
        vm.SourceMode = IdeaSourceMode.Text;

        vm.Idea.Should().BeEmpty("image-mode notes must not become the next text idea");
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
            NullLogger<IdeaToPromptViewModel>.Instance, generator: BuildGenerator())
        {
            SelectedModelTier = ModelTier.Local,
            BuildJson = true,
            Idea = "a red fox in snow"
        };

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseTiers.Should().Equal(ModelTier.Local);
        builder.JsonTiers.Should().Equal(ModelTier.Local);
    }

    // Real decodable bytes: reference-image intake now normalizes via ImagePayloadNormalizer,
    // so opaque dummies like [1,2,3,4] are rejected at import. PNGs pass through byte-identical,
    // keeping the pass-through assertions below valid.
    private static readonly byte[] TinyPng = CreatePng(1);

    private static byte[] CreatePng(int size)
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(size, size);
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
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
            await File.WriteAllBytesAsync(filePath, TinyPng);

            await vm.SetReferenceImageFromPathAsync(filePath);
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String(TinyPng));
        vm.ReferenceImageFileName.Should().Be(Path.GetFileName(filePath));
        vm.ObservedImageDescription.Should().BeEmpty();
        vm.HasReferenceImage.Should().BeTrue();
        vm.BuildCommand.CanExecute(null).Should().BeTrue();
        vm.StatusKind.Should().Be(StatusKind.Info);
    }
    
    // ---- No implicit Ollama model -------------------------------------------------------

    [Fact]
    public async Task Build_LocalTier_WithNoModelPicked_FailsBeforeAnyModelCall()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var vm = new IdeaToPromptViewModel(builder, _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance, generator: BuildGenerator(ollamaModel: string.Empty))
            {
                SelectedModelTier = ModelTier.Local,
                Idea = "a red fox in snow"
            };

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseInputs.Should().BeEmpty("guessing a model name only produced an opaque Ollama 404");
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("Pick a local Ollama model first");
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
            NullLogger<IdeaToPromptViewModel>.Instance, generator: BuildGenerator())
        {
            SelectedModelTier = ModelTier.Local,
            Idea = "a red fox in snow"
        };

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
            NullLogger<IdeaToPromptViewModel>.Instance, generator: BuildGenerator())
        {
            SelectedModelTier = tier,
            BuildJson = false,
            Idea = "a red fox in snow"
        };

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseTiers.Should().Equal(tier);
    }

    [Fact]
    public async Task SetReferenceImageFromUrl_DownloadsVisionReferenceImage()
    {
        var downloader = new FakeReferenceImageDownloader(ReferenceImageDownloadResult.Ok("browser.png", TinyPng));
        var vm = new IdeaToPromptViewModel(new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            referenceImageDownloader: downloader);

        await vm.SetReferenceImageFromUrlAsync("https://example.test/image.png?token=secret");

        downloader.Requests.Should().ContainSingle().Which.Should().Be("https://example.test/image.png?token=secret");
        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String(TinyPng));
        vm.ReferenceImageFileName.Should().Be("browser.png");
        vm.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_LoadsBrowserBitmapReferenceImage()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = await vm.SetReferenceImageFromBytesAsync("browser-reference.png", TinyPng, "browser bitmap");

        loaded.Should().BeTrue();
        vm.ReferenceImageBase64.Should().Be(Convert.ToBase64String(TinyPng));
        vm.ReferenceImageFileName.Should().Be("browser-reference.png");
        vm.StatusKind.Should().Be(StatusKind.Info);
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_RejectsEmptyClipboardBitmap()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = await vm.SetReferenceImageFromBytesAsync("pasted-reference.png", []);

        loaded.Should().BeFalse();
        vm.HasReferenceImage.Should().BeFalse();
        vm.StatusKind.Should().Be(StatusKind.Error);
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_TranscodesWebpToPng()
    {
        // The live-bug shape: a browser image (WebP, often with a lying .png/.jfif name) used
        // to pass through untouched and die at generate time as Ollama's opaque HTTP 400
        // ("Failed to load image or audio file"). Intake must hand every backend PNG/JPEG.
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2, 2);
        using var ms = new MemoryStream();
        await image.SaveAsWebpAsync(ms);
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = await vm.SetReferenceImageFromBytesAsync("browser-image.png", ms.ToArray());

        loaded.Should().BeTrue();
        var stored = Convert.FromBase64String(vm.ReferenceImageBase64);
        stored.Take(4).Should().Equal(0x89, (byte)'P', (byte)'N', (byte)'G');
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_RejectsUndecodableBytes()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = await vm.SetReferenceImageFromBytesAsync("mystery.avif", [1, 2, 3, 4]);

        loaded.Should().BeFalse();
        vm.HasReferenceImage.Should().BeFalse();
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("Unsupported image format");
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_EmptyImage_PreservesPreviousResults()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.SelectedModelTier = ModelTier.Local;
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";
        await vm.BuildCommand.ExecuteAsync(null);

        var loaded = await vm.SetReferenceImageFromBytesAsync("empty.png", []);

        loaded.Should().BeFalse();
        vm.Prose.Should().Be(Prose);
        vm.HasJson.Should().BeTrue();
        vm.CopyProseCommand.CanExecute(null).Should().BeTrue();
        vm.UseProseCommand.CanExecute(null).Should().BeTrue();
        vm.UseJsonCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public async Task SetReferenceImageFromBytes_OversizedImage_DownscalesReferenceImage()
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(2000, 1500);
        using var ms = new MemoryStream();
        await image.SaveAsPngAsync(ms);
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));

        var loaded = await vm.SetReferenceImageFromBytesAsync("large.png", ms.ToArray());

        loaded.Should().BeTrue();
        vm.HasReferenceImage.Should().BeTrue();

        // Verify the stored base64 image was actually shrunk to <= 1024px:
        var storedBytes = Convert.FromBase64String(vm.ReferenceImageBase64);
        using var decoded = SixLabors.ImageSharp.Image.Load(storedBytes);
        decoded.Width.Should().Be(ImagePayloadLimits.Vision.MaxDimension!.Value);
        decoded.Height.Should().Be(768);
    }

    // ---- Helpers / fakes ----------------------------------------------------------------

    private static ProseResult ProseOk() => ProseResult.Ok(Prose);

    private static PromptBuilderResult JsonOk(V4JsonPrompt prompt) => PromptBuilderResult.Ok(prompt);

    // Defaults to a generator with a local model picked — production always has one injected, and
    // without a pick the Local tier (which most of these tests use) refuses to build at all.
    private IdeaToPromptViewModel NewVm(ProseResult prose, PromptBuilderResult json, GeneratorViewModel? generator = null) =>
        new(new FakePromptBuilder(prose, json),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator ?? BuildGenerator());

    private sealed class FakePromptBuilder(ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public Task<ProseResult> BuildProseAsync(
            string idea,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus) =>
            Task.FromResult(prose);

        public Task<PromptBuilderResult> BuildJsonAsync(
            string proseInner,
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
            string proseInner,
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
            string proseInner,
            CancellationToken cancellationToken = default,
            ModelTier tier = ModelTier.Opus) =>
            Task.FromResult(PromptBuilderResult.Fail("not reached in the cancellation test"));
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
    // The Local tier has no default model, so a generator that has never picked one blocks every
    // build with "pick a local Ollama model first". Tests about the prose/JSON passes want a
    // working setup; the guard itself is covered by Build_LocalTier_WithNoModelPicked_*.
    private static GeneratorViewModel BuildGenerator(string ollamaModel = "llama3.1")
    {
        var generator = BuildBareGenerator();
        generator.Settings.OllamaModel = ollamaModel;
        return generator;
    }

    private static GeneratorViewModel BuildBareGenerator() =>
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
