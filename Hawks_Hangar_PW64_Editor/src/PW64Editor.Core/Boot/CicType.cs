namespace PW64Editor.Core.Boot;

/// <summary>
/// The CIC lockout chip variant a ROM was built for.
/// </summary>
/// <remarks>
/// Every cartridge contains a CIC chip. At power-on the console's PIF chip checks that the
/// IPL3 boot code in the ROM (offset 0x40-0xFFF) matches the CIC. IPL3 then verifies the
/// CRC1/CRC2 checksum in the header. Each CIC family uses a different IPL3, a different
/// checksum seed and (for some) a slightly different checksum formula.
/// NTSC and PAL chips of the same family (e.g. 6102 and 7101) share the same IPL3.
/// Pilotwings 64 uses <see cref="Cic6102"/>.
/// </remarks>
public enum CicType
{
    /// <summary>The IPL3 boot code does not match any known variant.</summary>
    Unknown = 0,

    /// <summary>CIC-NUS-6101 (NTSC only, e.g. Star Fox 64).</summary>
    Cic6101,

    /// <summary>CIC-NUS-6102 / 7101. The most common chip, used by about 88% of retail games.</summary>
    Cic6102,

    /// <summary>CIC-NUS-6103 / 7103 (e.g. Banjo-Kazooie, Paper Mario).</summary>
    Cic6103,

    /// <summary>CIC-NUS-6105 / 7105 (e.g. Ocarina of Time, Majora's Mask).</summary>
    Cic6105,

    /// <summary>CIC-NUS-6106 / 7106 (e.g. F-Zero X, Yoshi's Story).</summary>
    Cic6106,
}
