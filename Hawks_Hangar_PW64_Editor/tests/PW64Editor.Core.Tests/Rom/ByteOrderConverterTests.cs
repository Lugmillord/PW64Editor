using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Tests.Rom;

public class ByteOrderConverterTests
{
    // The first word of a ROM in each of the three layouts.
    private static readonly byte[] BigEndianStart = [0x80, 0x37, 0x12, 0x40];
    private static readonly byte[] ByteSwappedStart = [0x37, 0x80, 0x40, 0x12];
    private static readonly byte[] LittleEndianStart = [0x40, 0x12, 0x37, 0x80];

    [Fact]
    public void Detect_RecognizesAllThreeLayouts()
    {
        Assert.Equal(RomByteOrder.BigEndian, ByteOrderConverter.Detect(BigEndianStart));
        Assert.Equal(RomByteOrder.ByteSwapped, ByteOrderConverter.Detect(ByteSwappedStart));
        Assert.Equal(RomByteOrder.LittleEndian, ByteOrderConverter.Detect(LittleEndianStart));
    }

    [Fact]
    public void Detect_ReturnsUnknown_ForUnrelatedData()
    {
        Assert.Equal(RomByteOrder.Unknown, ByteOrderConverter.Detect(new byte[] { 0x12, 0x34, 0x56, 0x78 }));
    }

    [Fact]
    public void Detect_ReturnsUnknown_ForTooShortData()
    {
        Assert.Equal(RomByteOrder.Unknown, ByteOrderConverter.Detect(new byte[] { 0x80, 0x37 }));
    }

    [Fact]
    public void ConvertToBigEndian_FromByteSwapped_SwapsEvery16BitWord()
    {
        byte[] data = [0x37, 0x80, 0x40, 0x12, 0xBB, 0xAA, 0xDD, 0xCC];

        ByteOrderConverter.ConvertToBigEndian(data, RomByteOrder.ByteSwapped);

        Assert.Equal(new byte[] { 0x80, 0x37, 0x12, 0x40, 0xAA, 0xBB, 0xCC, 0xDD }, data);
    }

    [Fact]
    public void ConvertToBigEndian_FromLittleEndian_ReversesEvery32BitWord()
    {
        byte[] data = [0x40, 0x12, 0x37, 0x80, 0xDD, 0xCC, 0xBB, 0xAA];

        ByteOrderConverter.ConvertToBigEndian(data, RomByteOrder.LittleEndian);

        Assert.Equal(new byte[] { 0x80, 0x37, 0x12, 0x40, 0xAA, 0xBB, 0xCC, 0xDD }, data);
    }

    [Theory]
    [InlineData(RomByteOrder.BigEndian)]
    [InlineData(RomByteOrder.ByteSwapped)]
    [InlineData(RomByteOrder.LittleEndian)]
    public void ConvertFromBigEndian_ThenBack_RestoresOriginal(RomByteOrder order)
    {
        byte[] original = [0x80, 0x37, 0x12, 0x40, 0x01, 0x02, 0x03, 0x04];
        byte[] data = (byte[])original.Clone();

        ByteOrderConverter.ConvertFromBigEndian(data, order);
        ByteOrderConverter.ConvertToBigEndian(data, order);

        Assert.Equal(original, data);
    }

    [Fact]
    public void ConvertToBigEndian_Throws_ForUnknownOrder()
    {
        byte[] data = new byte[4];
        Assert.Throws<ArgumentException>(() => ByteOrderConverter.ConvertToBigEndian(data, RomByteOrder.Unknown));
    }
}
