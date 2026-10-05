using System.Buffers.Binary;

namespace PW64Editor.Core.Rom;

/// <summary>
/// Detects the byte order of an N64 ROM image and converts between the three common layouts.
/// </summary>
public static class ByteOrderConverter
{
    // The first word of every retail N64 ROM is the PI bus domain 1 configuration value
    // 0x80371240. We use it as a "magic number" to recognize the layout.
    private const uint MagicBigEndian = 0x80371240;   // 80 37 12 40
    private const uint MagicByteSwapped = 0x37804012; // 37 80 40 12
    private const uint MagicLittleEndian = 0x40123780; // 40 12 37 80

    /// <summary>
    /// Determines the byte order by looking at the first four bytes of the image.
    /// </summary>
    /// <param name="data">The raw ROM data as read from disk (at least 4 bytes).</param>
    /// <returns>The detected byte order, or <see cref="RomByteOrder.Unknown"/>.</returns>
    public static RomByteOrder Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
        {
            return RomByteOrder.Unknown;
        }

        // Read the first word as big-endian and compare it against all three variants.
        uint firstWord = BinaryPrimitives.ReadUInt32BigEndian(data);
        return firstWord switch
        {
            MagicBigEndian => RomByteOrder.BigEndian,
            MagicByteSwapped => RomByteOrder.ByteSwapped,
            MagicLittleEndian => RomByteOrder.LittleEndian,
            _ => RomByteOrder.Unknown,
        };
    }

    /// <summary>
    /// Converts ROM data from <paramref name="sourceOrder"/> to big-endian, in place.
    /// </summary>
    /// <remarks>
    /// Both swap operations are their own inverse (swapping twice restores the original),
    /// so the same method can also be used to convert big-endian data back into another layout.
    /// See <see cref="ConvertFromBigEndian"/>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The order is unknown, or the data length is not a multiple of the word size.
    /// </exception>
    public static void ConvertToBigEndian(Span<byte> data, RomByteOrder sourceOrder)
    {
        switch (sourceOrder)
        {
            case RomByteOrder.BigEndian:
                // Already in native order, nothing to do.
                break;

            case RomByteOrder.ByteSwapped:
                Swap16(data);
                break;

            case RomByteOrder.LittleEndian:
                Swap32(data);
                break;

            default:
                throw new ArgumentException(
                    "Cannot convert data with an unknown byte order.", nameof(sourceOrder));
        }
    }

    /// <summary>
    /// Converts big-endian ROM data to <paramref name="targetOrder"/>, in place.
    /// Useful when a user explicitly wants to export a .v64 or .n64 file.
    /// </summary>
    public static void ConvertFromBigEndian(Span<byte> data, RomByteOrder targetOrder)
    {
        // The swaps are symmetric, so converting "to" and "from" is the same operation.
        ConvertToBigEndian(data, targetOrder);
    }

    /// <summary>
    /// Returns the conventional file extension for a byte order (including the dot).
    /// </summary>
    public static string GetFileExtension(RomByteOrder order) => order switch
    {
        RomByteOrder.BigEndian => ".z64",
        RomByteOrder.ByteSwapped => ".v64",
        RomByteOrder.LittleEndian => ".n64",
        _ => ".bin",
    };

    /// <summary>Swaps the two bytes of every 16-bit word: AB CD -> BA DC.</summary>
    private static void Swap16(Span<byte> data)
    {
        if (data.Length % 2 != 0)
        {
            throw new ArgumentException("Data length must be a multiple of 2.", nameof(data));
        }

        for (int i = 0; i < data.Length; i += 2)
        {
            (data[i], data[i + 1]) = (data[i + 1], data[i]);
        }
    }

    /// <summary>Reverses the four bytes of every 32-bit word: ABCD -> DCBA.</summary>
    private static void Swap32(Span<byte> data)
    {
        if (data.Length % 4 != 0)
        {
            throw new ArgumentException("Data length must be a multiple of 4.", nameof(data));
        }

        for (int i = 0; i < data.Length; i += 4)
        {
            data.Slice(i, 4).Reverse();
        }
    }
}
