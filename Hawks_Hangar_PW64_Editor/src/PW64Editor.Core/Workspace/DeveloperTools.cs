using System.Text;
using PW64Editor.Core.Build;
using PW64Editor.Core.Compression;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Workspace;

/// <summary>
/// Research and testing functions, shared by the command line tool and the editor's
/// "Developer tools" menu.
/// </summary>
public static class DeveloperTools
{
    /// <summary>Lists all MIO0 blocks of a ROM as text.</summary>
    public static string DescribeMio0Blocks(N64Rom rom)
    {
        IReadOnlyList<Mio0BlockInfo> blocks = Mio0Scanner.FindAll(rom.Data);
        var text = new StringBuilder();

        text.AppendLine("  Offset    Compressed  Decompressed");
        foreach (Mio0BlockInfo block in blocks)
        {
            text.AppendLine($"  0x{block.Offset:X6}  {block.CompressedLength,10:N0}  {block.DecompressedSize,12:N0}");
        }

        text.AppendLine();
        text.AppendLine($"{blocks.Count} blocks, {blocks.Sum(b => (long)b.CompressedLength):N0} bytes compressed, " +
                        $"{blocks.Sum(b => (long)b.DecompressedSize):N0} bytes decompressed.");
        return text.ToString();
    }

    /// <summary>Describes the chunks inside one game file as text.</summary>
    /// <exception cref="ArgumentException">No file with this table index.</exception>
    public static string DescribeChunks(GameFileSystem fileSystem, int tableIndex)
    {
        GameFile file = fileSystem.Files.FirstOrDefault(f => f.TableIndex == tableIndex)
            ?? throw new ArgumentException($"No file with table index {tableIndex}.", nameof(tableIndex));

        IffForm form = IffForm.Parse(file.Data);
        var text = new StringBuilder();
        text.AppendLine($"File {file.TableIndex}: FORM '{form.FormType}', {file.Size:N0} bytes at ROM 0x{file.RomOffset:X6}");
        text.AppendLine($"Game lookup: {file.Group} #{file.GroupIndex}");
        text.AppendLine();
        text.AppendLine("  Offset   Tag   Size        Content");

        foreach (IffChunk chunk in form.Chunks)
        {
            string content = string.Empty;
            if (chunk.Tag == GzipChunk.Tag)
            {
                (string innerTag, byte[] inner) = GzipChunk.ReadChunkData(file.Data, chunk);
                content = $"compressed '{innerTag}', {inner.Length:N0} bytes unpacked";
            }

            text.AppendLine($"  0x{chunk.Offset:X5}  {chunk.Tag}  {chunk.DataSize,9:N0}   {content}");
        }

        return text.ToString();
    }

    /// <summary>
    /// Builds a test ROM from the clean ROM to stress the build pipeline.
    /// </summary>
    /// <param name="cleanRom">The clean ROM.</param>
    /// <param name="recompress">Recompress every file with our MIO0 compressor.</param>
    /// <param name="options">Build options (audio relocation, expansion).</param>
    /// <param name="dummyFileSize">If set, append an unused file of about this many bytes.</param>
    public static RomBuildResult BuildStressTest(N64Rom cleanRom, bool recompress, RomBuildOptions options, int? dummyFileSize)
    {
        GameFileSystem fs = GameFileSystem.Read(cleanRom, RomLayout.PilotwingsUsa);
        List<GameFile> files = recompress
            ? fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList()
            : fs.Files.ToList();

        if (dummyFileSize is { } size)
        {
            files.Add(CreateDummyFile(size));
        }

        return RomBuilder.Build(cleanRom, files, RomLayout.PilotwingsUsa, options);
    }

    /// <summary>
    /// Creates a file the game never loads: an unknown type ("DUMY") counts as a user file and
    /// is appended last, so it gets the highest user file index, which no game code requests.
    /// Every existing file keeps its index.
    /// </summary>
    public static GameFile CreateDummyFile(int approximateSize)
    {
        int payloadSize = Math.Max(4, approximateSize / 4 * 4); // keep the file size a multiple of 4
        byte[] data = IffWriter.BuildForm("DUMY", [IffWriter.BuildChunk("PAD ", new byte[payloadSize])]);
        return new GameFile(0, "DUMY", FileTypeLimits.UserFileGroup, 0, 0, data);
    }
}
