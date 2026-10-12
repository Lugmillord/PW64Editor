using System.Buffers.Binary;
using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>A mission file to read: its place in the file system and its current content.</summary>
/// <param name="TableIndex">Index in the file table.</param>
/// <param name="FileNumber">Number among the user files.</param>
/// <param name="Data">The current content (project or original).</param>
/// <param name="FromProject">True if the content comes from the project.</param>
public sealed record MissionSource(int TableIndex, int FileNumber, byte[] Data, bool FromProject);

/// <summary>All missions of the game, and the mission files that could not be read.</summary>
public sealed record MissionCatalogResult(IReadOnlyList<MissionInfo> Missions, IReadOnlyList<string> Problems);

/// <summary>
/// Names, order and point values of the missions, as the Missions tab shows them.
/// </summary>
public static class MissionCatalog
{
    /// <summary>The vehicles in the order of the game's menus.</summary>
    public static IReadOnlyList<Vehicle> VehicleOrder { get; } =
    [
        Vehicle.HangGlider, Vehicle.RocketBelt, Vehicle.Gyrocopter,
        Vehicle.Cannonball, Vehicle.SkyDiving, Vehicle.JumbleHopper, Vehicle.Birdman,
    ];

    /// <summary>English names of the islands (game names, not translated).</summary>
    public static IReadOnlyList<string> IslandNames { get; } = ["Holiday Island", "Crescent Island", "Little States", "Ever-Frost Island"];

    /// <summary>Name of an island number.</summary>
    public static string IslandName(int island) =>
        island >= 0 && island < IslandNames.Count ? IslandNames[island] : CoreText.F("Island {0}", island);

    /// <summary>Name of a class: Beginner/Class A/… for the license vehicles, levels for the bonus games, stages for Birdman.</summary>
    public static string ClassName(Vehicle vehicle, int classIndex) => vehicle switch
    {
        _ when Vehicles.IsMain(vehicle) => Vehicles.ClassName(classIndex),
        Vehicle.Birdman => CoreText.F("Stage {0}", classIndex + 1),
        _ => CoreText.F("Level {0}", classIndex + 1),
    };

    /// <summary>Name of a test within its class (Cannonball: the targets).</summary>
    public static string TestName(Vehicle vehicle, int test) => vehicle == Vehicle.Cannonball
        ? CoreText.F("Target {0}", test + 1)
        : CoreText.F("Test {0}", test + 1);

    /// <summary>"Gyrocopter Beginner Test 1": vehicle, class and test.</summary>
    public static string Label(TestId id) => $"{Vehicles.Name(id.Vehicle)} {ClassName(id.Vehicle, id.Class)} {TestName(id.Vehicle, id.Test)}";

    /// <summary>
    /// The sources that can be chosen for a vehicle: those it can collect in the game (see
    /// Mission_Scoring.md). Cannonball and Birdman have none: Cannonball shows its four targets,
    /// Birdman has no score sheet.
    /// </summary>
    public static IReadOnlyList<PointSource> AvailableSources(Vehicle vehicle) => vehicle switch
    {
        Vehicle.HangGlider => [PointSource.Rings, PointSource.Balloons, PointSource.Photo, PointSource.Altitude, PointSource.Time,
            PointSource.LandingAccuracy, PointSource.LandingImpact],
        Vehicle.RocketBelt => [PointSource.Rings, PointSource.Balloons, PointSource.HoverPads, PointSource.Altitude, PointSource.Time,
            PointSource.LandingAccuracy, PointSource.LandingImpact],
        Vehicle.Gyrocopter => [PointSource.Rings, PointSource.Balloons, PointSource.Targets, PointSource.MecaHawks, PointSource.Altitude,
            PointSource.Time, PointSource.LandingAccuracy, PointSource.LandingImpact],
        Vehicle.SkyDiving => [PointSource.Formation, PointSource.Rings, PointSource.Altitude, PointSource.Time,
            PointSource.LandingAccuracy, PointSource.LandingImpact],

        // The Jumble Hopper does not measure an impact (it would always give the full points).
        Vehicle.JumbleHopper => [PointSource.Rings, PointSource.Balloons, PointSource.Altitude, PointSource.Time, PointSource.LandingAccuracy],
        _ => [],
    };

    /// <summary>
    /// Reads all missions and fills in texts and point rows. Missions that cannot be read are
    /// reported in <see cref="MissionCatalogResult.Problems"/>.
    /// </summary>
    /// <param name="sources">The mission files.</param>
    /// <param name="tables">The tables of the game code, or null if unknown.</param>
    /// <param name="settings">The project's settings of missions (changed result rows, generated texts), or null.</param>
    public static MissionCatalogResult Build(IEnumerable<MissionSource> sources, MissionTables? tables, IEnumerable<MissionSettings>? settings = null)
    {
        Dictionary<TestId, MissionSettings> settingsById = (settings ?? []).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last());
        var missions = new List<MissionInfo>();
        var problems = new List<string>();
        foreach (MissionSource source in sources)
        {
            try
            {
                missions.Add(new MissionInfo(new GameFileRef(source.TableIndex, source.FileNumber, source.FromProject), MissionFile.Parse(source.Data)));
            }
            catch (InvalidDataException ex)
            {
                problems.Add(CoreText.F("Mission file {0}: {1}", source.FileNumber, ex.Message));
            }
        }

        missions.Sort((a, b) => Order(a).CompareTo(Order(b)));
        Dictionary<TestId, MissionInfo> byId = missions.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());

        foreach (MissionInfo mission in missions)
        {
            if (tables is not null)
            {
                mission.MissionScreenTextName = tables.MissionScreenTextName(mission.Id);
                mission.ResultSheetTextName = tables.ResultSheetTextName(mission.Id);
                mission.ClassIsland = tables.ClassIsland(mission.Id.Vehicle, mission.Id.Class);
                mission.OriginalRowSources = tables.ResultRows(mission.Id)
                    .Where(r => r != 0)
                    .Reverse() // the table lists the bottom row first
                    .Select(r => (PointSource)r)
                    .ToList();
            }

            mission.Settings = settingsById.GetValueOrDefault(mission.Id);
            IReadOnlyList<PointSource> rows = mission.Settings?.ResultRows?.Select(r => (PointSource)r).ToList() ?? mission.OriginalRowSources;
            mission.Rows = rows.Select(r => Evaluate(r, mission.Id, mission.File, mission.File.Comm, id => byId.GetValueOrDefault(id)?.File.Comm)).ToList();
        }

        foreach (MissionInfo mission in missions)
        {
            mission.SharesMissionScreenTextWith = Others(missions, mission, m => m.MissionScreenTextName);
            mission.SharesResultSheetTextWith = Others(missions, mission, m => m.ResultSheetTextName);
            mission.SharesStatementWith = Others(missions, mission, m => m.TitleTextName);
        }

        return new MissionCatalogResult(missions, problems);
    }

    /// <summary>Sort key: vehicle in menu order, class, test.</summary>
    private static (int, int, int) Order(MissionInfo mission)
    {
        int vehicle = VehicleOrder.ToList().IndexOf(mission.Id.Vehicle);
        return (vehicle < 0 ? int.MaxValue : vehicle, mission.Id.Class, mission.Id.Test);
    }

    private static IReadOnlyList<TestId> Others(List<MissionInfo> missions, MissionInfo mission, Func<MissionInfo, string?> name)
    {
        string? own = name(mission);
        return own is null
            ? []
            : missions.Where(m => m != mission && name(m) == own).Select(m => m.Id).ToList();
    }

    /// <summary>
    /// The most points the mission data gives for a row.
    /// </summary>
    /// <param name="source">The kind of points.</param>
    /// <param name="id">The mission.</param>
    /// <param name="file">The mission file (for its objects).</param>
    /// <param name="comm">The COMM block to evaluate (may be an edited copy of the file's).</param>
    /// <param name="otherComm">The COMM block of another mission (Cannonball: the other targets of the level), or null.</param>
    public static MissionPointRow Evaluate(PointSource source, TestId id, MissionFile file, MissionComm comm, Func<TestId, MissionComm?> otherComm)
    {
        if (PointSources.Curve(source) is { } kind)
        {
            return new MissionPointRow(source, MissionCurves.ShownPoints(kind, id.Vehicle, comm.GetCurve(kind)), PointValueKind.Curve);
        }

        switch (source)
        {
            case PointSource.Rings:
                return new MissionPointRow(source, RingSum(file, comm), PointValueKind.AllObjects);

            case PointSource.Balloons:
                return new MissionPointRow(source, BalloonSum(file.GetObjectData(MissionObjectKind.Balloon)), PointValueKind.AllObjects);

            case PointSource.Targets:
                return new MissionPointRow(source, ByteSum(file.GetObjectData(MissionObjectKind.Target), 0x20, 0x19), PointValueKind.AllObjects);

            case PointSource.HoverPads:
                return new MissionPointRow(source, ByteSum(file.GetObjectData(MissionObjectKind.HoverPad), 0x40, 0x1D), PointValueKind.AllObjects);

            case >= PointSource.CannonballTarget1 and <= PointSource.CannonballTarget4:
            {
                // The score of target n is the score of test n of this level (best of its shots).
                var target = new TestId(id.Class, id.Vehicle, source - PointSource.CannonballTarget1);
                MissionComm? other = target == id ? comm : otherComm(target);
                return other is not null
                    ? new MissionPointRow(source, MissionCurves.ShownPoints(ScoreCurveKind.LandingAccuracy, id.Vehicle,
                        other.GetCurve(ScoreCurveKind.LandingAccuracy)), PointValueKind.OtherMission)
                    : new MissionPointRow(source, null, PointValueKind.None);
            }

            case PointSource.MecaHawks:
            case PointSource.Photo:
            case PointSource.Formation:
                return new MissionPointRow(source, null, PointValueKind.ComputedByGame);

            default:
                return new MissionPointRow(source, null, PointValueKind.None);
        }
    }

    /// <summary>The ring points of all rings for a COMM block (only rings of a type can be changed in the COMM block).</summary>
    public static int RingSum(MissionFile file, MissionComm comm)
    {
        ReadOnlySpan<byte> data = file.GetObjectData(MissionObjectKind.Ring);
        int sum = 0;
        for (int offset = 0; offset + 0x84 <= data.Length; offset += 0x84)
        {
            int type = Math.Min((int)data[offset + 0x72], MissionComm.RingTypeCount - 1);
            sum += Math.Max(comm.GetRingPoints(type), data[offset + 0x4C]);
        }

        return sum;
    }

    /// <summary>How many rings of each type (0-3) the mission has.</summary>
    public static int[] RingTypeCounts(MissionFile file)
    {
        ReadOnlySpan<byte> data = file.GetObjectData(MissionObjectKind.Ring);
        int[] counts = new int[MissionComm.RingTypeCount];
        for (int offset = 0; offset + 0x84 <= data.Length; offset += 0x84)
        {
            counts[Math.Min((int)data[offset + 0x72], MissionComm.RingTypeCount - 1)]++;
        }

        return counts;
    }

    /// <summary>
    /// Balloons (0x68 bytes each): points at 0x34; a balloon that splits (0x21) adds five smaller
    /// balloons with the points at 0x54 each (ballsLoad and balls_802CB9B4 in balls.c).
    /// </summary>
    private static int BalloonSum(ReadOnlySpan<byte> data)
    {
        int sum = 0;
        for (int offset = 0; offset + 0x68 <= data.Length; offset += 0x68)
        {
            sum += BinaryPrimitives.ReadInt32BigEndian(data[(offset + 0x34)..]);
            if (data[offset + 0x21] != 0)
            {
                sum += 5 * BinaryPrimitives.ReadInt32BigEndian(data[(offset + 0x54)..]);
            }
        }

        return sum;
    }

    private static int ByteSum(ReadOnlySpan<byte> data, int entrySize, int field)
    {
        int sum = 0;
        for (int offset = 0; offset + entrySize <= data.Length; offset += entrySize)
        {
            sum += data[offset + field];
        }

        return sum;
    }
}
