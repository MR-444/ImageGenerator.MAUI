using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Ideogram;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation;
using ImageGenerator.MAUI.Core.Domain.Ideogram.Mutation.Library;
using ImageGenerator.MAUI.Infrastructure.Services;
using ImageGenerator.MAUI.Tests.Core.Domain.Ideogram.Mutation;
using Microsoft.Extensions.Logging.Abstractions;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Services;

public sealed class MutationLibraryServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "mutation-library-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private MutationLibraryService CreateSut(Func<string, Task<Stream>>? assetOpener = null) =>
        new(NullLogger<MutationLibraryService>.Instance, _tempDir, assetOpener ?? DefaultsOpener);

    // ---- Round-trip --------------------------------------------------------------------

    [Fact]
    public async Task SaveThenLoad_RoundTripsTheLibrary()
    {
        var original = MutationTestData.Library();

        await CreateSut().SaveAsync(original);
        var loaded = await CreateSut().LoadAsync();

        loaded.StyleFragments.Should().BeEquivalentTo(original.StyleFragments);
        loaded.OrnamentKits.Should().BeEquivalentTo(original.OrnamentKits);
        loaded.SceneElements.Should().BeEquivalentTo(original.SceneElements);
    }

    // ---- Seeding -----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_SeedsMissingStoresFromDefaults_AndWritesThemToDisk()
    {
        var library = await CreateSut().LoadAsync(); // temp dir starts empty → all three seed

        File.Exists(Path.Combine(_tempDir, "style-fragments.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ornament-kits.json")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "scene-elements.json")).Should().BeTrue();

        library.StyleFragments.Select(f => f.Name).Should().Contain(["gouache", "anime", "density"]);
        library.OrnamentKits.Should().ContainSingle(k => k.Name == "density");
        library.SceneElements.Select(e => e.Name).Should().BeEquivalentTo("trailing_ivy", "luna_moth");
    }

    [Fact]
    public async Task LoadAsync_DoesNotReseed_WhenStoreAlreadyExists()
    {
        Directory.CreateDirectory(_tempDir);
        var stylePath = Path.Combine(_tempDir, "style-fragments.json");
        await File.WriteAllTextAsync(stylePath, """[{"name":"only_mine","style":{"medium":"painting","art_style":"x"}}]""");

        var library = await CreateSut().LoadAsync();

        // The user's existing store is preserved, not overwritten by the bundled default.
        library.StyleFragments.Should().ContainSingle(f => f.Name == "only_mine");
    }

    // ---- Graceful degradation ----------------------------------------------------------

    [Fact]
    public async Task LoadAsync_CorruptStore_DegradesToEmpty_WithoutSinkingTheOthers()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "style-fragments.json"), "{ not valid json ][");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "ornament-kits.json"), KitsJson);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "scene-elements.json"), SceneJson);

        var library = await CreateSut().LoadAsync();

        library.StyleFragments.Should().BeEmpty();
        library.OrnamentKits.Should().HaveCount(1);
        library.SceneElements.Should().HaveCount(2);
    }

    [Fact]
    public async Task LoadAsync_MissingBundledAsset_LeavesStoreEmpty_WithoutThrowing()
    {
        Task<Stream> Opener(string name) => name.Contains("style-fragments")
            ? throw new FileNotFoundException(name)
            : DefaultsOpener(name);

        var library = await CreateSut(Opener).LoadAsync();

        library.StyleFragments.Should().BeEmpty();
        File.Exists(Path.Combine(_tempDir, "style-fragments.json")).Should().BeFalse();
        library.SceneElements.Should().HaveCount(2); // the other stores still seed normally
    }

    // ---- Shipped seed files guard ------------------------------------------------------

    [Fact]
    public async Task ShippedSeedFiles_DeserializeIntoTheCuratedLibrary()
    {
        var seedFolder = SeedFolder();
        Directory.Exists(seedFolder).Should().BeTrue($"the bundled seed folder should exist at {seedFolder}");

        Task<Stream> RealSeedOpener(string name) =>
            Task.FromResult<Stream>(File.OpenRead(Path.Combine(seedFolder, FileNameOf(name))));

        var library = await CreateSut(RealSeedOpener).LoadAsync();

        library.StyleFragments.Select(f => f.Name).Should().Equal(
            "gouache", "anime", "density",
            "oil_impasto", "watercolor", "pastel_soft", "charcoal_sketch", "woodcut",
            "ukiyo_e", "risograph", "art_nouveau", "art_deco", "comic_ink",
            "cyberpunk_neon", "synthwave", "low_poly", "pixel_art", "concept_matte",
            "stained_glass", "sumi_e", "papercut_layered",
            "cinematic_film", "film_noir", "analog_35mm");
        library.OrnamentKits.Should().ContainSingle(k => k.Name == "density");
        library.SceneElements.Select(e => e.Name).Should().Equal("trailing_ivy", "luna_moth");

        // Spot-check the schema survived: the gouache fragment is a valid single-branch style with its palette.
        var gouache = library.StyleFragments.First(f => f.Name == "gouache");
        gouache.Style.ArtStyle.Should().NotBeNullOrWhiteSpace();
        gouache.Style.Photo.Should().BeNull();
        gouache.Style.ColorPalette.Should().HaveCount(8);

        // Every shipped fragment must be schema-clean — an invalid one is silently dropped (operator
        // returns null) at apply time, so it would never surface in the app. Catch that here instead.
        foreach (var fragment in library.StyleFragments)
        {
            var probe = CaptionClone.Clone(MutationTestData.BaseCaption());
            probe.StyleDescription = StyleMath.Clone(fragment.Style);
            V4JsonPromptValidator.Validate(probe).Should()
                .BeEmpty($"shipped style '{fragment.Name}' must be a schema-valid style_description");
        }

        // And the density kit's phrases round-tripped their string enum tiers.
        var density = library.OrnamentKits.Single();
        density.PhrasesBySlot.Should().ContainKey("subject.garment");
        density.PhrasesBySlot["subject.garment"].Should()
            .Contain(p => p.Category == DescBudgetCategory.StyleMarker);
    }

    // ---- Helpers / fakes ---------------------------------------------------------------

    private static string FileNameOf(string assetName) => assetName.Split('/')[^1];

    // Locates the real Resources/Raw/MutationDefaults folder relative to THIS source file (compile-time path),
    // robust to bin depth and CI checkout location.
    private static string SeedFolder([CallerFilePath] string? thisFile = null)
    {
        var servicesDir = Path.GetDirectoryName(thisFile)!;                 // ...\Tests\Infrastructure\Services
        var repoRoot = Path.GetFullPath(Path.Combine(servicesDir, "..", "..", ".."));
        return Path.Combine(repoRoot, "ImageGenerator.MAUI", "Resources", "Raw", "MutationDefaults");
    }

    private static Task<Stream> DefaultsOpener(string assetName)
    {
        var json = FileNameOf(assetName) switch
        {
            "style-fragments.json" => StyleJson,
            "ornament-kits.json" => KitsJson,
            "scene-elements.json" => SceneJson,
            _ => throw new FileNotFoundException(assetName)
        };
        return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json)));
    }

    private const string StyleJson =
        """
        [
          {"name":"gouache","style":{"medium":"painting","art_style":"gouache","color_palette":["#0E0F14"]}},
          {"name":"anime","style":{"medium":"illustration","art_style":"anime"}},
          {"name":"density","style":{"medium":"illustration","art_style":"density"}}
        ]
        """;

    private const string KitsJson =
        """
        [
          {"name":"density","phrasesBySlot":{"subject.garment":[{"text":"harness straps","category":"StyleMarker"}]}}
        ]
        """;

    private const string SceneJson =
        """
        [
          {"name":"trailing_ivy","type":"obj","slotTag":"scene.flora","desc":"ivy"},
          {"name":"luna_moth","type":"obj","slotTag":"prop.instrument","desc":"moth"}
        ]
        """;
}
