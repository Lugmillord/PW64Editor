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

    /// <summary>
    /// Removes an optional flag such as "--expand" from the argument list.
    /// Flags may appear anywhere, so the remaining positional arguments keep their order.
    /// </summary>
    /// <returns>True if the flag was present.</returns>
    public static bool ExtractFlag(ref string[] args, string flag)
    {
        bool present = args.Contains(flag, StringComparer.OrdinalIgnoreCase);
        args = args.Where(a => !string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)).ToArray();
        return present;
    }

    /// <summary>
    /// Removes an option with a text value such as "--rom C:\rom.z64" from the argument list.
    /// </summary>
    /// <returns>The value, or null if the option is absent.</returns>
    /// <exception cref="ArgumentException">The option is present but has no value.</exception>
    public static string? ExtractStringOption(ref string[] args, string option)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        string value = args[index + 1];
        args = args.Where((_, i) => i != index && i != index + 1).ToArray();
        return value;
    }

    /// <summary>
    /// Removes an option with a numeric value such as "--add-dummy 1000" from the argument list.
    /// </summary>
    /// <returns>The value, or null if the option is absent.</returns>
    /// <exception cref="ArgumentException">The option is present but its value is missing or invalid.</exception>
    public static int? ExtractNumberOption(ref string[] args, string option)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        if (index + 1 >= args.Length || !HexParser.TryParseOffset(args[index + 1], out int value) || value < 0)
        {
            throw new ArgumentException($"{option} needs a number (decimal or 0x hex).");
        }

        args = args.Where((_, i) => i != index && i != index + 1).ToArray();
        return value;
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
