using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// "fixcrc" command: recalculates the boot checksum (CRC1/CRC2) and writes the result
/// to a new file. Needed after manual edits inside the first MiB of the ROM, otherwise
/// the ROM will not boot on real hardware.
/// </summary>
/// <remarks>
/// The output must be a different file than the input. We never overwrite the source ROM,
/// so the user always keeps an untouched copy (see "backups" in the project requirements).
/// The output is always written in big-endian (.z64) order.
/// </remarks>
internal static class FixCrcCommand
{
    public static int Run(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli fixcrc <input> <output>");
            return ExitCodes.InvalidArguments;
        }

        string inputPath = args[0];
        string outputPath = args[1];

        // Path.GetFullPath turns relative paths and things like "..\" into one canonical form,
        // so "rom.z64" and ".\rom.z64" are recognized as the same file.
        if (string.Equals(Path.GetFullPath(inputPath), Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Output must be a different file than the input. The source ROM is never overwritten.");
            return ExitCodes.InvalidArguments;
        }

        N64Rom rom;
        try
        {
            rom = N64Rom.Load(inputPath);
        }
        catch (Exception ex) when (ex is InvalidRomException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot load ROM: {ex.Message}");
            return ExitCodes.InvalidRom;
        }

        if (rom.DetectCic() == CicType.Unknown)
        {
            Console.Error.WriteLine("Cannot fix checksum: the CIC type could not be identified from the boot code.");
            return ExitCodes.InvalidRom;
        }

        BootChecksumValues before = BootChecksum.ReadFromHeader(rom.Data);
        bool changed = rom.UpdateBootChecksum();
        BootChecksumValues after = BootChecksum.ReadFromHeader(rom.Data);

        try
        {
            rom.Save(outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot write output file: {ex.Message}");
            return ExitCodes.UnexpectedError;
        }

        Console.WriteLine(changed
            ? $"Checksum updated: {before} -> {after}"
            : $"Checksum was already correct ({after}).");
        Console.WriteLine($"Saved to {outputPath}");
        return ExitCodes.Success;
    }
}
