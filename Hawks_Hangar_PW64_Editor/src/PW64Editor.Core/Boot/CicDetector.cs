using PW64Editor.Core.Hashing;

namespace PW64Editor.Core.Boot;

/// <summary>
/// Identifies the CIC variant of a ROM by hashing its IPL3 boot code.
/// </summary>
/// <remarks>
/// The IPL3 code is identical in every game using the same CIC family, so its CRC-32 works as
/// a fingerprint. The values below are the ones used by common tools such as n64crc and uCON64.
/// </remarks>
public static class CicDetector
{
    /// <summary>Start of the IPL3 boot code (right after the 64-byte header).</summary>
    public const int Ipl3Offset = 0x40;

    /// <summary>End of the IPL3 boot code (exclusive).</summary>
    public const int Ipl3End = 0x1000;

    /// <summary>Length of the IPL3 boot code in bytes.</summary>
    public const int Ipl3Length = Ipl3End - Ipl3Offset;

    /// <summary>
    /// Determines the CIC type from big-endian ROM data.
    /// </summary>
    /// <param name="romData">ROM data in big-endian order, at least 0x1000 bytes.</param>
    public static CicType Detect(ReadOnlySpan<byte> romData)
    {
        if (romData.Length < Ipl3End)
        {
            return CicType.Unknown;
        }

        uint ipl3Crc = Crc32.Compute(romData.Slice(Ipl3Offset, Ipl3Length));
        return ipl3Crc switch
        {
            0x6170A4A1 => CicType.Cic6101,
            0x90BB6CB5 => CicType.Cic6102,
            0x0B050EE0 => CicType.Cic6103,
            0x98BC2C86 => CicType.Cic6105,
            0xACC8580A => CicType.Cic6106,
            _ => CicType.Unknown,
        };
    }

    /// <summary>
    /// Returns a display name such as "6102 / 7101".
    /// </summary>
    public static string GetDisplayName(CicType cic) => cic switch
    {
        CicType.Cic6101 => "6101",
        CicType.Cic6102 => "6102 / 7101",
        CicType.Cic6103 => "6103 / 7103",
        CicType.Cic6105 => "6105 / 7105",
        CicType.Cic6106 => "6106 / 7106",
        _ => "Unknown",
    };
}
