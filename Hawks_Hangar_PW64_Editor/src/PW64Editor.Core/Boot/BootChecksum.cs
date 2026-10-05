using System.Buffers.Binary;
using System.Numerics;

namespace PW64Editor.Core.Boot;

/// <summary>
/// The two 32-bit checksum values stored in the ROM header at 0x10 (CRC1) and 0x14 (CRC2).
/// </summary>
public readonly record struct BootChecksumValues(uint Crc1, uint Crc2)
{
    public override string ToString() => $"0x{Crc1:X8} / 0x{Crc2:X8}";
}

/// <summary>
/// Computes the boot checksum that the IPL3 boot code verifies at power-on.
/// </summary>
/// <remarks>
/// <para>
/// IPL3 copies the first 1 MiB of the game (ROM 0x1000-0x100FFF) into RAM and computes a
/// checksum over it. If the result differs from CRC1/CRC2 in the header, the console refuses
/// to boot (black screen). Emulators often ignore this, real hardware never does.
/// </para>
/// <para>
/// So whenever we change anything inside that 1 MiB (typically code patches), we must
/// recompute and store the checksum. Changes beyond 0x101000 (e.g. most game assets)
/// do not affect it.
/// </para>
/// <para>
/// This is a port of the well-known algorithm from n64crc.c (based on uCON64), which
/// reimplements what the IPL3 code does on the console.
/// </para>
/// </remarks>
public static class BootChecksum
{
    /// <summary>First ROM offset covered by the checksum.</summary>
    public const int ChecksumStart = 0x1000;

    /// <summary>Number of bytes covered by the checksum (1 MiB).</summary>
    public const int ChecksumLength = 0x100000;

    /// <summary>End of the checksummed region (exclusive).</summary>
    public const int ChecksumEnd = ChecksumStart + ChecksumLength;

    private const int OffsetCrc1 = 0x10;
    private const int OffsetCrc2 = 0x14;

    /// <summary>
    /// Returns true if changing a byte at <paramref name="romOffset"/> changes the boot checksum.
    /// </summary>
    public static bool IsInChecksumRange(int romOffset) =>
        romOffset >= ChecksumStart && romOffset < ChecksumEnd;

    /// <summary>
    /// Computes CRC1/CRC2 for big-endian ROM data.
    /// </summary>
    /// <exception cref="NotSupportedException">The CIC type is unknown.</exception>
    /// <exception cref="ArgumentException">The ROM is too small.</exception>
    public static BootChecksumValues Compute(ReadOnlySpan<byte> romData, CicType cic)
    {
        if (romData.Length < ChecksumEnd)
        {
            throw new ArgumentException(
                $"ROM must be at least 0x{ChecksumEnd:X} bytes to compute the boot checksum.", nameof(romData));
        }

        uint seed = GetSeed(cic);

        // Six running accumulators, all starting with the CIC-specific seed.
        // The names t1..t6 follow the original n64crc.c source to make comparison easy.
        uint t1 = seed, t2 = seed, t3 = seed, t4 = seed, t5 = seed, t6 = seed;

        // Note on overflow: C# integer arithmetic silently wraps around by default
        // (unless the project enables "checked" arithmetic), which is exactly the
        // 32-bit behavior of the MIPS CPU that we need here.
        for (int i = ChecksumStart; i < ChecksumEnd; i += 4)
        {
            uint d = BinaryPrimitives.ReadUInt32BigEndian(romData.Slice(i, 4));

            // Count carries out of t6 (detects 32-bit overflow of the addition below).
            if (t6 + d < t6)
            {
                t4++;
            }

            t6 += d;
            t3 ^= d;

            uint r = BitOperations.RotateLeft(d, (int)(d & 0x1F));
            t5 += r;

            if (t2 > d)
            {
                t2 ^= r;
            }
            else
            {
                t2 ^= t6 ^ d;
            }

            if (cic == CicType.Cic6105)
            {
                // 6105 mixes in a word from its own IPL3 code (ROM 0x750-0x84F).
                int index = 0x0750 + (i & 0xFF);
                t1 += BinaryPrimitives.ReadUInt32BigEndian(romData.Slice(index, 4)) ^ d;
            }
            else
            {
                t1 += t5 ^ d;
            }
        }

        // Combine the accumulators. 6103 and 6106 use a different final formula.
        return cic switch
        {
            CicType.Cic6103 => new BootChecksumValues((t6 ^ t4) + t3, (t5 ^ t2) + t1),
            CicType.Cic6106 => new BootChecksumValues((t6 * t4) + t3, (t5 * t2) + t1),
            _ => new BootChecksumValues(t6 ^ t4 ^ t3, t5 ^ t2 ^ t1),
        };
    }

    /// <summary>Reads the checksum currently stored in the header.</summary>
    public static BootChecksumValues ReadFromHeader(ReadOnlySpan<byte> romData)
    {
        return new BootChecksumValues(
            BinaryPrimitives.ReadUInt32BigEndian(romData[OffsetCrc1..]),
            BinaryPrimitives.ReadUInt32BigEndian(romData[OffsetCrc2..]));
    }

    /// <summary>Writes a checksum into the header.</summary>
    public static void WriteToHeader(Span<byte> romData, BootChecksumValues values)
    {
        BinaryPrimitives.WriteUInt32BigEndian(romData[OffsetCrc1..], values.Crc1);
        BinaryPrimitives.WriteUInt32BigEndian(romData[OffsetCrc2..], values.Crc2);
    }

    /// <summary>
    /// The starting value of the accumulators. It is derived from the CIC's seed byte
    /// and is hardcoded in each IPL3 variant.
    /// </summary>
    private static uint GetSeed(CicType cic) => cic switch
    {
        CicType.Cic6101 or CicType.Cic6102 => 0xF8CA4DDC,
        CicType.Cic6103 => 0xA3886759,
        CicType.Cic6105 => 0xDF26F436,
        CicType.Cic6106 => 0x1FEA617A,
        _ => throw new NotSupportedException($"Cannot compute the boot checksum for CIC type '{cic}'."),
    };
}
