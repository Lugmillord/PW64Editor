using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Missions;

/// <summary>
/// A kind of points, numbered as in the game's table of result rows (sResultPtTypes in
/// src/app/results.c). 0 is an empty row; 10 does not exist.
/// </summary>
public enum PointSource
{
    None = 0,
    LandingImpact = 1,
    LandingAccuracy = 2,
    Time = 3,
    Rings = 4,
    Balloons = 5,
    Altitude = 6,
    Targets = 7,
    MechaHawks = 8,
    HoverPads = 9,
    Photo = 11,
    CannonballTarget1 = 12,
    CannonballTarget2 = 13,
    CannonballTarget3 = 14,
    CannonballTarget4 = 15,
    Formation = 16,
}

/// <summary>Names and kinds of the point sources.</summary>
public static class PointSources
{
    /// <summary>True for a known source number.</summary>
    public static bool IsKnown(int value) => Enum.IsDefined(typeof(PointSource), value);

    /// <summary>The name of a source in the editor's language.</summary>
    public static string Name(PointSource source) => source switch
    {
        PointSource.None => CoreText.T("(empty)"),
        PointSource.LandingImpact => CoreText.T("Landing impact"),
        PointSource.LandingAccuracy => CoreText.T("Landing accuracy"),
        PointSource.Time => CoreText.T("Time"),
        PointSource.Rings => CoreText.T("Rings"),
        PointSource.Balloons => CoreText.T("Balloons"),
        PointSource.Altitude => CoreText.T("Altitude"),
        PointSource.Targets => CoreText.T("Targets"),
        PointSource.MechaHawks => "Mecha Hawks",
        PointSource.HoverPads => CoreText.T("Floating pads"),
        PointSource.Photo => CoreText.T("Photo"),
        PointSource.CannonballTarget1 => CoreText.F("Target {0}", 1),
        PointSource.CannonballTarget2 => CoreText.F("Target {0}", 2),
        PointSource.CannonballTarget3 => CoreText.F("Target {0}", 3),
        PointSource.CannonballTarget4 => CoreText.F("Target {0}", 4),
        PointSource.Formation => CoreText.T("Formation"),
        _ => CoreText.F("Unknown kind {0}", (int)source),
    };

    /// <summary>The score curve behind a source, or null for points from objects.</summary>
    public static ScoreCurveKind? Curve(PointSource source) => source switch
    {
        PointSource.LandingImpact => ScoreCurveKind.Impact,
        PointSource.LandingAccuracy => ScoreCurveKind.LandingAccuracy,
        PointSource.Time => ScoreCurveKind.Time,
        PointSource.Altitude => ScoreCurveKind.Altitude,
        _ => null,
    };
}
