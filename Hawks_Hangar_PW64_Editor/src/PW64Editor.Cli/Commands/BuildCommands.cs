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
        // Optional flags:
        // --recompress      recompress every file with our own MIO0 compressor. A stress test:
        //                   nearly every file moves, so the output proves in an emulator or on
        //                   hardware that the game accepts our compressed data.
        // --relocate-audio  always move the audio data directly behind the file system.
        //                   Combined with --recompress this really moves it, which tests the
        //                   code patches for the audio addresses.
        // --add-dummy <n> append an extra file of about n bytes that the game never loads.
        //                   Forces the audio to move (and with --expand the ROM to grow),
        //                   without changing anything the game actually uses.
        bool recompress = CommandHelpers.ExtractFlag(ref args, "--recompress");
        RomBuildOptions options = ParseBuildOptions(ref args);
        int? dummySize;
        try
        {
            dummySize = CommandHelpers.ExtractNumberOption(ref args, "--add-dummy");
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.InvalidArguments;
        }

        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli rebuild <rom> <output> [--recompress] [--relocate-audio] [--expand] [--add-dummy <bytes>]");
            return ExitCodes.InvalidArguments;
        }

        if (!CheckDifferentFiles(args[0], args[1])
            || !CommandHelpers.TryLoadRom(args[0], out N64Rom? rom)
            || !TryReadFileSystem(rom, out GameFileSystem? fs))
        {
            return ExitCodes.InvalidArguments;
        }

        List<GameFile> files = recompress
            ? fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList()
            : fs.Files.ToList();

        if (dummySize is { } size)
        {
            files.Add(CreateDummyFile(size));
        }

        if (!TryBuild(rom, files, options, out RomBuildResult? result))
        {
            return ExitCodes.BuildFailed;
        }

        result.Rom.Save(args[1]);

        if (recompress || dummySize is not null || options != RomBuildOptions.Default)
        {
            Console.WriteLine($"Rebuilt {files.Count} files{(recompress ? " (recompressed)" : string.Empty)}" +
                              $"{(dummySize is not null ? " including a dummy file" : string.Empty)}, saved to {args[1]}");
            Console.WriteLine($"File system size: {fs.TotalSize:N0} -> {files.Sum(f => f.Size):N0} bytes");
            PrintLayout(result);
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
        RomBuildOptions options = ParseBuildOptions(ref args);

        if (args.Length != 4 || !int.TryParse(args[1], out int index))
        {
            Console.Error.WriteLine("Usage: pw64cli fs-replace <rom> <table index> <new file> <output> [--relocate-audio] [--expand]");
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

        if (!TryBuild(rom, files, options, out RomBuildResult? result))
        {
            return ExitCodes.BuildFailed;
        }

        result.Rom.Save(outputPath);

        int delta = newData.Length - original.Size;
        Console.WriteLine($"Replaced file {index} ({original.FileType} #{original.GroupIndex}): " +
                          $"{original.Size:N0} -> {newData.Length:N0} bytes ({delta:+#,0;-#,0;0})");
        Console.WriteLine(result.TableRewritten
            ? "File table was rewritten (sizes changed)."
            : "File table unchanged (same size).");
        PrintLayout(result);
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

    /// <summary>
    /// Creates a file the game never loads: an unknown type ("DUMY") counts as a user file and
    /// is appended last, so it gets the highest user file index, which no game code requests.
    /// Every existing file keeps its index.
    /// </summary>
    private static GameFile CreateDummyFile(int approximateSize)
    {
        int payloadSize = Math.Max(4, approximateSize / 4 * 4); // keep the file size a multiple of 4
        byte[] data = IffWriter.BuildForm("DUMY", [IffWriter.BuildChunk("PAD ", new byte[payloadSize])]);
        return new GameFile(0, "DUMY", FileTypeLimits.UserFileGroup, 0, 0, data);
    }

    private static RomBuildOptions ParseBuildOptions(ref string[] args)
    {
        bool relocateAudio = CommandHelpers.ExtractFlag(ref args, "--relocate-audio");
        bool expand = CommandHelpers.ExtractFlag(ref args, "--expand");
        return new RomBuildOptions(AlwaysRelocateAudio: relocateAudio, AllowExpansion: expand);
    }

    private static void PrintLayout(RomBuildResult result)
    {
        Console.WriteLine($"File system ends at 0x{result.FileSystemEnd:X}, audio data at 0x{result.AudioOffset:X}" +
                          (result.AudioRelocated ? " (relocated)" : " (original position)"));
        Console.WriteLine($"ROM size: {result.Rom.Size / (1024 * 1024)} MiB{(result.Expanded ? " (expanded)" : string.Empty)}, " +
                          $"room for {result.FreeSpace:N0} more bytes of game files");
    }

    private static bool TryBuild(N64Rom rom, IReadOnlyList<GameFile> files, RomBuildOptions options, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RomBuildResult? result)
    {
        try
        {
            result = RomBuilder.Build(rom, files, RomLayout.PilotwingsUsa, options);
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
