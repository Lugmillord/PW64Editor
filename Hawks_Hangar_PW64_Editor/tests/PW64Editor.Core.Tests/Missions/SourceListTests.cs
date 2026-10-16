using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Tests.Missions;

public class SourceListTests
{
    [Fact]
    public void JumbleHopper_OffersNoLandingSources()
    {
        IReadOnlyList<PointSource> sources = MissionCatalog.AvailableSources(Vehicle.JumbleHopper);
        Assert.DoesNotContain(PointSource.LandingAccuracy, sources);
        Assert.DoesNotContain(PointSource.LandingImpact, sources);
        Assert.Contains(PointSource.Time, sources);
        Assert.Contains(PointSource.LandingAccuracy, MissionCatalog.AvailableSources(Vehicle.HangGlider));
    }
}
