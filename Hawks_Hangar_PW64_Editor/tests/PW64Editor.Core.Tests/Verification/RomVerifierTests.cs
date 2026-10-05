using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Verification;

namespace PW64Editor.Core.Tests.Verification;

public class RomVerifierTests
{
    [Fact]
    public void Verify_ReportsModified_WhenHeaderMatchesButHashDoesNot()
    {
        // A synthetic ROM has the right header but obviously not the right content.
        N64Rom rom = N64Rom.FromBytes(SyntheticRom.Create(gameCode: "NPWE"));

        RomVerificationResult result = RomVerifier.Verify(rom);

        Assert.Equal(RomVerificationStatus.ModifiedOrBadDump, result.Status);
        Assert.False(result.IsUsableAsBase);
        Assert.Null(result.MatchedRom);
    }

    [Fact]
    public void Verify_ReportsNotPilotwings_ForOtherGames()
    {
        N64Rom rom = N64Rom.FromBytes(SyntheticRom.Create(gameCode: "NSME", internalName: "SUPER MARIO 64"));

        RomVerificationResult result = RomVerifier.Verify(rom);

        Assert.Equal(RomVerificationStatus.NotPilotwings, result.Status);
    }

    [Theory]
    [InlineData("NPWJ")] // Japan
    [InlineData("NPWP")] // Europe
    public void Verify_ReportsUnknownRelease_ForOtherRegions(string gameCode)
    {
        N64Rom rom = N64Rom.FromBytes(SyntheticRom.Create(gameCode: gameCode));

        RomVerificationResult result = RomVerifier.Verify(rom);

        Assert.Equal(RomVerificationStatus.UnknownRelease, result.Status);
    }

    [RealRomFact]
    public void Verify_AcceptsCleanUsaRom()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);

        RomVerificationResult result = RomVerifier.Verify(rom);

        Assert.Equal(RomVerificationStatus.CleanSupported, result.Status);
        Assert.Same(KnownRoms.PilotwingsUsa, result.MatchedRom);
        Assert.True(result.IsUsableAsBase);
    }

    [RealRomFact]
    public void Verify_ReportsModified_WhenASingleByteChanges()
    {
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        byte[] modified = (byte[])clean.Data.Clone();
        modified[0x500000] ^= 0xFF; // flip one byte somewhere in the asset area

        RomVerificationResult result = RomVerifier.Verify(N64Rom.FromBytes(modified));

        Assert.Equal(RomVerificationStatus.ModifiedOrBadDump, result.Status);
    }
}
