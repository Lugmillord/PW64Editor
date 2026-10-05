using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// "info" command: loads a ROM, prints its header and verifies it against known clean dumps.
/// </summary>
internal static class InfoCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli info <rom>");
            return ExitCodes.InvalidArguments;
        }

        string path = args[0];

        N64Rom rom;
        try
        {
            rom = N64Rom.Load(path);
        }
        catch (Exception ex) when (ex is InvalidRomException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot load ROM: {ex.Message}");
            return ExitCodes.InvalidRom;
        }

        PrintRomInfo(rom);
        PrintBootInfo(rom);

        RomVerificationResult result = RomVerifier.Verify(rom);
        PrintVerification(result);

        return result.IsUsableAsBase ? ExitCodes.Success : ExitCodes.VerificationFailed;
    }

    private static void PrintRomInfo(N64Rom rom)
    {
        RomHeader h = rom.Header;

        Console.WriteLine($"File            : {rom.FilePath}");
        Console.WriteLine($"Size            : {rom.Size:N0} bytes ({rom.Size / (1024.0 * 1024.0):0.##} MiB)");
        Console.WriteLine($"Byte order      : {rom.OriginalByteOrder} ({ByteOrderConverter.GetFileExtension(rom.OriginalByteOrder)})");
        Console.WriteLine();
        Console.WriteLine("Header");
        Console.WriteLine($"  Internal name : \"{h.InternalName}\"");
        Console.WriteLine($"  Game code     : {h.GameCode} ({h.RegionName})");
        Console.WriteLine($"  Version       : {h.VersionString}");
        Console.WriteLine($"  Boot address  : 0x{h.BootAddress:X8}");
        Console.WriteLine($"  CRC1 / CRC2   : 0x{h.Crc1:X8} / 0x{h.Crc2:X8}");
        Console.WriteLine($"  Clock rate    : 0x{h.ClockRate:X8}");
        Console.WriteLine($"  libultra      : 0x{h.LibultraRelease:X8}");
        Console.WriteLine();
    }

    private static void PrintBootInfo(N64Rom rom)
    {
        CicType cic = rom.DetectCic();

        Console.WriteLine("Boot");
        Console.WriteLine($"  CIC           : {CicDetector.GetDisplayName(cic)}");

        if (cic == CicType.Unknown)
        {
            Console.WriteLine("  Checksum      : cannot be verified (unknown CIC)");
        }
        else
        {
            BootChecksumValues computed = rom.ComputeBootChecksum();
            bool valid = computed == BootChecksum.ReadFromHeader(rom.Data);

            ConsoleColor previous = Console.ForegroundColor;
            Console.ForegroundColor = valid ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(valid
                ? "  Checksum      : OK"
                : $"  Checksum      : INVALID (expected {computed}) - will not boot on real hardware!");
            Console.ForegroundColor = previous;
        }

        Console.WriteLine();
    }

    private static void PrintVerification(RomVerificationResult result)
    {
        Console.WriteLine($"SHA-1           : {result.Sha1}");

        // Use color to make the verdict stand out, then restore the previous color.
        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = result.IsUsableAsBase ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine($"Verification    : {result.Status}");
        Console.ForegroundColor = previous;

        Console.WriteLine($"                  {result.Message}");
    }
}
