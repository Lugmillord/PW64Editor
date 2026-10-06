using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Build;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Hashing;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Patching;

public class BpsTests
{
    /// <summary>
    /// A patch built by hand from the specification: source "ABC" -> target "ABCX".
    /// One SourceRead of 3 bytes, then one TargetRead of the literal "X".
    /// Tests the reader against the spec instead of against our own writer.
    /// </summary>
    [Fact]
    public void Apply_HandMadePatch_ProducesExpectedTarget()
    {
        byte[] source = Encoding.ASCII.GetBytes("ABC");
        byte[] target = Encoding.ASCII.GetBytes("ABCX");

        var patch = new List<byte>();
        patch.AddRange("BPS1"u8.ToArray());
        patch.Add(0x83);       // source size 3 (high bit = last byte of the number)
        patch.Add(0x84);       // target size 4
        patch.Add(0x80);       // no metadata
        patch.Add(0x88);       // ((3 - 1) << 2) | 0 = SourceRead, 3 bytes
        patch.Add(0x81);       // ((1 - 1) << 2) | 1 = TargetRead, 1 byte
        patch.Add((byte)'X');
        AppendFooter(patch, source, target);

        byte[] result = BpsReader.Apply(source, patch.ToArray());

        Assert.Equal(target, result);
    }

    public static TheoryData<string, byte[], byte[]> RoundTripCases()
    {
        byte[] random = RandomBytes(20_000, seed: 1);
        return new TheoryData<string, byte[], byte[]>
        {
            { "identical", random, random },
            { "one byte changed", random, WithByteChanged(random, 5_000) },
            { "insertion shifts data", random, [.. random[..1000], .. RandomBytes(37, 2), .. random[1000..]] },
            { "deletion shifts data", random, [.. random[..1000], .. random[1100..]] },
            { "target larger, run of zeros", random, [.. random, .. new byte[50_000]] },
            { "target smaller", random, random[..123] },
            { "completely different", random, RandomBytes(5_000, seed: 3) },
            { "empty target", random, [] },
            { "empty source", [], RandomBytes(100, seed: 4) },
        };
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void Create_ThenApply_ReproducesTarget(string name, byte[] source, byte[] target)
    {
        byte[] patch = BpsWriter.Create(source, target);

        byte[] result = BpsReader.Apply(source, patch);

        Assert.True(result.AsSpan().SequenceEqual(target), $"Case '{name}' failed.");
    }

    [Fact]
    public void Create_ShiftedData_GivesSmallPatch()
    {
        // The whole point of a delta patch: inserting 4 bytes must not produce a patch
        // containing everything after the insertion point.
        byte[] source = RandomBytes(100_000, seed: 5);
        byte[] target = [.. source[..10], 1, 2, 3, 4, .. source[10..]];

        byte[] patch = BpsWriter.Create(source, target);

        Assert.True(patch.Length < 64, $"Patch is {patch.Length} bytes.");
    }

    [Fact]
    public void ReadInfo_ReturnsSizesChecksumsAndMetadata()
    {
        byte[] source = RandomBytes(1000, seed: 6);
        byte[] target = WithByteChanged(source, 10);

        BpsPatchInfo info = BpsReader.ReadInfo(BpsWriter.Create(source, target, "Hawk's Hangar test hack v1.0"));

        Assert.Equal(1000, info.SourceSize);
        Assert.Equal(1000, info.TargetSize);
        Assert.Equal(Crc32.Compute(source), info.SourceCrc32);
        Assert.Equal(Crc32.Compute(target), info.TargetCrc32);
        Assert.Equal("Hawk's Hangar test hack v1.0", info.Metadata);
    }

    [Fact]
    public void Apply_Throws_WrongSource_ForDifferentInput()
    {
        byte[] source = RandomBytes(1000, seed: 7);
        byte[] patch = BpsWriter.Create(source, WithByteChanged(source, 1));

        var ex = Assert.Throws<BpsException>(() => BpsReader.Apply(WithByteChanged(source, 500), patch));

        Assert.Equal(BpsError.WrongSource, ex.Error);
    }

    [Fact]
    public void Apply_Throws_WrongSource_ForDifferentSize()
    {
        byte[] source = RandomBytes(1000, seed: 8);
        byte[] patch = BpsWriter.Create(source, WithByteChanged(source, 1));

        var ex = Assert.Throws<BpsException>(() => BpsReader.Apply(source[..999], patch));

        Assert.Equal(BpsError.WrongSource, ex.Error);
    }

    [Fact]
    public void Apply_Throws_PatchCorrupted_WhenPatchIsDamaged()
    {
        byte[] source = RandomBytes(1000, seed: 9);
        byte[] patch = BpsWriter.Create(source, RandomBytes(1000, seed: 10));
        patch[patch.Length / 2] ^= 0xFF;

        var ex = Assert.Throws<BpsException>(() => BpsReader.Apply(source, patch));

        Assert.Equal(BpsError.PatchCorrupted, ex.Error);
    }

    [Fact]
    public void ReadInfo_Throws_InvalidFormat_ForNonPatchFile()
    {
        var ex = Assert.Throws<BpsException>(() => BpsReader.ReadInfo(new byte[100]));

        Assert.Equal(BpsError.InvalidFormat, ex.Error);
    }

    [RealRomFact]
    public void RealRom_FullyRecompressedRebuild_PatchRoundTrips()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        GameFileSystem fs = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        var files = fs.Files.Select(f => f with { Data = IffWriter.RecompressForm(f.Data) }).ToList();
        byte[] modified = RomBuilder.Build(rom, files, RomLayout.PilotwingsUsa).Rom.Data;

        byte[] patch = BpsWriter.Create(rom.Data, modified);
        byte[] result = BpsReader.Apply(rom.Data, patch);

        Assert.Equal(modified, result);
        // Every compressed chunk changed, so ~2.6 MB of new data compresses to under 1.5 MB.
        Assert.True(patch.Length < 1_500_000, $"Patch is {patch.Length:N0} bytes.");
    }

    [RealRomFact]
    public void RealRom_SmallEdit_GivesTinyPatch()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        byte[] modified = (byte[])rom.Data.Clone();
        modified[0x342E2C + 0x100] ^= 0x01; // one byte inside a mission file

        byte[] patch = BpsWriter.Create(rom.Data, modified);

        Assert.True(patch.Length < 64, $"Patch is {patch.Length} bytes.");
        Assert.Equal(modified, BpsReader.Apply(rom.Data, patch));
    }

    private static void AppendFooter(List<byte> patch, byte[] source, byte[] target)
    {
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(source));
        patch.AddRange(crc);
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(target));
        patch.AddRange(crc);
        BinaryPrimitives.WriteUInt32LittleEndian(crc, Crc32.Compute(patch.ToArray()));
        patch.AddRange(crc);
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static byte[] WithByteChanged(byte[] data, int index)
    {
        byte[] copy = (byte[])data.Clone();
        copy[index] ^= 0xFF;
        return copy;
    }
}
