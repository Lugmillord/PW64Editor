using System.Buffers.Binary;
using System.Text;

namespace PW64Editor.Core.Rom;

/// <summary>
/// The 64-byte header at the very start of every N64 ROM (offsets 0x00-0x3F).
/// </summary>
/// <remarks>
/// Layout (all multi-byte values are big-endian):
/// <code>
/// 0x00  u32   PI bus configuration (always 0x80371240 on retail ROMs)
/// 0x04  u32   Clock rate override (0x0000000F on most games)
/// 0x08  u32   Boot address: RAM address where the first 1 MiB of code is copied to
/// 0x0C  u32   libultra release the game was built with
/// 0x10  u32   CRC1 \  boot checksum, verified by the IPL3 boot code
/// 0x14  u32   CRC2 /
/// 0x18  8     unused
/// 0x20  20    internal name (Shift-JIS, padded with spaces)
/// 0x34  7     unused
/// 0x3B  char  media type ('N' = cartridge)
/// 0x3C  2     cartridge ID (e.g. "PW")
/// 0x3E  char  region / destination code (e.g. 'E' = North America)
/// 0x3F  u8    ROM version (0 = 1.0, 1 = 1.1, ...)
/// </code>
/// </remarks>
public sealed class RomHeader
{
    /// <summary>Size of the header in bytes.</summary>
    public const int Size = 0x40;

    // Header field offsets.
    private const int OffsetPiConfig = 0x00;
    private const int OffsetClockRate = 0x04;
    private const int OffsetBootAddress = 0x08;
    private const int OffsetLibultraRelease = 0x0C;
    private const int OffsetCrc1 = 0x10;
    private const int OffsetCrc2 = 0x14;
    private const int OffsetInternalName = 0x20;
    private const int InternalNameLength = 20;
    private const int OffsetMediaType = 0x3B;
    private const int OffsetCartridgeId = 0x3C;
    private const int OffsetRegion = 0x3E;
    private const int OffsetVersion = 0x3F;

    public uint PiConfig { get; private init; }
    public uint ClockRate { get; private init; }
    public uint BootAddress { get; private init; }
    public uint LibultraRelease { get; private init; }
    public uint Crc1 { get; private init; }
    public uint Crc2 { get; private init; }
    public string InternalName { get; private init; } = string.Empty;
    public char MediaType { get; private init; }
    public string CartridgeId { get; private init; } = string.Empty;
    public char Region { get; private init; }
    public byte Version { get; private init; }

    /// <summary>
    /// The four-character game code, e.g. "NPWE" for Pilotwings 64 (USA).
    /// Consists of media type + cartridge ID + region code.
    /// </summary>
    public string GameCode => $"{MediaType}{CartridgeId}{Region}";

    /// <summary>Human readable version string, e.g. "1.0".</summary>
    public string VersionString => $"1.{Version}";

    /// <summary>
    /// Human readable name of the region code. Covers the codes used by Pilotwings 64
    /// releases plus a few common ones; anything else is shown as "Unknown".
    /// </summary>
    public string RegionName => Region switch
    {
        'E' => "North America",
        'J' => "Japan",
        'P' => "Europe (PAL)",
        'D' => "Germany",
        'F' => "France",
        'U' => "Australia",
        _ => "Unknown",
    };

    /// <summary>
    /// Parses the header from big-endian ROM data.
    /// </summary>
    /// <param name="data">ROM data in big-endian order, at least <see cref="Size"/> bytes.</param>
    /// <exception cref="ArgumentException">The data is shorter than the header.</exception>
    public static RomHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < Size)
        {
            throw new ArgumentException(
                $"ROM data is too short to contain a header ({data.Length} < {Size} bytes).",
                nameof(data));
        }

        return new RomHeader
        {
            PiConfig = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetPiConfig..]),
            ClockRate = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetClockRate..]),
            BootAddress = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetBootAddress..]),
            LibultraRelease = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetLibultraRelease..]),
            Crc1 = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetCrc1..]),
            Crc2 = BinaryPrimitives.ReadUInt32BigEndian(data[OffsetCrc2..]),
            InternalName = DecodeInternalName(data.Slice(OffsetInternalName, InternalNameLength)),
            MediaType = (char)data[OffsetMediaType],
            CartridgeId = Encoding.ASCII.GetString(data.Slice(OffsetCartridgeId, 2)),
            Region = (char)data[OffsetRegion],
            Version = data[OffsetVersion],
        };
    }

    /// <summary>
    /// Decodes the internal name. It is officially Shift-JIS (Japanese games use katakana here),
    /// and padded with spaces or null bytes, which we trim.
    /// </summary>
    private static string DecodeInternalName(ReadOnlySpan<byte> raw)
    {
        return ShiftJis.GetString(raw).TrimEnd(' ', '\0');
    }

    /// <summary>
    /// Shift-JIS is not available in .NET by default. It lives in the "code pages" provider,
    /// which ships with .NET but must be registered once before use.
    /// </summary>
    private static Encoding ShiftJis
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(932); // 932 = Shift-JIS (Windows variant)
        }
    }
}
