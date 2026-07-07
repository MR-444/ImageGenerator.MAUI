using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace ImageGenerator.MAUI.Infrastructure.Imaging;

/// <summary>
/// Splices a single PNG text chunk into already-encoded PNG bytes without
/// decoding the pixels. This lets <c>ImageFileService</c> attach its
/// <c>Comment</c> metadata while preserving the provider's exact image stream
/// (byte-identical pixels, no re-compression) — far cheaper than the full
/// decode-to-Rgba32 + re-encode round-trip.
/// </summary>
public static class PngTextChunkWriter
{
    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>True when <paramref name="bytes"/> starts with the 8-byte PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= Signature.Length && bytes[..Signature.Length].SequenceEqual(Signature);

    /// <summary>
    /// Returns a copy of <paramref name="pngBytes"/> with every existing text
    /// chunk (<c>tEXt</c>/<c>zTXt</c>/<c>iTXt</c>) and any <c>eXIf</c> chunk
    /// removed, and a single fresh uncompressed <c>iTXt</c> chunk carrying
    /// <paramref name="text"/> under <paramref name="keyword"/> inserted just
    /// before the terminating <c>IEND</c> chunk.
    ///
    /// Stripping the source's own text/EXIF first preserves the existing
    /// "the prompt never shows up twice in a viewer" behaviour; in the common
    /// case (providers embed no metadata) nothing is stripped and the pixel
    /// stream stays byte-identical to the input.
    ///
    /// <c>iTXt</c> (UTF-8) is used rather than <c>tEXt</c> (Latin-1) so prompts
    /// containing emoji or non-Latin scripts survive intact. ImageSharp's
    /// reader surfaces both chunk types through the same <c>TextData</c> keyword
    /// lookup, so downstream readers (Remix, CivitAI posting) are unaffected.
    /// </summary>
    public static byte[] WriteComment(byte[] pngBytes, string keyword, string text)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (!IsPng(pngBytes))
            throw new ArgumentException("Input is not a PNG (bad signature).", nameof(pngBytes));

        var newChunk = BuildITxtChunk(keyword, text);

        using var output = new MemoryStream(pngBytes.Length + newChunk.Length);
        output.Write(Signature);

        var inserted = false;
        var offset = Signature.Length;
        while (offset + 8 <= pngBytes.Length)
        {
            var dataLength = BinaryPrimitives.ReadUInt32BigEndian(pngBytes.AsSpan(offset, 4));
            var type = pngBytes.AsSpan(offset + 4, 4);
            // length(4) + type(4) + data + crc(4)
            var chunkTotal = 12 + (int)dataLength;
            if (offset + chunkTotal > pngBytes.Length)
                throw new ArgumentException("Truncated PNG chunk.", nameof(pngBytes));

            if (type.SequenceEqual("IEND"u8))
            {
                // Our chunk must precede IEND, which is always the final chunk.
                output.Write(newChunk);
                inserted = true;
                output.Write(pngBytes.AsSpan(offset, chunkTotal));
            }
            else if (type.SequenceEqual("tEXt"u8) || type.SequenceEqual("zTXt"u8) ||
                     type.SequenceEqual("iTXt"u8) || type.SequenceEqual("eXIf"u8))
            {
                // Drop — superseded by the chunk we are inserting (dedup).
            }
            else
            {
                output.Write(pngBytes.AsSpan(offset, chunkTotal));
            }

            offset += chunkTotal;
        }

        if (!inserted)
            throw new ArgumentException("PNG had no IEND chunk.", nameof(pngBytes));

        return output.ToArray();
    }

    /// <summary>
    /// Builds a complete, uncompressed iTXt chunk: length + "iTXt" + data + CRC.
    /// Data layout per the PNG spec: keyword(Latin-1)\0, compression-flag(0),
    /// compression-method(0), language-tag\0, translated-keyword\0, text(UTF-8).
    /// </summary>
    private static byte[] BuildITxtChunk(string keyword, string text)
    {
        var keywordLength = Encoding.Latin1.GetByteCount(keyword);
        var language = "en"u8;
        var textLength = Encoding.UTF8.GetByteCount(text);

        // type(4) + keyword + \0 + flag + method + language + \0 + \0 + text
        var dataLength = keywordLength + 1 + 1 + 1 + language.Length + 1 + 1 + textLength;
        var chunk = new byte[4 + 4 + dataLength + 4];
        var pos = 0;

        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(pos, 4), (uint)dataLength);
        pos += 4;

        var crcStart = pos; // CRC covers type + data
        "iTXt"u8.CopyTo(chunk.AsSpan(pos, 4));
        pos += 4;

        pos += Encoding.Latin1.GetBytes(keyword, chunk.AsSpan(pos));
        chunk[pos++] = 0;            // keyword null separator
        chunk[pos++] = 0;            // compression flag (uncompressed)
        chunk[pos++] = 0;            // compression method
        language.CopyTo(chunk.AsSpan(pos));
        pos += language.Length;
        chunk[pos++] = 0;            // language-tag null separator
        chunk[pos++] = 0;            // translated-keyword (empty) null separator
        pos += Encoding.UTF8.GetBytes(text, chunk.AsSpan(pos));

        // CRC-32/ISO-HDLC over chunk type + data, as PNG requires.
        var crc = Crc32.HashToUInt32(chunk.AsSpan(crcStart, 4 + dataLength));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(pos, 4), crc);

        return chunk;
    }
}
