using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.ValueObjects;
using ImageGenerator.MAUI.Presentation.ViewModels;

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
        AspectRatios: new[] { "match_input_image", "16:9", "1:1" },
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
            initialPreferredAspectRatio: initialPreferredAspectRatio);
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
        coord.SelectedImages.Should().HaveCount(1);
        coord.SelectedImages[0].Base64.Should().Be("a", "truncation removes from the end");
    }

    [Fact]
    public void TruncateToMaxInputs_WithinCap_NoOpReturnsFalse()
    {
        var coord = Build();
        coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("a", null, "a.png"));

        var dropped = coord.TruncateToMaxInputs(5);

        dropped.Should().BeFalse();
        coord.SelectedImages.Should().HaveCount(1);
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
        await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3, 4 });
        try
        {
            var statusMessages = new List<(string Msg, StatusKind Kind)>();
            var coord = Build(setStatus: (m, k) => statusMessages.Add((m, k)));
            coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("seeded", null, "seeded.png", tempFile));

            await coord.AddAsInputAsync(tempFile);

            coord.SelectedImages.Should().HaveCount(1, "second add of the same path is deduped");
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
        await File.WriteAllBytesAsync(tempFile, new byte[] { 4, 5, 6 });
        try
        {
            var statusMessages = new List<(string Msg, StatusKind Kind)>();
            var coord = Build(caps: capOne, setStatus: (m, k) => statusMessages.Add((m, k)));
            coord.SelectedImages.Add(new InputImagesCoordinator.InputImageItem("seeded", null, "seeded.png", "other-path.png"));

            await coord.AddAsInputAsync(tempFile);

            coord.SelectedImages.Should().HaveCount(1, "cap is reached; new add is rejected");
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
