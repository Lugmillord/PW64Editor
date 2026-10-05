using PW64Editor.Core.Build;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Build;

public class RomBuilderTests
{
    private static readonly RomLayout TestLayout = new(
        FileTableOffset: 0x102000,
        FileSystemOffset: 0x103000,
        FileSystemLimit: 0x104000);

    private const int TestRomSize = 0x110000;

    [Fact]
    public void Build_WithSameFiles_KeepsTableBytes()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);

        RomBuildResult result = RomBuilder.Build(baseRom, fs.Files, TestLayout);

        Assert.False(result.TableRewritten);
        Assert.Equal(baseRom.Data, result.Rom.Data);
        Assert.False(result.BootChecksumUpdated); // synthetic ROM has no real boot code
    }

    [Fact]
    public void Build_WithResizedFile_RewritesTable_AndShiftsFollowingFiles()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        byte[] bigger = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[0x40]));
        var files = fs.Files.ToList();
        files[0] = files[0] with { Data = bigger };

        RomBuildResult result = RomBuilder.Build(baseRom, files, TestLayout);
        GameFileSystem reread = GameFileSystem.Read(result.Rom, TestLayout);

        Assert.True(result.TableRewritten);
        Assert.Equal(bigger, reread.Files[0].Data);
        Assert.Equal(fs.Files[1].Data, reread.Files[1].Data);
        Assert.Equal(TestLayout.FileSystemOffset + bigger.Length, reread.Files[1].RomOffset);
        Assert.Equal(reread.FreeSpace, result.FreeSpace);
    }

    [Fact]
    public void Build_ClearsSpaceLeftByShrinkingFiles()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        var files = fs.Files.Take(1).ToList(); // drop the second file entirely

        RomBuildResult result = RomBuilder.Build(baseRom, files, TestLayout);

        int end = TestLayout.FileSystemOffset + files[0].Size;
        Assert.Equal(end, result.FileSystemEnd);
        Assert.All(result.Rom.Data[end..TestLayout.FileSystemLimit], b => Assert.Equal(0, b));
    }

    [Fact]
    public void Build_DoesNotModifyBaseRom()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        byte[] before = (byte[])baseRom.Data.Clone();
        var files = fs.Files.ToList();
        files[0] = files[0] with { Data = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[0x40])) };

        RomBuilder.Build(baseRom, files, TestLayout);

        Assert.Equal(before, baseRom.Data);
    }

    [Fact]
    public void Build_Throws_WhenFilesDoNotFit()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        var files = fs.Files.ToList();
        files[0] = files[0] with { Data = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[0x2000])) };

        Assert.Throws<FileSystemFullException>(() => RomBuilder.Build(baseRom, files, TestLayout));
    }

    [Fact]
    public void Build_Throws_WhenFormTypeDoesNotMatchDeclaredType()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        var files = fs.Files.ToList();
        files[0] = files[0] with { Data = IffBuilder.Form("UVTX", IffBuilder.Chunk("COMM", new byte[8])) };

        Assert.Throws<InvalidDataException>(() => RomBuilder.Build(baseRom, files, TestLayout));
    }

    [Fact]
    public void Build_Throws_WhenGroupLimitIsExceeded()
    {
        N64Rom baseRom = CreateBaseRom(out GameFileSystem fs);
        byte[] system = IffBuilder.Form("UVSY", IffBuilder.Chunk("COMM", new byte[4]));
        var files = fs.Files.ToList();
        files.Add(new GameFile(0, "UVSY", "UVSY", 0, 0, system));
        files.Add(new GameFile(0, "UVSY", "UVSY", 0, 0, system)); // only one UVSY allowed

        Assert.Throws<InvalidDataException>(() => RomBuilder.Build(baseRom, files, TestLayout));
    }

    [RealRomFact]
    public void RealRom_UnchangedRebuild_IsByteIdentical()
    {
        // The key milestone: reading the file system and writing it back must reproduce
        // the original ROM exactly. This proves our understanding of the layout is complete.
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);

        RomBuildResult result = RomBuilder.Build(rom, fs.Files, RomLayout.PilotwingsUsa);

        Assert.False(result.TableRewritten);
        Assert.True(result.BootChecksumUpdated);
        Assert.Equal(rom.ComputeSha1(), result.Rom.ComputeSha1());
    }

    [RealRomFact]
    public void RealRom_FullyRecompressed_KeepsAllGameDataAndStaysBootable()
    {
        // Recompress every file with our own MIO0 compressor. Nearly every file changes size,
        // so this exercises table rewriting and shifting of the entire file system.
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        var files = fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList();

        RomBuildResult result = RomBuilder.Build(rom, files, RomLayout.PilotwingsUsa);
        GameFileSystem reread = GameFileSystem.Read(result.Rom, RomLayout.PilotwingsUsa);

        Assert.True(result.TableRewritten);
        Assert.True(result.Rom.IsBootChecksumValid());
        Assert.Equal(fs.Files.Count, reread.Files.Count);

        // Every chunk must decompress to exactly the original content.
        for (int i = 0; i < fs.Files.Count; i++)
        {
            List<(string Tag, byte[] Data)> before = ReadAllChunks(fs.Files[i].Data);
            List<(string Tag, byte[] Data)> after = ReadAllChunks(reread.Files[i].Data);

            Assert.Equal(before.Count, after.Count);
            for (int c = 0; c < before.Count; c++)
            {
                // Compare tag and bytes separately: tuples compare arrays by reference, not content.
                Assert.Equal(before[c].Tag, after[c].Tag);
                Assert.Equal(before[c].Data, after[c].Data);
            }
        }

        // Code and audio data must be untouched. The only exception is the boot checksum
        // in the header (0x10-0x17): the file table and the first files lie inside the
        // checksummed first MiB (0x1000-0x100FFF), so moving files changes it.
        Assert.Equal(rom.Data[0x18..RomLayout.PilotwingsUsa.FileTableOffset],
                     result.Rom.Data[0x18..RomLayout.PilotwingsUsa.FileTableOffset]);
        Assert.Equal(rom.Data[RomLayout.PilotwingsUsa.FileSystemLimit..],
                     result.Rom.Data[RomLayout.PilotwingsUsa.FileSystemLimit..]);
    }

    [RealRomFact]
    public void RealRom_GrowingAFile_FailsBecauseAudioFollowsDirectly()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        var files = fs.Files.ToList();
        GameFile mission = files.First(f => f.FileType == "UPWT");
        int position = files.IndexOf(mission);

        // Append a 16-byte dummy chunk: only 4 bytes are free before the audio data.
        IffForm form = IffForm.Parse(mission.Data);
        var chunks = form.Chunks.Select(c => mission.Data[c.Offset..(c.Offset + c.TotalSize)]).ToList();
        chunks.Add(IffWriter.BuildChunk("PAD ", new byte[8]));
        files[position] = mission with { Data = IffWriter.BuildForm("UPWT", chunks) };

        Assert.Throws<FileSystemFullException>(() => RomBuilder.Build(rom, files, RomLayout.PilotwingsUsa));
    }

    /// <summary>Returns (tag, decompressed data) of every chunk, for content comparisons.</summary>
    private static List<(string Tag, byte[] Data)> ReadAllChunks(byte[] formData)
    {
        return IffForm.Parse(formData).Chunks.Select(c => GzipChunk.ReadChunkData(formData, c)).ToList();
    }

    /// <summary>A synthetic ROM with a file table and two files in the test layout.</summary>
    private static N64Rom CreateBaseRom(out GameFileSystem fs)
    {
        byte[] model = IffBuilder.Form("UVMD", IffBuilder.Chunk("COMM", new byte[0x10]));
        byte[] mission = IffBuilder.Form("UPWT", IffBuilder.Chunk("COMM", new byte[0x20]));

        byte[] rom = SyntheticRom.Create(size: TestRomSize);
        byte[] table = IffBuilder.Form(FileTable.FormType,
            IffBuilder.Chunk("PAD ", new byte[4]),
            IffBuilder.Chunk("PAD ", new byte[4]),
            IffBuilder.GzipChunk(FileTable.TableTag, IffBuilder.TableEntries(("UVMD", model.Length), ("UPWT", mission.Length))));
        table.CopyTo(rom, TestLayout.FileTableOffset);
        model.CopyTo(rom, TestLayout.FileSystemOffset);
        mission.CopyTo(rom, TestLayout.FileSystemOffset + model.Length);

        // The rest of the file system area must be zero, like after a real build.
        rom.AsSpan(TestLayout.FileSystemOffset + model.Length + mission.Length,
                   TestLayout.FileSystemLimit - TestLayout.FileSystemOffset - model.Length - mission.Length).Clear();

        N64Rom baseRom = N64Rom.FromBytes(rom);
        fs = GameFileSystem.Read(baseRom, TestLayout);
        return baseRom;
    }
}
