using System.Text;
using PW64Editor.Core.Compression;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Compression;

public class Mio0Tests
{
    /// <summary>
    /// A tiny hand-built MIO0 block that decodes to "ABCABCABCABC":
    /// three literals (A, B, C) followed by one back-reference "copy 9 bytes from 3 back".
    /// Built by hand so the decoder is tested against the format specification,
    /// not just against our own encoder.
    /// </summary>
    private static readonly byte[] HandMadeBlock =
    [
        0x4D, 0x49, 0x4F, 0x30, // "MIO0"
        0x00, 0x00, 0x00, 0x0C, // decompressed size = 12
        0x00, 0x00, 0x00, 0x14, // compressed stream at 0x14
        0x00, 0x00, 0x00, 0x16, // literal stream at 0x16
        0xE0, 0x00, 0x00, 0x00, // layout bits: 1,1,1,0 = literal, literal, literal, reference
        0x60, 0x02,             // reference: length (6 + 3) = 9, distance (2 + 1) = 3
        0x41, 0x42, 0x43,       // literals "ABC"
    ];

    [Fact]
    public void Decompress_HandMadeBlock_ProducesExpectedOutput()
    {
        byte[] result = Mio0.Decompress(HandMadeBlock, out int compressedLength);

        Assert.Equal("ABCABCABCABC", Encoding.ASCII.GetString(result));
        Assert.Equal(HandMadeBlock.Length, compressedLength);
    }

    [Fact]
    public void Decompress_IgnoresTrailingData()
    {
        byte[] withTrailing = [.. HandMadeBlock, 0xFF, 0xFF, 0xFF, 0xFF];

        byte[] result = Mio0.Decompress(withTrailing, out int compressedLength);

        Assert.Equal("ABCABCABCABC", Encoding.ASCII.GetString(result));
        Assert.Equal(HandMadeBlock.Length, compressedLength);
    }

    [Fact]
    public void Decompress_Throws_WithoutMagic()
    {
        byte[] data = new byte[32];
        Assert.Throws<InvalidDataException>(() => Mio0.Decompress(data));
    }

    [Fact]
    public void Decompress_Throws_WhenTruncated()
    {
        byte[] truncated = HandMadeBlock[..^2]; // cut off two literal bytes

        Assert.Throws<InvalidDataException>(() => Mio0.Decompress(truncated));
    }

    [Fact]
    public void Decompress_Throws_WhenReferencePointsBeforeStart()
    {
        // Layout bit 0 as the very first step: a reference with nothing to copy from yet.
        byte[] block =
        [
            0x4D, 0x49, 0x4F, 0x30,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x14,
            0x00, 0x00, 0x00, 0x16,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00,
        ];

        Assert.Throws<InvalidDataException>(() => Mio0.Decompress(block));
    }

    public static TheoryData<string, byte[]> RoundTripData => new()
    {
        { "empty", [] },
        { "single byte", [0x42] },
        { "two bytes", [0x01, 0x02] },
        { "run of zeros", new byte[10_000] },
        { "all byte values", Enumerable.Range(0, 256).Select(i => (byte)i).ToArray() },
        { "text", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Pilotwings 64 Hawk's Hangar ", 200))) },
        { "random", RandomBytes(50_000, seed: 1) },
        { "random with repeats beyond window", RepeatWithGap(seed: 2) },
    };

    [Theory]
    [MemberData(nameof(RoundTripData))]
    public void Compress_ThenDecompress_RestoresOriginal(string name, byte[] original)
    {
        byte[] compressed = Mio0.Compress(original);
        byte[] restored = Mio0.Decompress(compressed, out int compressedLength);

        Assert.Equal(original, restored);
        Assert.True(compressedLength <= compressed.Length, $"Block '{name}' reports more bytes than it has.");
    }

    [Fact]
    public void Compress_ProducesAlignedStreams()
    {
        byte[] compressed = Mio0.Compress(Encoding.ASCII.GetBytes("abcabcabcXYZabcabc12345"));
        Mio0Header header = Mio0Header.Parse(compressed);

        Assert.Equal(0, header.CompressedStreamOffset % 4);
        Assert.Equal(0, header.UncompressedStreamOffset % 4);
        Assert.Equal(0, compressed.Length % 4);
    }

    [Fact]
    public void Compress_ActuallyCompressesRepetitiveData()
    {
        byte[] zeros = new byte[10_000];

        byte[] compressed = Mio0.Compress(zeros);

        Assert.True(compressed.Length < 2_000, $"Expected strong compression, got {compressed.Length} bytes.");
    }

    [RealRomFact]
    public void AllBlocksInRom_DecompressAndRoundTrip()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        IReadOnlyList<Mio0BlockInfo> blocks = Mio0Scanner.FindAll(rom.Data);

        Assert.Equal(1322, blocks.Count);
        foreach (Mio0BlockInfo block in blocks)
        {
            byte[] original = Mio0.Decompress(rom.Data.AsSpan(block.Offset));
            byte[] restored = Mio0.Decompress(Mio0.Compress(original));
            Assert.Equal(original, restored);
        }
    }

    [RealRomFact]
    public void FileTableBlock_DecompressesToExpectedContent()
    {
        // The first MIO0 block in the ROM holds the compressed file table ("TABL").
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        byte[] table = Mio0.Decompress(rom.Data.AsSpan(0xDE754));

        Assert.Equal(0x27C0, table.Length);
        Assert.Equal("UVSY", Encoding.ASCII.GetString(table, 0, 4)); // first entry type
    }

    private static byte[] RandomBytes(int length, int seed)
    {
        byte[] data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    /// <summary>
    /// A random block, then 6000 other random bytes, then the first block again.
    /// The repeat is farther away than the 4096-byte window, so the compressor must
    /// not try to reference it.
    /// </summary>
    private static byte[] RepeatWithGap(int seed)
    {
        byte[] first = RandomBytes(1_000, seed);
        byte[] gap = RandomBytes(6_000, seed + 1);
        return [.. first, .. gap, .. first];
    }
}
