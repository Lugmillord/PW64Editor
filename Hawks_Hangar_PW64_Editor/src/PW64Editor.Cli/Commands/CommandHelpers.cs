using System.Diagnostics.CodeAnalysis;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Rom;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Small helpers shared by several commands.
/// </summary>
internal static class CommandHelpers
{
    /// <summary>Loads a ROM and prints a friendly error on failure.</summary>
    public static bool TryLoadRom(string path, [NotNullWhen(true)] out N64Rom? rom)
    {
        try
        {
            rom = N64Rom.Load(path);
            return true;
        }
        catch (Exception ex) when (ex is InvalidRomException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Cannot load ROM: {ex.Message}");
            rom = null;
            return false;
        }
    }

    /// <summary>Loads a ROM and reads its file system, printing a friendly error on failure.</summary>
    public static bool TryLoadFileSystem(string path, [NotNullWhen(true)] out GameFileSystem? fileSystem)
    {
        fileSystem = null;
        if (!TryLoadRom(path, out N64Rom? rom))
        {
            return false;
        }

        try
        {
            // Only the US release is supported for now (see KnownRoms).
            fileSystem = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
            return true;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Cannot read file system: {ex.Message}");
            return false;
        }
    }
}
