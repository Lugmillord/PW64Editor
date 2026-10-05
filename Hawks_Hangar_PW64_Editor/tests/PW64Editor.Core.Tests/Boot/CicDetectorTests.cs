using PW64Editor.Core.Boot;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Boot;

public class CicDetectorTests
{
    [Fact]
    public void Detect_ReturnsUnknown_ForSyntheticBootCode()
    {
        // The synthetic ROM contains a counting pattern instead of real IPL3 code.
        byte[] data = SyntheticRom.Create();

        Assert.Equal(CicType.Unknown, CicDetector.Detect(data));
    }

    [Fact]
    public void Detect_ReturnsUnknown_ForTooShortData()
    {
        Assert.Equal(CicType.Unknown, CicDetector.Detect(new byte[0x800]));
    }

    [RealRomFact]
    public void Detect_FindsCic6102_ForPilotwings()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        Assert.Equal(CicType.Cic6102, rom.DetectCic());
    }
}
