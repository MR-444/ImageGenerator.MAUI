using FluentAssertions;
using ImageGenerator.MAUI.Core.Application.Interfaces;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Infrastructure.External.Anthropic;
using ImageGenerator.MAUI.Infrastructure.Interfaces;
using ImageGenerator.MAUI.Presentation.ViewModels;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Tests.TestSupport;
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");

        vm.BuildCommand.CanExecute(null).Should().BeFalse("no idea entered yet");

        vm.Idea = "a red fox in snow";

        vm.BuildCommand.CanExecute(null).Should().BeTrue();
    }

    // ---- The writer's decision ---------------------------------------------------------------

    [Fact]
    public void TheWriterOpensOnLocalBecauseLocalCannotBillAnyone()
    {
        var vm = NewWriterVm(EmptyUiStateStore.New());

        vm.Writer!.SelectedProvider.Should().Be(ModelProvider.Local,
            "nobody has picked anything, and the provider that cannot bill anyone is the one to open on");
    }

    [Fact]
    public void BuildCommand_RefusesUntilAModelIsPicked_AndLightsUpWhenOneIs()
    {
        var vm = NewVm(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm.Idea = "a red fox in snow";

        // Nothing picked, so nothing may be called — but the reason is a missing model, not a missing
        // permission. The row opens usable; the placeholder it replaces refused without saying what for.
        vm.BuildCommand.CanExecute(null).Should().BeFalse("no model is picked on the provider being shown");

        PickWriter(vm, ModelProvider.Local, "llama3.1");

        vm.BuildCommand.CanExecute(null).Should().BeTrue(
            "a pick or a refresh can make a build possible while the page sits open, and the button has to notice");
    }

    [Fact]
    public void TheWriterIsOfferedEveryTextProviderAndTheObserverIsNot()
    {
        var vm = NewWriterVm(new Mock<IUiStateStore>());

        // The name of the fix. The two cards used to answer one question in two dialects — "Local" here,
        // "Local (Ollama)" above it — because a tier named two providers and no more, and because an
        // observer sends a picture that this build cannot send to Claude.
        vm.Writer!.Providers.Should().Equal(ModelProvider.Local, ModelProvider.Anthropic, ModelProvider.OpenRouter);
        vm.Vision!.Providers.Should().Equal(ModelProvider.Local, ModelProvider.OpenRouter);
    }

    // ---- Prompt-writer persistence ---------------------------------------------------------

    [Fact]
    public void OpeningThePageRestoresWhicheverWriterTheUserLastUsed()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmProvider(ModelRole.PromptWrite, It.IsAny<ModelProvider>()))
            .Returns(ModelProvider.OpenRouter);
        store.Setup(s => s.LoadLlmModel(ModelRole.PromptWrite, ModelProvider.OpenRouter))
            .Returns("deepseek/deepseek-chat");

        var vm = NewWriterVm(store);

        vm.Writer!.SelectedProvider.Should().Be(ModelProvider.OpenRouter, "the last-used writer survives a restart");
        vm.Writer.SelectedModel.Should().Be("deepseek/deepseek-chat");
    }

    [Fact]
    public void RememberingTheWriterWritesTheWriterRoleAndNoOtherRole()
    {
        var store = new Mock<IUiStateStore>();
        var vm = NewWriterVm(store);

        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Sonnet);

        store.Verify(s => s.PersistLlmProvider(ModelRole.PromptWrite, ModelProvider.Anthropic), Times.Once);
        store.Verify(
            s => s.PersistLlmModel(ModelRole.PromptWrite, ModelProvider.Anthropic, AnthropicModels.Sonnet),
            Times.Once);
        store.Verify(
            s => s.PersistLlmModel(It.Is<ModelRole>(r => r != ModelRole.PromptWrite), It.IsAny<ModelProvider>(),
                It.IsAny<string?>()),
            Times.Never, "this page does not decide what the mutator or the observer calls");
    }

    [Fact]
    public void TheWriterSaysWhoIsBilledAndWhereTheirKeyLives()
    {
        var vm = NewWriterVm(new Mock<IUiStateStore>());

        // No figures in any of them. What this replaces compared two named Claude models by price out of a
        // constant nobody had read off an API, and once the picker can hold whatever a key can reach, any
        // number written here would simply be invented.
        PickWriter(vm, ModelProvider.Local, "llama3.1");
        vm.Writer!.Helper.Should().Contain("Free",
            "the reason to open on Local is that nobody is billed, and the sentence has to say so");
        vm.Writer.Helper.Should().Contain("Settings",
            "the Ollama address lives on another page, and a person picking a local model needs to know that");

        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.Writer!.Helper.Should().Contain("Anthropic API key",
            "a paid call the user cannot configure is a paid call they will blame the app for");

        PickWriter(vm, ModelProvider.OpenRouter, "deepseek/deepseek-chat");
        vm.Writer!.Helper.Should().Contain("OpenRouter API key");
    }

    [Fact]
    public async Task ALocalWriterRunUnloadsTheModelItCalledNotTheModelTheLegacyKeyHolds()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmModel(ModelRole.PromptWrite, ModelProvider.Local)).Returns("row-writer-model");
        var generator = BuildGenerator();
        var catalog = new Mock<IOllamaModelCatalog>();
        var vm = NewWriterVm(store, generator, catalog.Object);
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        catalog.Verify(
            c => c.UnloadAsync(It.IsAny<string>(), "row-writer-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AWriterThatMovesMidRun_KeepsCallingTheWriterTheRunStartedWith()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmModel(ModelRole.PromptWrite, ModelProvider.Local)).Returns("started-model");
        store.Setup(s => s.LoadLlmModel(ModelRole.PromptWrite, ModelProvider.Anthropic)).Returns("moved-model");
        var catalog = new Mock<IOllamaModelCatalog>();

        IdeaToPromptViewModel? vm = null;
        // The row moves the moment the first call lands: a build that started on a local model must not
        // send its second half to Claude because the pointer happened to land elsewhere mid-run.
        var builder = new MovingPickerPromptBuilder(
            () => vm!.Writer!.SelectedProvider = ModelProvider.Anthropic,
            ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        vm = new IdeaToPromptViewModel(builder,
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            BuildGenerator(),
            ollamaCatalog: catalog.Object,
            uiStateStore: store.Object);
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseTargets.Should().ContainSingle().Which.ModelId.Should().Be("started-model");
        builder.JsonTargets.Should().ContainSingle().Which.Provider.Should().Be(ModelProvider.Local,
            "a call already in flight is billed to whoever the run started with");
        catalog.Verify(
            c => c.UnloadAsync(It.IsAny<string>(), "started-model", It.IsAny<CancellationToken>()), Times.Once,
            "and the model freed afterwards is the one that was loaded, not the one the row now shows");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
    public async Task Build_ALocalWriterIsPassedToBothPromptBuilderPasses()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var vm = NewVm(builder);
        PickWriter(vm, ModelProvider.Local, "llama3.1");
        vm.BuildJson = true;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        // Both passes go to the target the row shows, and "Local" is the provider, not a model name:
        // the tag comes from the row too, which is what the tier could not carry.
        builder.ProseTargets.Should().OnlyContain(t => t.Provider == ModelProvider.Local && t.ModelId == "llama3.1");
        builder.JsonTargets.Should().OnlyContain(t => t.Provider == ModelProvider.Local && t.ModelId == "llama3.1");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
    public async Task Build_LocalWriterWithNoModelPicked_FailsBeforeAnyModelCall()
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        // The store answers "nobody has picked a model", which is a real state: a first launch, or a
        // server that had nothing installed when the list was refreshed.
        var vm = NewVm(builder);
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        builder.ProseInputs.Should().BeEmpty("guessing a model name only produced an opaque Ollama 404");
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("No local Ollama model selected");
    }

    [Fact]
    public async Task Build_JsonChecked_JsonFails_KeepsProseVisibleAndSurfacesError()
    {
        var generator = BuildGenerator();
        var originalPrompt = generator.Parameters.Prompt;
        var vm = NewVm(ProseOk(), PromptBuilderResult.Fail("Claude's prompt didn't satisfy the schema"), generator);
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
        var vm = NewVm(builder);
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";
        await vm.BuildCommand.ExecuteAsync(null);

        await vm.CopyProseCommand.ExecuteAsync(null);

        _clipboard.Verify(c => c.SetTextAsync(Prose), Times.Once);
    }

    [Theory]
    [InlineData(ModelProvider.Anthropic, AnthropicModels.Opus)]
    [InlineData(ModelProvider.Anthropic, AnthropicModels.Sonnet)]
    [InlineData(ModelProvider.OpenRouter, "deepseek/deepseek-chat")]
    public async Task Build_AnExplicitWriterIsTheModelThatGetsCalled(
        ModelProvider provider, string model)
    {
        var builder = new TrackingPromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption()));
        var vm = NewVm(builder);
        PickWriter(vm, provider, model);
        vm.BuildJson = false;
        vm.Idea = "a red fox in snow";

        await vm.BuildCommand.ExecuteAsync(null);

        var passed = builder.ProseTargets.Single();
        passed.Provider.Should().Be(provider);
        passed.ModelId.Should().Be(model,
            "the row names the model, and the service is told that one rather than a model read back out of settings");
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
        PickWriter(vm, ModelProvider.Local, "llama3.1");
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
            generator ?? BuildGenerator(),
            uiStateStore: new Mock<IUiStateStore>().Object);

    private IdeaToPromptViewModel NewVm(IPromptBuilderService builder, GeneratorViewModel? generator = null) =>
        new(builder,
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator ?? BuildGenerator(),
            uiStateStore: new Mock<IUiStateStore>().Object);

    /// <summary>A page with settings to read, which is the app's normal state: the writer row needs both
    /// a store to remember through and a host to read providers from.</summary>
    private IdeaToPromptViewModel NewWriterVm(
        Mock<IUiStateStore> store, GeneratorViewModel? generator = null, IOllamaModelCatalog? catalog = null) =>
        new(new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator ?? BuildGenerator(),
            ollamaCatalog: catalog,
            uiStateStore: store.Object);

    /// <summary>Point the writer row the way the user does: provider first, because a model id means
    /// nothing on the wrong provider.</summary>
    private static void PickWriter(IdeaToPromptViewModel vm, ModelProvider provider, string model)
    {
        var writer = vm.Writer
            ?? throw new InvalidOperationException("This page was built with no model settings to read.");
        writer.SelectedProvider = provider;
        writer.SelectedModel = model;
    }



    private sealed class FakePromptBuilder(ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public Task<ProseResult> BuildProseAsync(
            string idea,
            LlmTarget target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(prose);

        public Task<PromptBuilderResult> BuildJsonAsync(
            string proseInner,
            LlmTarget target,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(json);
    }

    /// <summary>Moves the page's row the instant a prose call arrives, to prove a run does not follow it.</summary>
    private sealed class MovingPickerPromptBuilder(
        Action onFirstCall, ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public List<LlmTarget> ProseTargets { get; } = [];
        public List<LlmTarget> JsonTargets { get; } = [];

        public Task<ProseResult> BuildProseAsync(
            string idea,
            LlmTarget target,
            CancellationToken cancellationToken = default)
        {
            ProseTargets.Add(target);
            onFirstCall();
            return Task.FromResult(prose);
        }

        public Task<PromptBuilderResult> BuildJsonAsync(
            string proseInner,
            LlmTarget target,
            CancellationToken cancellationToken = default)
        {
            JsonTargets.Add(target);
            return Task.FromResult(json);
        }
    }

    private sealed class TrackingPromptBuilder(ProseResult prose, PromptBuilderResult json) : IPromptBuilderService
    {
        public List<string> ProseInputs { get; } = [];
        public List<LlmTarget> ProseTargets { get; } = [];
        public List<LlmTarget> JsonTargets { get; } = [];

        public Task<ProseResult> BuildProseAsync(
            string idea,
            LlmTarget target,
            CancellationToken cancellationToken = default)
        {
            ProseInputs.Add(idea);
            ProseTargets.Add(target);
            return Task.FromResult(prose);
        }

        public Task<PromptBuilderResult> BuildJsonAsync(
            string proseInner,
            LlmTarget target,
            CancellationToken cancellationToken = default)
        {
            JsonTargets.Add(target);
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
            LlmTarget target,
            CancellationToken cancellationToken = default)
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
            LlmTarget target,
            CancellationToken cancellationToken = default) =>
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
    // The Local tier has no default model, so a generator whose prompt writer has never picked one
    // blocks every build with "pick a local Ollama model first". Tests about the prose/JSON passes
    // want a working setup; the guard itself is covered by Build_LocalTier_WithNoModelPicked_*.
    // Note which pick this is: the WRITER's, not Settings.OllamaModel, which names the image model.
    // ---- The observer's own decision ------------------------------------------------------
    //
    // Nothing below this line existed before, because nothing here was covered: the observation path had
    // no view-model test at all. That is how it accumulated a provider the page never saved (so every
    // navigation answered in Ollama) and a model the service read out of the settings store rather than
    // from the row the user had just picked from.

    [Fact]
    public void OpeningThePageRestoresWhicheverObserverTheUserLastUsed()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmProvider(ModelRole.Observe, It.IsAny<ModelProvider>()))
            .Returns(ModelProvider.OpenRouter);
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.OpenRouter))
            .Returns("google/gemini-3.1-flash-image");

        var vm = NewVisionVm(store, new RecordingVisionObserver());

        vm.Vision!.SelectedProvider.Should().Be(ModelProvider.OpenRouter,
            "a picker that resets to Ollama on every visit is a picker the user cannot use");
        vm.Vision.SelectedModel.Should().Be("google/gemini-3.1-flash-image");
    }

    [Fact]
    public void RememberingTheObserverWritesTheObserveRoleAndNoOtherRole()
    {
        var store = new Mock<IUiStateStore>();
        var vm = NewVisionVm(store, new RecordingVisionObserver());

        vm.Vision!.SelectedProvider = ModelProvider.OpenRouter;

        store.Verify(s => s.PersistLlmProvider(ModelRole.Observe, ModelProvider.OpenRouter), Times.Once);
        store.Verify(s => s.PersistLlmProvider(It.Is<ModelRole>(r => r != ModelRole.Observe), It.IsAny<ModelProvider>()), Times.Never,
            "the mutation page's provider is not this page's to write");
    }

    [Fact]
    public void TheFreeOnlySwitchFollowsTheRowNotTheProviderItStartedOn()
    {
        var vm = NewVisionVm(new Mock<IUiStateStore>(), new RecordingVisionObserver());
        var raised = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IdeaToPromptViewModel.IsOpenRouterVisionProvider)) raised++;
        };

        vm.Vision!.SelectedProvider = ModelProvider.OpenRouter;
        vm.IsOpenRouterVisionProvider.Should().BeTrue();
        raised.Should().Be(1, "the free-only switch is a binding, and a binding nobody tells is a switch that vanishes");

        vm.Vision.SelectedProvider = ModelProvider.Local;
        vm.IsOpenRouterVisionProvider.Should().BeFalse();
        raised.Should().Be(2);
    }

    [Fact]
    public async Task ObservingCallsTheModelTheRowShowsNotTheOneTheSettingsStoreRemembers()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.Local)).Returns("row-model");
        var generator = BuildGenerator();
        // The legacy shared key the service used to go and read for itself.
        generator.Settings.OllamaVisionModel = "stored-model";
        var observer = new RecordingVisionObserver();
        var vm = NewVisionVm(store, observer, generator: generator);
        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().ContainSingle();
        observer.Requests[0].Target.Provider.Should().Be(ModelProvider.Local);
        observer.Requests[0].Target.ModelId.Should().Be("row-model");
    }

    [Fact]
    public async Task ObservingNothingPickedRefusesRatherThanCallingWhateverIsStored()
    {
        var observer = new RecordingVisionObserver();
        var vm = NewVisionVm(new Mock<IUiStateStore>(), observer);
        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().BeEmpty();
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("No local Ollama model selected");
    }

    [Fact]
    public async Task ASecondBuildWithTheSameImageAndModelIsNotChargedForASecondObservation()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.Local)).Returns("row-model");
        var observer = new RecordingVisionObserver();
        var vm = NewVisionVm(store, observer);
        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);
        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().ContainSingle("the caption has not gone stale just because Build was pressed again");

        // Another model answers differently, so the caption really is stale now.
        vm.Vision!.SelectedModel = "second-model";
        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().HaveCount(2);
        observer.Requests[1].Target.ModelId.Should().Be("second-model");
    }

    [Fact]
    public async Task ALocalObservationUnloadsTheModelItCalledNotTheModelTheLegacyKeyHolds()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.Local)).Returns("row-model");
        var generator = BuildGenerator();
        generator.Settings.OllamaVisionModel = "stored-model";
        var catalog = new Mock<IOllamaModelCatalog>();
        var vm = NewVisionVm(store, new RecordingVisionObserver(), generator: generator, catalog: catalog.Object);
        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);

        catalog.Verify(c => c.UnloadAsync(It.IsAny<string>(), "row-model", It.IsAny<CancellationToken>()), Times.Once);
        catalog.Verify(c => c.UnloadAsync(It.IsAny<string>(), "stored-model", It.IsAny<CancellationToken>()), Times.Never,
            "freeing a model nobody loaded leaves the one that is using the VRAM in place");
    }

    [Fact]
    public async Task AHostedObservationUnloadsNothingBecauseNothingWasLoadedLocally()
    {
        var store = new Mock<IUiStateStore>();
        store.Setup(s => s.LoadLlmProvider(ModelRole.Observe, It.IsAny<ModelProvider>()))
            .Returns(ModelProvider.OpenRouter);
        store.Setup(s => s.LoadLlmModel(ModelRole.Observe, ModelProvider.OpenRouter))
            .Returns("google/gemini-3.1-flash-image");
        var catalog = new Mock<IOllamaModelCatalog>();
        var observer = new RecordingVisionObserver();
        var vm = NewVisionVm(store, observer, catalog: catalog.Object);
        PickWriter(vm, ModelProvider.Anthropic, AnthropicModels.Opus);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().ContainSingle();
        catalog.Verify(c => c.UnloadAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheObserverIsOfferedOnlyTheProvidersThatCanTakeAnImage()
    {
        var vm = NewVisionVm(new Mock<IUiStateStore>(), new RecordingVisionObserver());

        // The text roles offer three; handing the observer the same dictionary would put Claude in a
        // picker whose calls all carry a picture, and the refusal for that would only surface after the
        // user had already been billed for the attempt.
        vm.Vision!.Providers.Should().Equal(ModelProvider.Local, ModelProvider.OpenRouter);
    }

    [Fact]
    public void OpensTheObserverOnLocalOllamaBecauseObservingCostsNothingWhenNothingWasChosen()
    {
        var vm = NewVisionVm(EmptyUiStateStore.New(), new RecordingVisionObserver());

        // The writer's rule again, and the same reason to state it twice: this row sends somebody's
        // photograph somewhere, and on a first visit "somewhere" must not be a host that bills.
        vm.Vision!.SelectedProvider.Should().Be(ModelProvider.Local);
    }

    [Fact]
    public async Task WithoutModelSettingsThePageRefusesRatherThanCallingAGuessedModel()
    {
        // No settings to read is not the app's normal state; it is a page built without a host generator.
        // The old code did not notice either, and reached past the picker into the settings store for a
        // model, which is exactly the guess this change set removes. Both rows are built from the same
        // settings, so neither has a model to guess from, and the page says so instead of calling one.
        var observer = new RecordingVisionObserver();
        var vm = new IdeaToPromptViewModel(
            new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            BuildGenerator(),
            visionObserver: observer);
        vm.SourceMode = IdeaSourceMode.Image;
        vm.ReferenceImageBase64 = "data:image/png;base64,abcd";

        await vm.BuildCommand.ExecuteAsync(null);

        observer.Requests.Should().BeEmpty();
        vm.StatusKind.Should().Be(StatusKind.Error);
        vm.StatusMessage.Should().Contain("Model settings");
    }

    private IdeaToPromptViewModel NewVisionVm(
        Mock<IUiStateStore> store,
        IVisionObservationService observer,
        GeneratorViewModel? generator = null,
        IOllamaModelCatalog? catalog = null) =>
        new(new FakePromptBuilder(ProseOk(), JsonOk(MutationTestData.BaseCaption())),
            _clipboard.Object,
            NullLogger<IdeaToPromptViewModel>.Instance,
            generator ?? BuildGenerator(),
            ollamaCatalog: catalog,
            visionObserver: observer,
            uiStateStore: store.Object);

    private sealed class RecordingVisionObserver : IVisionObservationService
    {
        public List<VisionObservationRequest> Requests { get; } = [];

        public Task<VisionObservationResult> ObserveAsync(VisionObservationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(VisionObservationResult.Ok("A russet-red fox mid-step through fresh snow."));
        }
    }

    private static GeneratorViewModel BuildGenerator() => BuildBareGenerator();

    /// <summary>The host these pages read settings through; see <c>BareGenerator</c> in TestSupport.</summary>
    private static GeneratorViewModel BuildBareGenerator() => BareGenerator.New();
}
