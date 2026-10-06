using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Build;

public class AudioRelocationTests
{
    private static readonly RomLayout Layout = RomLayout.PilotwingsUsa;
    private static readonly AudioLayout Audio = AudioLayout.PilotwingsUsa;

    [RealRomFact]
    public void CleanRom_ContainsAllAudioReferencesWithOriginalValues()
    {
        // Verifies our table of code locations against the real ROM.
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        foreach ((MipsAddressReference reference, int original) in Audio.AllReferences)
        {
            Assert.Equal((uint)original, MipsAddressPatcher.ReadValue(rom.Data, reference));
        }
    }

    [RealRomFact]
    public void CleanRom_HasOnlyPaddingAfterAudioData()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        Assert.NotEqual(0xFF, rom.Data[Audio.EndOffset - 1]); // last byte of real data
        Assert.All(rom.Data[Audio.EndOffset..], b => Assert.Equal(0xFF, b));
    }

    [RealRomFact]
    public void GrowingAFile_MovesAudioBehindFileSystem()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        List<GameFile> files = GrowFirstMission(rom, extraBytes: 1024);

        RomBuildResult result = RomBuilder.Build(rom, files, Layout);

        Assert.True(result.AudioRelocated);
        Assert.False(result.Expanded);
        Assert.Equal(0, result.AudioOffset % AudioLayout.Alignment);
        Assert.True(result.AudioOffset >= result.FileSystemEnd);
        AssertAudioMovedCorrectly(rom, result);
    }

    [RealRomFact]
    public void RecompressedWithForcedRelocation_MovesAudioBackwards()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);
        var files = fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList();

        RomBuildResult result = RomBuilder.Build(rom, files, Layout, new RomBuildOptions(AlwaysRelocateAudio: true));

        Assert.True(result.AudioRelocated);
        Assert.True(result.AudioOffset < Audio.SequenceOffset);
        AssertAudioMovedCorrectly(rom, result);
    }

    [RealRomFact]
    public void UnchangedFiles_WithForcedRelocation_StayByteIdentical()
    {
        // The audio already sits directly behind the files (4 bytes gap, aligned to 16),
        // so "relocating" it means not moving it at all.
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);

        RomBuildResult result = RomBuilder.Build(rom, fs.Files, Layout, new RomBuildOptions(AlwaysRelocateAudio: true));

        Assert.False(result.AudioRelocated);
        Assert.Equal(rom.ComputeSha1(), result.Rom.ComputeSha1());
    }

    [RealRomFact]
    public void TooMuchData_WithoutExpansion_Throws()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        List<GameFile> files = GrowFirstMission(rom, extraBytes: 2 * 1024 * 1024);

        Assert.Throws<FileSystemFullException>(() => RomBuilder.Build(rom, files, Layout));
    }

    [RealRomFact]
    public void TooMuchData_WithExpansion_Creates16MiBRom()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        List<GameFile> files = GrowFirstMission(rom, extraBytes: 2 * 1024 * 1024);

        RomBuildResult result = RomBuilder.Build(rom, files, Layout, new RomBuildOptions(AllowExpansion: true));

        Assert.True(result.Expanded);
        Assert.Equal(16 * 1024 * 1024, result.Rom.Size);
        Assert.True(result.Rom.IsBootChecksumValid());
        AssertAudioMovedCorrectly(rom, result);
        Assert.All(result.Rom.Data[(result.AudioOffset + Audio.Size)..], b => Assert.Equal(0xFF, b));
    }

    [RealRomFact]
    public void FreeSpace_OfCleanRom_IsAboutOneMegabyte()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);

        RomBuildResult result = RomBuilder.Build(rom, fs.Files, Layout);

        Assert.InRange(result.FreeSpace, 1_000_000, 1_100_000);
    }

    /// <summary>Checks audio data, code references, the file system and the boot checksum.</summary>
    private static void AssertAudioMovedCorrectly(N64Rom original, RomBuildResult result)
    {
        byte[] data = result.Rom.Data;
        int delta = result.AudioOffset - Audio.SequenceOffset;

        // The audio block is an exact copy.
        Assert.Equal(
            original.Data[Audio.SequenceOffset..Audio.EndOffset],
            data[result.AudioOffset..(result.AudioOffset + Audio.Size)]);

        // Every code reference points to the new location.
        foreach ((MipsAddressReference reference, int originalAddress) in Audio.AllReferences)
        {
            Assert.Equal((uint)(originalAddress + delta), MipsAddressPatcher.ReadValue(data, reference));
        }

        // The file system is still readable and knows where the audio is.
        GameFileSystem fs = GameFileSystem.Read(result.Rom, Layout);
        Assert.Equal(result.AudioOffset, fs.AudioOffset);
        Assert.True(result.Rom.IsBootChecksumValid());
    }

    /// <summary>Adds a dummy "PAD " chunk to the first mission file to make it larger.</summary>
    private static List<GameFile> GrowFirstMission(N64Rom rom, int extraBytes)
    {
        GameFileSystem fs = GameFileSystem.Read(rom, Layout);
        var files = fs.Files.ToList();
        int position = files.FindIndex(f => f.FileType == "UPWT");
        GameFile mission = files[position];

        IffForm form = IffForm.Parse(mission.Data);
        var chunks = form.Chunks.Select(c => mission.Data[c.Offset..(c.Offset + c.TotalSize)]).ToList();
        chunks.Add(IffWriter.BuildChunk("PAD ", new byte[extraBytes]));
        files[position] = mission with { Data = IffWriter.BuildForm("UPWT", chunks) };
        return files;
    }
}
