using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Rom;

public class RomHeaderTests
{
    [Fact]
    public void Parse_ReadsAllFields()
    {
        byte[] data = SyntheticRom.Create(gameCode: "NPWE", version: 1, internalName: "Pilot Wings64");

        RomHeader header = RomHeader.Parse(data);

        Assert.Equal(0x80371240u, header.PiConfig);
        Assert.Equal(0x0000000Fu, header.ClockRate);
        Assert.Equal(0x80200050u, header.BootAddress);
        Assert.Equal(0x11223344u, header.Crc1);
        Assert.Equal(0x55667788u, header.Crc2);
        Assert.Equal("Pilot Wings64", header.InternalName); // trailing spaces trimmed
        Assert.Equal('N', header.MediaType);
        Assert.Equal("PW", header.CartridgeId);
        Assert.Equal('E', header.Region);
        Assert.Equal("NPWE", header.GameCode);
        Assert.Equal("1.1", header.VersionString);
        Assert.Equal("North America", header.RegionName);
    }

    [Fact]
    public void Parse_Throws_WhenDataIsShorterThanHeader()
    {
        Assert.Throws<ArgumentException>(() => RomHeader.Parse(new byte[RomHeader.Size - 1]));
    }
}
