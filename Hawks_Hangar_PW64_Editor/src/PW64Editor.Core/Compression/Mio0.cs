using System.Buffers.Binary;

namespace PW64Editor.Core.Compression;

/// <summary>
/// Compressor and decompressor for Nintendo's MIO0 format (LZ77 variant).
/// </summary>
/// <remarks>
/// <para>
/// MIO0 is used by several first-party N64 games (Super Mario 64, Mario Kart 64, Pilotwings 64).
/// A block has three separate streams after the 16-byte header (see <see cref="Mio0Header"/>):
/// </para>
/// <list type="bullet">
///   <item><b>Layout bits</b>: one bit per output step, read most significant bit first from
///         32-bit big-endian words. 1 = copy one literal byte, 0 = copy a back-reference.</item>
///   <item><b>Compressed stream</b>: 16-bit big-endian back-references.
///         Upper 4 bits = length - 3 (so 3..18 bytes), lower 12 bits = distance - 1 (so 1..4096).</item>
///   <item><b>Uncompressed stream</b>: the literal bytes, in order.</item>
/// </list>
/// <para>
/// A back-reference means "copy <i>length</i> bytes starting <i>distance</i> bytes back in the
/// output produced so far". Source and destination may overlap (distance &lt; length), which is
/// how runs of repeated bytes are encoded, so the copy must happen byte by byte.
/// </para>
/// </remarks>
public static class Mio0
{
    /// <summary>Shortest back-reference. Shorter matches are stored as literals.</summary>
    public const int MinMatchLength = 3;

    /// <summary>Longest back-reference (4 bits of length + 3).</summary>
    public const int MaxMatchLength = 18;

    /// <summary>Farthest a back-reference can reach (12 bits of distance + 1).</summary>
    public const int MaxDistance = 4096;

    /// <summary>
    /// Decompresses a MIO0 block.
    /// </summary>
    /// <param name="source">Data starting at the "MIO0" magic. May extend beyond the block.</param>
    /// <returns>The decompressed data.</returns>
    /// <exception cref="InvalidDataException">The block is corrupt.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> source)
    {
        return Decompress(source, out _);
    }

    /// <summary>
    /// Decompresses a MIO0 block and reports how many bytes of <paramref name="source"/> it used.
    /// </summary>
    /// <param name="source">Data starting at the "MIO0" magic. May extend beyond the block.</param>
    /// <param name="compressedLength">
    /// The size of the MIO0 block. The format does not store this anywhere; it is the end of the
    /// last literal byte that was read.
    /// </param>
    /// <exception cref="InvalidDataException">The block is corrupt.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> source, out int compressedLength)
    {
        Mio0Header header = Mio0Header.Parse(source);
        byte[] output = new byte[header.DecompressedSize];

        int layoutPos = Mio0Header.Size;                  // next layout word
        int compressedPos = header.CompressedStreamOffset; // next back-reference
        int literalPos = header.UncompressedStreamOffset;  // next literal byte
        int outPos = 0;

        uint layoutWord = 0;
        int bitsLeft = 0;

        try
        {
            while (outPos < output.Length)
            {
                // Fetch the next 32 layout bits when the current word is used up.
                if (bitsLeft == 0)
                {
                    layoutWord = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(layoutPos, 4));
                    layoutPos += 4;
                    bitsLeft = 32;
                }

                bool isLiteral = (layoutWord & 0x80000000) != 0;
                layoutWord <<= 1;
                bitsLeft--;

                if (isLiteral)
                {
                    output[outPos++] = source[literalPos++];
                    continue;
                }

                ushort reference = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(compressedPos, 2));
                compressedPos += 2;

                int length = (reference >> 12) + MinMatchLength;
                int distance = (reference & 0x0FFF) + 1;
                int copyFrom = outPos - distance;

                if (copyFrom < 0)
                {
                    throw new InvalidDataException(
                        $"MIO0 back-reference points before the start of the output (at output offset 0x{outPos:X}).");
                }

                // Byte-by-byte on purpose: the ranges may overlap (see class remarks).
                // Clamp to the output size in case the last reference runs past the end.
                for (int i = 0; i < length && outPos < output.Length; i++)
                {
                    output[outPos++] = output[copyFrom + i];
                }
            }
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // Slice() throws this when a stream runs past the end of the source data.
            throw new InvalidDataException("MIO0 data is truncated.", ex);
        }
        catch (IndexOutOfRangeException ex)
        {
            throw new InvalidDataException("MIO0 data is truncated.", ex);
        }

        compressedLength = literalPos;
        return output;
    }

    /// <summary>
    /// Compresses data into a MIO0 block.
    /// </summary>
    /// <remarks>
    /// The output is not byte-identical to Nintendo's original compressor (which is unknown),
    /// but any correct MIO0 decoder, including the game's own, decompresses it to the same data.
    /// Unchanged game files should therefore never be recompressed; copy their original bytes.
    /// <para>
    /// The block layout is: header, layout bits (padded to 4 bytes), back-references
    /// (padded to 4 bytes), literal bytes. The padding keeps every stream 4-byte aligned,
    /// which is the safest choice for decoders running on the N64.
    /// </para>
    /// </remarks>
    public static byte[] Compress(ReadOnlySpan<byte> data)
    {
        var matcher = new LzMatcher(data, MinMatchLength, MaxMatchLength, MaxDistance);

        var layoutBits = new List<bool>();     // true = literal, false = back-reference
        var references = new List<ushort>();
        var literals = new List<byte>();

        int pos = 0;
        while (pos < data.Length)
        {
            (int length, int distance) = matcher.FindLongestMatch(pos);

            // Lazy matching: if the match starting at the next byte is longer,
            // emit the current byte as a literal and take the better match next round.
            // This usually saves a few percent compared to always taking the first match.
            if (length >= MinMatchLength && pos + 1 < data.Length)
            {
                matcher.Insert(pos);
                (int nextLength, _) = matcher.FindLongestMatch(pos + 1);
                if (nextLength > length)
                {
                    layoutBits.Add(true);
                    literals.Add(data[pos]);
                    pos++;
                    continue;
                }

                // Insert the remaining positions covered by the match into the search index.
                for (int i = 1; i < length; i++)
                {
                    matcher.Insert(pos + i);
                }
            }
            else
            {
                matcher.Insert(pos);
            }

            if (length >= MinMatchLength)
            {
                layoutBits.Add(false);
                references.Add((ushort)(((length - MinMatchLength) << 12) | (distance - 1)));
                pos += length;
            }
            else
            {
                layoutBits.Add(true);
                literals.Add(data[pos]);
                pos++;
            }
        }

        return BuildBlock(data.Length, layoutBits, references, literals);
    }

    private static byte[] BuildBlock(int decompressedSize, List<bool> layoutBits, List<ushort> references, List<byte> literals)
    {
        int layoutBytes = AlignUp((layoutBits.Count + 31) / 32 * 4, 4);
        int compressedOffset = Mio0Header.Size + layoutBytes;
        int uncompressedOffset = compressedOffset + AlignUp(references.Count * 2, 4);
        int totalSize = AlignUp(uncompressedOffset + literals.Count, 4);

        byte[] block = new byte[totalSize];
        Span<byte> span = block;

        BinaryPrimitives.WriteUInt32BigEndian(span, Mio0Header.Magic);
        BinaryPrimitives.WriteUInt32BigEndian(span[0x04..], (uint)decompressedSize);
        BinaryPrimitives.WriteUInt32BigEndian(span[0x08..], (uint)compressedOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[0x0C..], (uint)uncompressedOffset);

        // Pack the layout bits, most significant bit first, into 32-bit words.
        for (int i = 0; i < layoutBits.Count; i++)
        {
            if (layoutBits[i])
            {
                int byteIndex = Mio0Header.Size + (i / 8);
                block[byteIndex] |= (byte)(0x80 >> (i % 8));
            }
        }

        for (int i = 0; i < references.Count; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[(compressedOffset + (i * 2))..], references[i]);
        }

        literals.CopyTo(block, uncompressedOffset);
        return block;
    }

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
