namespace PW64Editor.Core.Compression;

/// <summary>
/// Location of one MIO0 block inside a larger buffer (e.g. a ROM).
/// </summary>
/// <param name="Offset">Offset of the "MIO0" magic.</param>
/// <param name="CompressedLength">Size of the block in the buffer.</param>
/// <param name="DecompressedSize">Size of the data after decompression.</param>
public sealed record Mio0BlockInfo(int Offset, int CompressedLength, int DecompressedSize);

/// <summary>
/// Searches a buffer for MIO0 blocks. Mainly a research and debugging aid: the real file
/// system code will find blocks through the file table instead of by scanning.
/// </summary>
public static class Mio0Scanner
{
    /// <summary>
    /// Finds all valid MIO0 blocks in <paramref name="data"/>.
    /// </summary>
    /// <remarks>
    /// Only 4-byte aligned positions are checked (N64 data is practically always aligned).
    /// Every candidate is fully decompressed to make sure it is a real block and not just the
    /// four letters "MIO0" appearing by chance, e.g. inside a text string.
    /// </remarks>
    public static IReadOnlyList<Mio0BlockInfo> FindAll(ReadOnlySpan<byte> data)
    {
        var blocks = new List<Mio0BlockInfo>();

        for (int offset = 0; offset + Mio0Header.Size <= data.Length; offset += 4)
        {
            if (!Mio0Header.HasMagic(data[offset..]))
            {
                continue;
            }

            try
            {
                byte[] decompressed = Mio0.Decompress(data[offset..], out int compressedLength);
                blocks.Add(new Mio0BlockInfo(offset, compressedLength, decompressed.Length));
            }
            catch (InvalidDataException)
            {
                // Not a valid block, just a coincidental "MIO0". Keep scanning.
            }
        }

        return blocks;
    }
}
