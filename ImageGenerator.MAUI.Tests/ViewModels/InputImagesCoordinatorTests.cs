using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using ImageGenerator.MAUI.Presentation.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.ViewModels;

// Coordinator-level tests covering paths the VM-integration tests don't reach: the
// file-path AddAsInputAsync entry, TruncateToMaxInputs return value, and the
// RecordExplicitAspectRatioPick capture mechanism in isolation.
public class InputImagesCoordinatorTests
{
    private static readonly ModelCapabilities DefaultCaps = new(
        SafetyTolerance: false,
        PromptUpsampling: false,
        OutputQuality: false,
        AspectRatio: true,
        CustomDimensions: false,
        Seed: true,
        ImagePrompt: true,
        AspectRatioLabel: "Aspect ratio",
        AspectRatios: ["match_input_image", "16:9", "1:1"],
        ImagePromptStrength: true,
        MaxImageInputs: 10);

    private static InputImagesCoordinator Build(
        ModelCapabilities? caps = null,
        Action<string>? setAspectRatio = null,
        Action? mirrorImagePrompts = null,
        Action<string, StatusKind>? setStatus = null,
        string? initialPreferredAspectRatio = null)
    {
        var resolvedCaps = caps ?? DefaultCaps;
        return new InputImagesCoordinator(
            capsAccessor: () => resolvedCaps,
            setAspectRatioProgrammatically: setAspectRatio ?? (_ => { }),
            mirrorImagePromptsToParameters: mirrorImagePrompts ?? (() => { }),
            setStatus: setStatus ?? ((_, _) => { }),
            logger: NullLogger.Instance,
            initialPreferredAspectRatio: initialPreferredAspectRatio);
    }

    // ---- Intake: what arrives is not what gets sent ------------------------------------------

    [Fact]
    public async Task AddAsInputAsync_ShrinksAnOversizeFile_ToTheModelInputBudget()
    {
        var path = WritePng("oversize.png", 3000, 2000);
        var statuses = new List<(string Message, StatusKind Kind)>();
        try
        {
            var coord = Build(setStatus: (message, kind) => statuses.Add((message, kind)));

            await coord.AddAsInputAsync(path);

            var item = Assert.Single(coord.SelectedImages);
            using var sent = SixLabors.ImageSharp.Image.Load(Convert.FromBase64String(item.Base64));
            Math.Max(sent.Width, sent.Height).Should().Be(ImagePayloadLimits.ModelInput.MaxDimension!.Value,
                "a phone-camera photo is not what a hosted model should be sent");
            statuses.Should().ContainSingle().Which.Message
                .Should().StartWith("Added as input.")
                .And.Contain("→", "the status says out loud what was done to the file");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddAsInputAsync_PassesAFileInsideTheBudget_ThroughUnchanged()
    {
        var path = WritePng("fine.png", 640, 480);
        var statuses = new List<(string Message, StatusKind Kind)>();
        try
        {
            var coord = Build(setStatus: (message, kind) => statuses.Add((message, kind)));

            await coord.AddAsInputAsync(path);

            var item = Assert.Single(coord.SelectedImages);
            Convert.FromBase64String(item.Base64).Should().Equal(await File.ReadAllBytesAsync(path),
                "re-encoding a file that already fits only loses quality");
            statuses.Should().ContainSingle().Which.Message.Should().Be("Added as input.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task AddAsInputAsync_RefusesAFileNothingCanDecode()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-not-an-image.png");
        await File.WriteAllBytesAsync(path, [1, 2, 3, 4]);
        var statuses = new List<(string Message, StatusKind Kind)>();
        try
        {
            var coord = Build(setStatus: (message, kind) => statuses.Add((message, kind)));

            await coord.AddAsInputAsync(path);

            coord.SelectedImages.Should().BeEmpty(
                "a file no endpoint can decode would fail later as a provider error nobody recognises");
            statuses.Should().ContainSingle().Which.Message.Should().Contain("Unsupported image format");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePng(string fileName, int width, int height)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fileName}");
        using var image = new Image<Rgba32>(width, height, new Rgba32(255, 255, 255));
        image.SaveAsPng(path);
        return path;
    }

    [Fact]
    public void RecordExplicitAspectRatioPick_StoresValue_ReadableViaPreferredAspectRatio()
    {
        var coord = Build(initialPreferredAspectRatio: "16:9");
        coord.PreferredAspectRatio.Should().Be("16:9");

        coord.RecordExplicitAspectRatioPick("3:2");

        coord.PreferredAspectRatio.Should().Be("3:2");
    }

    [Fact]
    public void TruncateToMaxInputs_DropsExcess_ReturnsTrue()
    {
        var coord = Build();
        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("a", null, "a.png"));
        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("b", null, "b.png"));
        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("c", null, "c.png"));

        var dropped = coord.TruncateToMaxInputs(1);

        dropped.Should().BeTrue();
        coord.SelectedImages.Should().ContainSingle();
        coord.SelectedImages[0].Base64.Should().Be("a", "truncation removes from the end");
    }

    [Fact]
    public void TruncateToMaxInputs_WithinCap_NoOpReturnsFalse()
    {
        var coord = Build();
        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("a", null, "a.png"));

        var dropped = coord.TruncateToMaxInputs(5);

        dropped.Should().BeFalse();
        coord.SelectedImages.Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsInputAsync_FileNotFound_SetsErrorStatus_DoesNotAdd()
    {
        var statusMessages = new List<(string Msg, StatusKind Kind)>();
        var coord = Build(setStatus: (m, k) => statusMessages.Add((m, k)));

        await coord.AddAsInputAsync(Path.Combine(Path.GetTempPath(), "definitely-not-real-" + Guid.NewGuid() + ".png"));

        coord.SelectedImages.Should().BeEmpty();
        statusMessages.Should().ContainSingle(x => x.Kind == StatusKind.Error && x.Msg.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddAsInputAsync_DedupsByPath()
    {
        // The happy path runs ImageSource.FromStream which requires a live MAUI runtime; pre-
        // seed the collection so the dedup branch fires before that line. Verifies the
        // rejection contract — that's the part the coordinator actually owns.
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, [1, 2, 3, 4]);
        try
        {
            var statusMessages = new List<(string Msg, StatusKind Kind)>();
            var coord = Build(setStatus: (m, k) => statusMessages.Add((m, k)));
            coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("seeded", null, "seeded.png", tempFile));

            await coord.AddAsInputAsync(tempFile);

            coord.SelectedImages.Should().ContainSingle("second add of the same path is deduped");
            statusMessages.Should().Contain(x => x.Kind == StatusKind.Warning && x.Msg.Contains("already in the list"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AddAsInputAsync_AtCap_SetsErrorStatus_DoesNotAdd()
    {
        // Same MAUI-runtime constraint as the dedup test: pre-seed to cap, then verify the cap
        // check rejects the new add before the ImageSource line.
        var capOne = DefaultCaps with { MaxImageInputs = 1 };
        var tempFile = Path.GetTempFileName();
        await File.WriteAllBytesAsync(tempFile, [4, 5, 6]);
        try
        {
            var statusMessages = new List<(string Msg, StatusKind Kind)>();
            var coord = Build(caps: capOne, setStatus: (m, k) => statusMessages.Add((m, k)));
            coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("seeded", null, "seeded.png", "other-path.png"));

            await coord.AddAsInputAsync(tempFile);

            coord.SelectedImages.Should().ContainSingle("cap is reached; new add is rejected");
            statusMessages.Should().Contain(x => x.Kind == StatusKind.Error && x.Msg.Contains("Maximum"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void SelectedImages_Add_TriggersMirrorCallback()
    {
        var mirrorCalls = 0;
        var coord = Build(mirrorImagePrompts: () => mirrorCalls++);

        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("a", null, "a.png"));

        mirrorCalls.Should().Be(1);
    }
}
