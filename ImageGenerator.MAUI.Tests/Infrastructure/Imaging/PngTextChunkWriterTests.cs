using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using ImageGenerator.MAUI.Infrastructure.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Png.Chunks;
using SixLabors.ImageSharp.PixelFormats;

namespace ImageGenerator.MAUI.Tests.Infrastructure.Imaging;

public class PngTextChunkWriterTests
{
    [Fact]
    public void IsPng_TrueForPngBytes_FalseOtherwise()
    {
        PngTextChunkWriter.IsPng(BuildPng()).Should().BeTrue();
        PngTextChunkWriter.IsPng([0xFF, 0xD8, 0xFF, 0xE0]).Should().BeFalse(); // JPEG SOI
        PngTextChunkWriter.IsPng([0x89, 0x50]).Should().BeFalse();             // too short
    }

    [Fact]
    public void WriteComment_ProducesChunkImageSharpCanRead()
    {
        var result = PngTextChunkWriter.WriteComment(BuildPng(), "Comment", "Prompt: hello 🐱");

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(result);
        var comment = image.Metadata.GetPngMetadata().TextData.Single(t => t.Keyword == "Comment");
        comment.Value.Should().Be("Prompt: hello 🐱");
    }

    [Fact]
    public void WriteComment_PreservesIdatStreamExactly()
    {
        var original = BuildPng();

        var result = PngTextChunkWriter.WriteComment(original, "Comment", "x");

        ExtractIdat(result).Should().Equal(ExtractIdat(original));
    }

    [Fact]
    public void WriteComment_RemovesPreexistingTextChunks()
    {
        var withComment = BuildPng(existingComment: "Prompt: stale");

        var result = PngTextChunkWriter.WriteComment(withComment, "Comment", "Prompt: fresh");

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(result);
        var comments = image.Metadata.GetPngMetadata().TextData.Where(t => t.Keyword == "Comment").ToList();
        comments.Should().ContainSingle();
        comments[0].Value.Should().Be("Prompt: fresh");
    }

    [Fact]
    public void WriteComment_InsertedChunkHasValidCrc()
    {
        var result = PngTextChunkWriter.WriteComment(BuildPng(), "Comment", "Prompt: crc check");

        // Walk every chunk and verify its stored CRC against a recomputed one — proves the
        // hand-rolled CRC32 matches the PNG spec for the chunk we inserted.
        var offset = 8;
        var sawITxt = false;
        while (offset + 8 <= result.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(result, offset + 4, 4);
            var stored = BinaryPrimitives.ReadUInt32BigEndian(result.AsSpan(offset + 8 + length, 4));
            Crc32(result.AsSpan(offset + 4, 4 + length)).Should().Be(stored, $"chunk {type} CRC");
            if (type == "iTXt") sawITxt = true;
            offset += 12 + length;
        }
        sawITxt.Should().BeTrue();
    }

    [Fact]
    public void WriteComment_ThrowsOnNonPng()
    {
        var act = () => PngTextChunkWriter.WriteComment([1, 2, 3, 4], "Comment", "x");
        act.Should().Throw<ArgumentException>();
    }

    private static byte[] BuildPng(string? existingComment = null)
    {
        using var image = new SixLabors.ImageSharp.Image<Rgba32>(6, 6);
        for (var y = 0; y < 6; y++)
            for (var x = 0; x < 6; x++)
                image[x, y] = new Rgba32((byte)(x * 40), (byte)(y * 40), 128, 255);

        if (existingComment is not null)
            image.Metadata.GetPngMetadata().TextData.Add(new PngTextData("Comment", existingComment, "en", string.Empty));

        using var ms = new MemoryStream();
        image.Save(ms, new PngEncoder());
        return ms.ToArray();
    }

    private static byte[] ExtractIdat(byte[] png)
    {
        using var ms = new MemoryStream();
        var offset = 8;
        while (offset + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT") ms.Write(png, offset + 8, length);
            offset += 12 + length;
        }
        return ms.ToArray();
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
