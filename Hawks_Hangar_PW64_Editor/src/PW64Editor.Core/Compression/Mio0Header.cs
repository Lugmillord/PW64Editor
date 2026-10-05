using System.Buffers.Binary;

namespace PW64Editor.Core.Compression;

/// <summary>
/// The 16-byte header at the start of every MIO0 block.
/// </summary>
/// <remarks>
/// <code>
/// 0x00  4   magic "MIO0"
/// 0x04  u32 decompressed size in bytes
/// 0x08  u32 offset of the "compressed" stream (back-references), relative to the block start
/// 0x0C  u32 offset of the "uncompressed" stream (literal bytes), relative to the block start
/// 0x10  ... layout bits (32-bit words) up to the compressed stream
/// </code>
/// </remarks>
/// <param name="DecompressedSize">Size of the data after decompression.</param>
/// <param name="CompressedStreamOffset">Start of the back-reference stream.</param>
/// <param name="UncompressedStreamOffset">Start of the literal byte stream.</param>
public readonly record struct Mio0Header(int DecompressedSize, int CompressedStreamOffset, int UncompressedStreamOffset)
{
    /// <summary>Header size in bytes. The layout bits start right after it.</summary>
    public const int Size = 0x10;

    /// <summary>The magic bytes "MIO0" as a big-endian 32-bit value.</summary>
    public const uint Magic = 0x4D494F30;

    /// <summary>
    /// Returns true if <paramref name="data"/> starts with the "MIO0" magic.
    /// </summary>
    public static bool HasMagic(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) == Magic;

    /// <summary>
    /// Parses and sanity-checks the header at the start of <paramref name="data"/>.
    /// </summary>
    /// <exception cref="InvalidDataException">The header is missing or inconsistent.</exception>
    public static Mio0Header Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size || !HasMagic(data))
        {
            throw new InvalidDataException("Data does not start with a MIO0 header.");
        }

        uint decompressedSize = BinaryPrimitives.ReadUInt32BigEndian(data[0x04..]);
        uint compressedOffset = BinaryPrimitives.ReadUInt32BigEndian(data[0x08..]);
        uint uncompressedOffset = BinaryPrimitives.ReadUInt32BigEndian(data[0x0C..]);

        // Reject values that cannot be valid, so corrupt data fails early with a clear message
        // instead of causing out-of-range errors deep inside the decoder.
        if (decompressedSize > int.MaxValue
            || compressedOffset < Size
            || uncompressedOffset < compressedOffset
            || uncompressedOffset > data.Length)
        {
            throw new InvalidDataException(
                $"Invalid MIO0 header (size 0x{decompressedSize:X}, compressed offset 0x{compressedOffset:X}, " +
                $"uncompressed offset 0x{uncompressedOffset:X}, available 0x{data.Length:X}).");
        }

        return new Mio0Header((int)decompressedSize, (int)compressedOffset, (int)uncompressedOffset);
    }
}
