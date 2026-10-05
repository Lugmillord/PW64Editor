using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Commands for the game's file system:
/// "fs-list" lists all files, "fs-extract" exports them, "fs-chunks" shows the inside of one file.
/// </summary>
internal static class FileSystemCommands
{
    public static int List(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli fs-list <rom>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadFileSystem(args[0], out GameFileSystem? fs))
        {
            return ExitCodes.InvalidRom;
        }

        Console.WriteLine("  Index  Type  Group  #     ROM offset   Size");
        foreach (GameFile file in fs.Files)
        {
            Console.WriteLine(
                $"  {file.TableIndex,5}  {file.FileType}  {file.Group,-5}  {file.GroupIndex,3}   0x{file.RomOffset:X6}  {file.Size,9:N0}");
        }

        Console.WriteLine();
        PrintSummary(fs);
        return ExitCodes.Success;
    }

    public static int Extract(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: pw64cli fs-extract <rom> <output folder>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadFileSystem(args[0], out GameFileSystem? fs))
        {
            return ExitCodes.InvalidRom;
        }

        string folder = args[1];
        Directory.CreateDirectory(folder);

        foreach (GameFile file in fs.Files)
        {
            File.WriteAllBytes(Path.Combine(folder, file.ExportName), file.Data);
        }

        Console.WriteLine($"Extracted {fs.Files.Count} files to {Path.GetFullPath(folder)}");
        return ExitCodes.Success;
    }

    public static int Chunks(string[] args)
    {
        if (args.Length != 2 || !int.TryParse(args[1], out int index))
        {
            Console.Error.WriteLine("Usage: pw64cli fs-chunks <rom> <table index>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadFileSystem(args[0], out GameFileSystem? fs))
        {
            return ExitCodes.InvalidRom;
        }

        GameFile? file = fs.Files.FirstOrDefault(f => f.TableIndex == index);
        if (file is null)
        {
            Console.Error.WriteLine($"No file with table index {index}.");
            return ExitCodes.InvalidArguments;
        }

        IffForm form = IffForm.Parse(file.Data);
        Console.WriteLine($"File {file.TableIndex}: FORM '{form.FormType}', {file.Size:N0} bytes at ROM 0x{file.RomOffset:X6}");
        Console.WriteLine($"Game lookup: {file.Group} #{file.GroupIndex}");
        Console.WriteLine();
        Console.WriteLine("  Offset   Tag   Size        Content");

        foreach (IffChunk chunk in form.Chunks)
        {
            string content = string.Empty;
            if (chunk.Tag == GzipChunk.Tag)
            {
                (string innerTag, byte[] inner) = GzipChunk.ReadChunkData(file.Data, chunk);
                content = $"compressed '{innerTag}', {inner.Length:N0} bytes unpacked";
            }

            Console.WriteLine($"  0x{chunk.Offset:X5}  {chunk.Tag}  {chunk.DataSize,9:N0}   {content}");
        }

        return ExitCodes.Success;
    }

    private static void PrintSummary(GameFileSystem fs)
    {
        Console.WriteLine($"{fs.Files.Count} files, {fs.TotalSize:N0} bytes");
        Console.WriteLine($"File system: 0x{fs.Layout.FileSystemOffset:X} - 0x{fs.EndOffset:X}, " +
                          $"{fs.FreeSpace:N0} bytes free before audio data at 0x{fs.Layout.FileSystemLimit:X}");
        if (fs.SkippedEntries > 0)
        {
            Console.WriteLine($"{fs.SkippedEntries} empty table entries skipped");
        }

        Console.WriteLine();
        Console.WriteLine("  Group  Files  Limit");
        foreach ((string group, int count, int limit) in fs.GetGroupUsage())
        {
            Console.WriteLine($"  {group,-5}  {count,5}  {limit,5}");
        }
    }
}
