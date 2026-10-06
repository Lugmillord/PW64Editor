using System.Text;
using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Core.Workspace;

/// <summary>
/// Creates a human readable description of a ROM (header, boot code, verification),
/// as shown by the "ROM info" function.
/// </summary>
public static class RomReport
{
    public static string Describe(N64Rom rom)
    {
        RomHeader h = rom.Header;
        CicType cic = rom.DetectCic();
        RomVerificationResult verification = RomVerifier.Verify(rom);
        var text = new StringBuilder();

        text.AppendLine($"File            : {rom.FilePath ?? "(in memory)"}");
        text.AppendLine($"Size            : {rom.Size:N0} bytes ({rom.Size / (1024.0 * 1024.0):0.##} MiB)");
        text.AppendLine($"Byte order      : {rom.OriginalByteOrder} ({ByteOrderConverter.GetFileExtension(rom.OriginalByteOrder)})");
        text.AppendLine();
        text.AppendLine("Header");
        text.AppendLine($"  Internal name : \"{h.InternalName}\"");
        text.AppendLine($"  Game code     : {h.GameCode} ({h.RegionName})");
        text.AppendLine($"  Version       : {h.VersionString}");
        text.AppendLine($"  Boot address  : 0x{h.BootAddress:X8}");
        text.AppendLine($"  CRC1 / CRC2   : 0x{h.Crc1:X8} / 0x{h.Crc2:X8}");
        text.AppendLine();
        text.AppendLine("Boot");
        text.AppendLine($"  CIC           : {CicDetector.GetDisplayName(cic)}");
        text.AppendLine(cic == CicType.Unknown
            ? "  Checksum      : cannot be verified (unknown CIC)"
            : rom.IsBootChecksumValid()
                ? "  Checksum      : OK"
                : $"  Checksum      : INVALID (expected {rom.ComputeBootChecksum()}), will not boot on real hardware");
        text.AppendLine();
        text.AppendLine($"SHA-1           : {verification.Sha1}");
        text.AppendLine($"Verification    : {verification.Status}");
        text.AppendLine($"                  {verification.Message}");
        return text.ToString();
    }
}
