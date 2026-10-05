using PW64Editor.Core.Compression;
using PW64Editor.Core.Rom;

namespace PW64Editor.Cli.Commands;

/// <summary>
/// Research commands for MIO0-compressed data:
/// "mio0-scan" lists all blocks, "mio0-extract" decompresses one block to a file.
/// </summary>
internal static class Mio0Commands
{
    public static int Scan(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Usage: pw64cli mio0-scan <rom>");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadRom(args[0], out N64Rom? rom))
        {
            return ExitCodes.InvalidRom;
        }

        IReadOnlyList<Mio0BlockInfo> blocks = Mio0Scanner.FindAll(rom.Data);

        Console.WriteLine("  Offset    Compressed  Decompressed");
        foreach (Mio0BlockInfo block in blocks)
        {
            Console.WriteLine($"  0x{block.Offset:X6}  {block.CompressedLength,10:N0}  {block.DecompressedSize,12:N0}");
        }

        long totalCompressed = blocks.Sum(b => (long)b.CompressedLength);
        long totalDecompressed = blocks.Sum(b => (long)b.DecompressedSize);
        Console.WriteLine();
        Console.WriteLine($"{blocks.Count} blocks, {totalCompressed:N0} bytes compressed, {totalDecompressed:N0} bytes decompressed.");
        return ExitCodes.Success;
    }

    public static int Extract(string[] args)
    {
        if (args.Length != 3 || !HexParser.TryParseOffset(args[1], out int offset))
        {
            Console.Error.WriteLine("Usage: pw64cli mio0-extract <rom> <offset> <output>");
            Console.Error.WriteLine("       offset in hex (0xDE754) or decimal");
            return ExitCodes.InvalidArguments;
        }

        if (!CommandHelpers.TryLoadRom(args[0], out N64Rom? rom))
        {
            return ExitCodes.InvalidRom;
        }

        if (offset < 0 || offset >= rom.Size)
        {
            Console.Error.WriteLine($"Offset 0x{offset:X} is outside the ROM.");
            return ExitCodes.InvalidArguments;
        }

        byte[] data;
        int compressedLength;
        try
        {
            data = Mio0.Decompress(rom.Data.AsSpan(offset), out compressedLength);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"No valid MIO0 block at 0x{offset:X}: {ex.Message}");
            return ExitCodes.InvalidRom;
        }

        File.WriteAllBytes(args[2], data);
        Console.WriteLine($"Decompressed {compressedLength:N0} -> {data.Length:N0} bytes, saved to {args[2]}");
        return ExitCodes.Success;
    }
}
