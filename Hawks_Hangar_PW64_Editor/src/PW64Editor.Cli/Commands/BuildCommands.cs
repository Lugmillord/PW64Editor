using PW64Editor.Core.Build;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Verification;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Commands that produce a new ROM:
/// "rebuild" reassembles a ROM without changes (to verify the build pipeline),
/// "fs-replace" swaps one game file for a file from disk.
/// </summary>
internal static class BuildCommands
{
    public static int Rebuild(string[] args)
    {
        // Optional flag: recompress every file with our own MIO0 compressor.
        // This is a stress test: it moves nearly every file, so the output can be tested in an
        // emulator or on hardware to prove the game accepts our compressed data.
        bool recompress = args.Contains("--recompress");
        args = args.Where(a => a != "--recompress").ToArray();

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli rebuild <rom> <output> [--recompress]");
            return ExitCodes.InvalidArguments;
        }

        if (!CheckDifferentFiles(args[0], args[1])
            || !CommandHelpers.TryLoadRom(args[0], out N64Rom? rom)
            || !TryReadFileSystem(rom, out GameFileSystem? fs))
        {
            return ExitCodes.InvalidArguments;
        }

        IReadOnlyList<GameFile> files = recompress
            ? fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList()
            : fs.Files;

        if (!TryBuild(rom, files, out RomBuildResult? result))
        {
            return ExitCodes.UnexpectedError;
        }

        result.Rom.Save(args[1]);

        if (recompress)
        {
            Console.WriteLine($"Recompressed and rebuilt {files.Count} files, saved to {args[1]}");
            Console.WriteLine($"File system size: {fs.TotalSize:N0} -> {files.Sum(f => f.Size):N0} bytes, " +
                              $"{result.FreeSpace:N0} bytes free before audio data");
            Console.WriteLine("Test this ROM in an emulator: it should play exactly like the original.");
            return ExitCodes.Success;
        }

        bool identical = result.Rom.Data.AsSpan().SequenceEqual(rom.Data);
        Console.WriteLine($"Rebuilt {fs.Files.Count} files, saved to {args[1]}");
        Console.WriteLine($"SHA-1 input : {rom.ComputeSha1()}");
        Console.WriteLine($"SHA-1 output: {result.Rom.ComputeSha1()}");

        ConsoleColor previous = Console.ForegroundColor;
        Console.ForegroundColor = identical ? ConsoleColor.Green : ConsoleColor.Yellow;
        Console.WriteLine(identical
            ? "Result is byte-identical to the input."
            : "Result differs from the input (expected only if the input was not a clean ROM).");
        Console.ForegroundColor = previous;

        return identical ? ExitCodes.Success : ExitCodes.VerificationFailed;
    }

    public static int Replace(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[1], out int index))
        {
            Console.Error.WriteLine("Usage: pw64cli fs-replace <rom> <table index> <new file> <output>");
            return ExitCodes.InvalidArguments;
        }

        string romPath = args[0], newFilePath = args[2], outputPath = args[3];

        if (!CheckDifferentFiles(romPath, outputPath)
            || !CommandHelpers.TryLoadRom(romPath, out N64Rom? rom)
            || !TryReadFileSystem(rom, out GameFileSystem? fs))
        {
            return ExitCodes.InvalidArguments;
        }

        WarnIfNotClean(rom);

        int position = fs.Files.ToList().FindIndex(f => f.TableIndex == index);
        if (position < 0)
        {
            Console.Error.WriteLine($"No file with table index {index}.");
            return ExitCodes.InvalidArguments;
        }

        byte[] newData;
        string newType;
        try
        {
            newData = File.ReadAllBytes(newFilePath);
            newType = IffForm.ReadHeader(newData).FormType;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Console.Error.WriteLine($"Cannot use replacement file: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }

        GameFile original = fs.Files[position];
        if (newType != original.FileType)
        {
            // Changing the type would shift the numbering of other files and break every
            // reference to them, so we refuse it here.
            Console.Error.WriteLine(
                $"Type mismatch: file {index} is '{original.FileType}', the replacement is '{newType}'.");
            return ExitCodes.InvalidArguments;
        }

        var files = fs.Files.ToList();
        files[position] = original with { Data = newData };

        if (!TryBuild(rom, files, out RomBuildResult? result))
        {
            return ExitCodes.UnexpectedError;
        }

        result.Rom.Save(outputPath);

        int delta = newData.Length - original.Size;
        Console.WriteLine($"Replaced file {index} ({original.FileType} #{original.GroupIndex}): " +
                          $"{original.Size:N0} -> {newData.Length:N0} bytes ({delta:+#,0;-#,0;0})");
        Console.WriteLine(result.TableRewritten
            ? "File table was rewritten (sizes changed)."
            : "File table unchanged (same size).");
        Console.WriteLine($"Free space before audio data: {result.FreeSpace:N0} bytes");
        Console.WriteLine($"Saved to {outputPath}");
        return ExitCodes.Success;
    }

    private static bool CheckDifferentFiles(string input, string output)
    {
        if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Output must be a different file than the input. The source ROM is never overwritten.");
            return false;
        }

        return true;
    }

    private static bool TryReadFileSystem(N64Rom rom, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out GameFileSystem? fs)
    {
        try
        {
            fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
            return true;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"Cannot read file system: {ex.Message}");
            fs = null;
            return false;
        }
    }

    private static bool TryBuild(N64Rom rom, IReadOnlyList<GameFile> files, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RomBuildResult? result)
    {
        try
        {
            result = RomBuilder.Build(rom, files, RomLayout.PilotwingsUsa);
            return true;
        }
        catch (Exception ex) when (ex is FileSystemFullException or InvalidDataException)
        {
            Console.Error.WriteLine($"Build failed: {ex.Message}");
            result = null;
            return false;
        }
    }

    private static void WarnIfNotClean(N64Rom rom)
    {
        RomVerificationResult verification = RomVerifier.Verify(rom);
        if (!verification.IsUsableAsBase)
        {
            Console.WriteLine($"Warning: {verification.Message}");
        }
    }
}
