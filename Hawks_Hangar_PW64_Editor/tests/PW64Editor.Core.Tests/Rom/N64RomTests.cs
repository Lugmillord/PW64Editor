using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Rom;

public class N64RomTests
{
    [Theory]
    [InlineData(RomByteOrder.BigEndian)]
    [InlineData(RomByteOrder.ByteSwapped)]
    [InlineData(RomByteOrder.LittleEndian)]
    public void FromBytes_NormalizesEveryLayoutToBigEndian(RomByteOrder diskOrder)
    {
        byte[] bigEndian = SyntheticRom.Create();
        byte[] onDisk = (byte[])bigEndian.Clone();
        ByteOrderConverter.ConvertFromBigEndian(onDisk, diskOrder);

        N64Rom rom = N64Rom.FromBytes(onDisk);

        Assert.Equal(diskOrder, rom.OriginalByteOrder);
        Assert.Equal(bigEndian, rom.Data);
        Assert.Equal("NPWE", rom.Header.GameCode);
    }

    [Fact]
    public void FromBytes_DoesNotModifyCallerBuffer()
    {
        byte[] onDisk = SyntheticRom.Create();
        ByteOrderConverter.ConvertFromBigEndian(onDisk, RomByteOrder.ByteSwapped);
        byte[] copy = (byte[])onDisk.Clone();

        N64Rom.FromBytes(onDisk);

        Assert.Equal(copy, onDisk);
    }

    [Fact]
    public void FromBytes_Throws_WhenTooSmall()
    {
        byte[] data = SyntheticRom.Create(size: N64Rom.MinimumSize - 4);
        Assert.Throws<InvalidRomException>(() => N64Rom.FromBytes(data));
    }

    [Fact]
    public void FromBytes_Throws_WhenSizeIsNotMultipleOfFour()
    {
        byte[] data = SyntheticRom.Create(size: N64Rom.MinimumSize + 2);
        Assert.Throws<InvalidRomException>(() => N64Rom.FromBytes(data));
    }

    [Fact]
    public void FromBytes_Throws_ForUnknownFormat()
    {
        byte[] data = new byte[N64Rom.MinimumSize]; // all zeros, no magic number
        Assert.Throws<InvalidRomException>(() => N64Rom.FromBytes(data));
    }

    [Fact]
    public void Save_And_Load_RoundTrip_InEveryLayout()
    {
        N64Rom original = N64Rom.FromBytes(SyntheticRom.Create());
        string tempFile = Path.GetTempFileName();

        try
        {
            foreach (RomByteOrder order in new[] { RomByteOrder.BigEndian, RomByteOrder.ByteSwapped, RomByteOrder.LittleEndian })
            {
                original.Save(tempFile, order);
                N64Rom reloaded = N64Rom.Load(tempFile);

                Assert.Equal(order, reloaded.OriginalByteOrder);
                Assert.Equal(original.Data, reloaded.Data);
            }
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
