using System.Buffers.Binary;

namespace ImgForge.Tests;

/// <summary>
/// Reads image dimensions from a PNG file's IHDR chunk, avoiding a dependency on an imaging library.
/// </summary>
internal static class PngHeader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static (int Width, int Height) ReadDimensions(string path)
    {
        // Layout: 8-byte signature, then IHDR chunk: 4-byte length, "IHDR", 4-byte width, 4-byte height (big-endian).
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        stream.ReadExactly(header);

        if (!header[..8].SequenceEqual(Signature) || !header[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException($"'{path}' is not a valid PNG file.");

        return (BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
                BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }
}
