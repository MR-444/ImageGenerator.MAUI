using FluentAssertions;
using ImageGenerator.MAUI.Core.Domain.Descriptors;
using ImageGenerator.MAUI.Core.Domain.Entities;
using ImageGenerator.MAUI.Core.Domain.Enums;
using ImageGenerator.MAUI.Infrastructure.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.Services;

[Collection("OutputPathsState")]
public class GalleryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ImageFileService _imageFileService;
    private DateTime _imageClock;

    public GalleryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "imggen-gallery-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _imageClock = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        // Closure captures _imageClock by reference, so reassigning it in tests steps the
        // timestamp prefix that ImageFileService.BuildFileName bakes into each filename.
        _imageFileService = new ImageFileService(
            new ImageEncoderProvider(),
            ModelDescriptorRegistry.Default(),
            () => _imageClock);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EnumerateAsync_OnMissingDirectory_ReturnsEmpty()
    {
        var nonExistent = Path.Combine(_tempDir, "no-such-folder");
        var sut = new GalleryService(nonExistent);

        var items = await CollectAsync(sut.EnumerateAsync());

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task EnumerateAsync_SortsByFilenameDescending_NewestFirst()
    {
        await SaveImageAsync(SampleParams("first", 1, ImageOutputFormat.Png));
        _imageClock = _imageClock.AddSeconds(1);
        await SaveImageAsync(SampleParams("second", 2, ImageOutputFormat.Jpg));
        _imageClock = _imageClock.AddSeconds(1);
        await SaveImageAsync(SampleParams("third", 3, ImageOutputFormat.Webp));

        // Clock far enough ahead that the 2 s in-flight guard doesn't filter anything.
        var sut = new GalleryService(_tempDir, () => _imageClock.AddYears(1));

        var items = await CollectAsync(sut.EnumerateAsync());

        items.Should().HaveCount(3);
        items[0].FileName.Should().Contain("third");
        items[1].FileName.Should().Contain("second");
        items[2].FileName.Should().Contain("first");
    }

    [Fact]
    public async Task EnumerateAsync_IgnoresNonImageFiles()
    {
        await SaveImageAsync(SampleParams("a", 1, ImageOutputFormat.Png));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.txt"), "should be ignored");

        var sut = new GalleryService(_tempDir, () => _imageClock.AddYears(1));
        var items = await CollectAsync(sut.EnumerateAsync());

        items.Should().HaveCount(1);
        items[0].FileName.Should().EndWith(".png");
    }

    [Fact]
    public async Task EnumerateAsync_SkipsFilesModifiedWithinTwoSecondsOfClock()
    {
        var path = await SaveImageAsync(SampleParams("partial", 1, ImageOutputFormat.Png));
        var mtime = File.GetLastWriteTime(path);

        // Clock equals the file's mtime → cutoff = mtime - 2s → file is "in-flight".
        var inFlight = new GalleryService(_tempDir, () => mtime);
        (await CollectAsync(inFlight.EnumerateAsync())).Should().BeEmpty(
            "files written within the last 2s of the clock are treated as potentially partial");

        // Clock 5s ahead → cutoff well past the file's mtime → file is included.
        var stable = new GalleryService(_tempDir, () => mtime.AddSeconds(5));
        (await CollectAsync(stable.EnumerateAsync())).Should().HaveCount(1);
    }

    [Fact]
    public async Task EnumerateAsync_WithNoExplicitRoot_FollowsLiveOutputFolderOverride()
    {
        await SaveImageAsync(SampleParams("override-me", 1, ImageOutputFormat.Png));

        // No ctor directory => production behaviour: resolve OutputPaths live per enumeration.
        var sut = new GalleryService(rootDirectory: null, () => _imageClock.AddYears(1));
        try
        {
            ImageGenerator.MAUI.Shared.Constants.OutputPaths.SetGeneratedImagesOverride(_tempDir);

            var items = await CollectAsync(sut.EnumerateAsync());

            items.Should().HaveCount(1);
            items[0].FileName.Should().Contain("override-me");
        }
        finally
        {
            // Process-global static state — reset so sibling tests see the default.
            ImageGenerator.MAUI.Shared.Constants.OutputPaths.SetGeneratedImagesOverride(null);
        }
    }

    [Fact]
    public async Task ReadMetadataAsync_PngRoundTrip_ParsesPromptModelSeed()
    {
        var p = SampleParams("a round-trip cat", 42, ImageOutputFormat.Png);
        var path = await SaveImageAsync(p);

        var sut = new GalleryService(_tempDir);
        var meta = await sut.ReadMetadataAsync(path);

        meta.Should().NotBeNull();
        meta!["Prompt"].Should().Be("a round-trip cat");
        meta["ModelName"].Should().Be(p.Model);
        meta["Seed"].Should().Be("42");
        meta["AspectRatio"].Should().Be("16:9");
    }

    [Fact]
    public async Task ReadMetadataAsync_JpgRoundTrip_ParsesPromptModelSeed()
    {
        var p = SampleParams("jpeg cat", 99, ImageOutputFormat.Jpg);
        var path = await SaveImageAsync(p);

        var sut = new GalleryService(_tempDir);
        var meta = await sut.ReadMetadataAsync(path);

        meta.Should().NotBeNull();
        meta!["Prompt"].Should().Be("jpeg cat");
        meta["Seed"].Should().Be("99");
    }

    [Fact]
    public async Task ReadMetadataAsync_PromptContainingColon_PreservesEverythingAfterFirstColon()
    {
        var p = SampleParams("at 12:00 a cat appears", 7, ImageOutputFormat.Png);
        var path = await SaveImageAsync(p);

        var sut = new GalleryService(_tempDir);
        var meta = await sut.ReadMetadataAsync(path);

        meta.Should().NotBeNull();
        // Regression: a naive Split(':') would chop the prompt at the embedded "12:00".
        meta!["Prompt"].Should().Be("at 12:00 a cat appears");
    }

    [Fact]
    public async Task ReadMetadataAsync_NonExistentFile_ReturnsNull()
    {
        var sut = new GalleryService(_tempDir);
        var meta = await sut.ReadMetadataAsync(Path.Combine(_tempDir, "missing.png"));
        meta.Should().BeNull();
    }

    [Fact]
    public async Task ReadMetadataAsync_FileWithoutEmbeddedComment_ReturnsNull()
    {
        // Plain ImageSharp save with no metadata mutation — no Comment chunk, no UserComment.
        var path = Path.Combine(_tempDir, "barebones.png");
        using (var image = new Image<Rgba32>(1, 1))
        {
            await image.SaveAsync(path, new PngEncoder());
        }

        var sut = new GalleryService(_tempDir);
        var meta = await sut.ReadMetadataAsync(path);

        meta.Should().BeNull();
    }

    private async Task<string> SaveImageAsync(ImageGenerationParameters parameters)
    {
        var bytes = Build1x1Image(parameters.OutputFormat);
        var path = _imageFileService.GetUniqueSavePath(_tempDir, parameters);
        await _imageFileService.SaveImageWithMetadataAsync(path, bytes, parameters);
        return path;
    }

    private static ImageGenerationParameters SampleParams(string prompt, int seed, ImageOutputFormat format) => new()
    {
        ApiToken = "t",
        Model = "black-forest-labs/flux-1.1-pro",
        Prompt = prompt,
        Seed = seed,
        AspectRatio = "16:9",
        OutputFormat = format,
        OutputQuality = 90
    };

    private static byte[] Build1x1Image(ImageOutputFormat format)
    {
        using var image = new Image<Rgba32>(1, 1);
        using var memory = new MemoryStream();
        switch (format)
        {
            case ImageOutputFormat.Jpg: image.Save(memory, new JpegEncoder()); break;
            case ImageOutputFormat.Webp: image.Save(memory, new WebpEncoder()); break;
            default: image.Save(memory, new PngEncoder()); break;
        }
        return memory.ToArray();
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source) list.Add(item);
        return list;
    }
}
