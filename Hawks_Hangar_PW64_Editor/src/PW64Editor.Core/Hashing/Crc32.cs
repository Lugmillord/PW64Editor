namespace PW64Editor.Core.Hashing;

/// <summary>
/// Standard CRC-32 (IEEE 802.3, polynomial 0xEDB88320, the same one used by ZIP, PNG and BPS).
/// </summary>
/// <remarks>
/// .NET only offers CRC-32 through the separate "System.IO.Hashing" NuGet package.
/// The algorithm is tiny, so we implement it ourselves and avoid the extra dependency.
/// It is used to identify the IPL3 boot code (CIC detection) and later for BPS patches.
/// </remarks>
public static class Crc32
{
    private const uint Polynomial = 0xEDB88320;

    // Precomputed lookup table: the CRC of every possible byte value.
    // This makes the main loop process one byte with a single table lookup.
    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes the CRC-32 of the given data.</summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        return Append(0, data);
    }

    /// <summary>
    /// Continues a CRC-32 calculation. Pass the result of a previous call as <paramref name="crc"/>
    /// to process data in several chunks: Append(Compute(a), b) == Compute(a + b).
    /// </summary>
    public static uint Append(uint crc, ReadOnlySpan<byte> data)
    {
        // CRC-32 works on the inverted value internally; we undo it at the end.
        crc = ~crc;
        foreach (byte b in data)
        {
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? (value >> 1) ^ Polynomial : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
