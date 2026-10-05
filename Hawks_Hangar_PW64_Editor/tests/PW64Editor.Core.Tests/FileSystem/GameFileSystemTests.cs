using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.FileSystem;

public class GameFileSystemTests
{
    // A miniature layout inside a synthetic ROM, so the reader can be tested without the game.
    private static readonly RomLayout TestLayout = new(
        FileTableOffset: 0x102000,
        FileSystemOffset: 0x103000,
        FileSystemLimit: 0x10F000);

    private const int TestRomSize = 0x110000;

    [Fact]
    public void Read_FindsFilesAndAssignsGroupIndices()
    {
        byte[] texture0 = IffBuilder.Form("UVTX", IffBuilder.Chunk("COMM", new byte[16]));
        byte[] mission = IffBuilder.Form("UPWT", IffBuilder.Chunk("COMM", new byte[8]));
        byte[] texture1 = IffBuilder.Form("UVTX", IffBuilder.Chunk("COMM", new byte[4]));
        byte[] pdat = IffBuilder.Form("PDAT", IffBuilder.Chunk("COMM", new byte[4]));

        N64Rom rom = BuildRom(texture0, mission, texture1, pdat);

        GameFileSystem fs = GameFileSystem.Read(rom, TestLayout);

        Assert.Equal(4, fs.Files.Count);
        Assert.Equal(("UVTX", 0, 0x103000), (fs.Files[0].Group, fs.Files[0].GroupIndex, fs.Files[0].RomOffset));
        Assert.Equal(("user", 0), (fs.Files[1].Group, fs.Files[1].GroupIndex));
        Assert.Equal(("UVTX", 1), (fs.Files[2].Group, fs.Files[2].GroupIndex));
        Assert.Equal(("user", 1), (fs.Files[3].Group, fs.Files[3].GroupIndex)); // shares the user counter
        Assert.Equal(0x103000 + texture0.Length + mission.Length, fs.Files[2].RomOffset);
        Assert.Equal(texture1, fs.Files[2].Data);
        Assert.Same(fs.Files[2], fs.Find("UVTX", 1));
        Assert.Equal(fs.Layout.FileSystemLimit - fs.EndOffset, fs.FreeSpace);
    }

    [Fact]
    public void Read_SkipsZeroTagEntries_ButAdvancesOffset()
    {
        byte[] file = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[8]));
        byte[] tableData = IffBuilder.TableEntries((null, 0x20), ("UVMD", file.Length));
        byte[] rom = SyntheticRom.Create(size: TestRomSize);
        WriteTable(rom, tableData);
        file.CopyTo(rom, TestLayout.FileSystemOffset + 0x20);

        GameFileSystem fs = GameFileSystem.Read(N64Rom.FromBytes(rom), TestLayout);

        Assert.Equal(1, fs.SkippedEntries);
        GameFile single = Assert.Single(fs.Files);
        Assert.Equal(1, single.TableIndex);
        Assert.Equal(TestLayout.FileSystemOffset + 0x20, single.RomOffset);
    }

    [Fact]
    public void Read_Throws_WhenTableTypeDoesNotMatchForm()
    {
        byte[] file = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[8]));
        byte[] rom = SyntheticRom.Create(size: TestRomSize);
        WriteTable(rom, IffBuilder.TableEntries(("UVTX", file.Length)));
        file.CopyTo(rom, TestLayout.FileSystemOffset);

        Assert.Throws<InvalidDataException>(() => GameFileSystem.Read(N64Rom.FromBytes(rom), TestLayout));
    }

    [Fact]
    public void Read_Throws_WhenTableSizeDoesNotMatchForm()
    {
        byte[] file = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[8]));
        byte[] rom = SyntheticRom.Create(size: TestRomSize);
        WriteTable(rom, IffBuilder.TableEntries(("UVMD", file.Length + 4)));
        file.CopyTo(rom, TestLayout.FileSystemOffset);

        Assert.Throws<InvalidDataException>(() => GameFileSystem.Read(N64Rom.FromBytes(rom), TestLayout));
    }

    [Fact]
    public void FileTypeLimits_ClassifyKernelAndUserTypes()
    {
        Assert.Equal("UVTX", FileTypeLimits.GetGroup("UVTX"));
        Assert.Equal(FileTypeLimits.UserFileGroup, FileTypeLimits.GetGroup("UPWT"));
        Assert.Equal(0x1F4, FileTypeLimits.GetLimit("UVTX"));
        Assert.Equal(0x80, FileTypeLimits.GetLimit(FileTypeLimits.UserFileGroup));
    }

    [RealRomFact]
    public void RealRom_HasExpectedLayout()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        Assert.Equal(1272, fs.Files.Count);
        Assert.Equal(0, fs.SkippedEntries);
        Assert.Equal(0x618B6C, fs.EndOffset);
        Assert.Equal(4, fs.FreeSpace);
        Assert.Equal("UVSY", fs.Files[0].FileType);
        Assert.Equal(72, fs.Files[0].Size);
        Assert.Equal(61, fs.Files.Count(f => f.FileType == "UPWT"));
    }

    [RealRomFact]
    public void RealRom_StaysWithinAllGameLimits()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);

        foreach ((string group, int count, int limit) in fs.GetGroupUsage())
        {
            Assert.True(count <= limit, $"Group {group} has {count} files, limit is {limit}.");
        }
    }

    [RealRomFact]
    public void RealRom_EveryFileParses_AndEveryCompressedChunkDecompresses()
    {
        GameFileSystem fs = GameFileSystem.Read(N64Rom.Load(TestRomLocator.RomPath!), RomLayout.PilotwingsUsa);
        int compressedChunks = 0;

        foreach (GameFile file in fs.Files)
        {
            IffForm form = IffForm.Parse(file.Data);
            Assert.Equal(file.Size, form.TotalSize);

            foreach (IffChunk chunk in form.Chunks.Where(c => c.Tag == GzipChunk.Tag))
            {
                GzipChunk.ReadChunkData(file.Data, chunk);
                compressedChunks++;
            }
        }

        // 1322 MIO0 blocks in the ROM, minus the one holding the file table itself.
        Assert.Equal(1321, compressedChunks);
    }

    private static N64Rom BuildRom(params byte[][] files)
    {
        byte[] rom = SyntheticRom.Create(size: TestRomSize);
        var entries = files.Select(f => ((string?)IffForm.ReadHeader(f).FormType, f.Length)).ToArray();
        WriteTable(rom, IffBuilder.TableEntries(entries));

        int offset = TestLayout.FileSystemOffset;
        foreach (byte[] file in files)
        {
            file.CopyTo(rom, offset);
            offset += file.Length;
        }

        return N64Rom.FromBytes(rom);
    }

    /// <summary>Writes a file table FORM (structured like the real one) at the test layout's address.</summary>
    private static void WriteTable(byte[] rom, byte[] tableEntries)
    {
        byte[] tableForm = IffBuilder.Form(FileTable.FormType,
            IffBuilder.Chunk("PAD ", new byte[4]),
            IffBuilder.Chunk("PAD ", new byte[4]),
            IffBuilder.GzipChunk(FileTable.TableTag, tableEntries));
        tableForm.CopyTo(rom, TestLayout.FileTableOffset);
    }
}
