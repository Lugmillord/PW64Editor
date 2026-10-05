using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Boot;

public class BootChecksumTests
{
    [Theory]
    [InlineData(0x0FFF, false)]  // last byte of IPL3, not covered
    [InlineData(0x1000, true)]   // first covered byte
    [InlineData(0x100FFF, true)] // last covered byte
    [InlineData(0x101000, false)] // first byte after the covered 1 MiB
    public void IsInChecksumRange_CoversExactlyOneMiBFrom0x1000(int offset, bool expected)
    {
        Assert.Equal(expected, BootChecksum.IsInChecksumRange(offset));
    }

    [Fact]
    public void Compute_Throws_ForUnknownCic()
    {
        byte[] data = SyntheticRom.Create();
        Assert.Throws<NotSupportedException>(() => BootChecksum.Compute(data, CicType.Unknown));
    }

    [Fact]
    public void WriteToHeader_ThenReadFromHeader_RoundTrips()
    {
        byte[] data = SyntheticRom.Create();
        var values = new BootChecksumValues(0xDEADBEEF, 0x12345678);

        BootChecksum.WriteToHeader(data, values);

        Assert.Equal(values, BootChecksum.ReadFromHeader(data));
    }

    [RealRomFact]
    public void CleanRom_HasValidChecksum()
    {
        // The strongest test: our algorithm must reproduce Nintendo's original values.
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        Assert.Equal(new BootChecksumValues(0xC851961C, 0x78FCAAFA), rom.ComputeBootChecksum());
        Assert.True(rom.IsBootChecksumValid());
    }

    [RealRomFact]
    public void ChangeInsideCoveredRange_InvalidatesChecksum_AndUpdateFixesIt()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        rom.Data[0x2000] ^= 0x01; // flip one bit in the game code area

        Assert.False(rom.IsBootChecksumValid());
        Assert.True(rom.UpdateBootChecksum());  // header was changed
        Assert.True(rom.IsBootChecksumValid());
        Assert.False(rom.UpdateBootChecksum()); // second call: nothing left to do
    }

    [RealRomFact]
    public void ChangeOutsideCoveredRange_KeepsChecksumValid()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        rom.Data[0x500000] ^= 0xFF; // asset area, beyond the first MiB

        Assert.True(rom.IsBootChecksumValid());
    }

    [RealRomFact]
    public void Header_ReflectsUpdatedChecksum()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        rom.Data[0x2000] ^= 0x01;

        rom.UpdateBootChecksum();

        BootChecksumValues expected = rom.ComputeBootChecksum();
        Assert.Equal(expected.Crc1, rom.Header.Crc1);
        Assert.Equal(expected.Crc2, rom.Header.Crc2);
    }
}
