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

    /// <summary>
    /// Reads all missions and fills in texts and point rows. Missions that cannot be read are
    /// reported in <see cref="MissionCatalogResult.Problems"/>.
    /// </summary>
    /// <param name="sources">The mission files.</param>
    /// <param name="tables">The tables of the game code, or null if unknown.</param>
    public static MissionCatalogResult Build(IEnumerable<MissionSource> sources, MissionTables? tables)
    {
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
                mission.Rows = tables.ResultRows(mission.Id)
                    .Where(r => r != 0)
                    .Reverse() // the table lists the bottom row first
                    .Select(r => Row((PointSource)r, mission, byId))
                    .ToList();
            }
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

    /// <summary>The most points the mission data gives for a row.</summary>
    private static MissionPointRow Row(PointSource source, MissionInfo mission, Dictionary<TestId, MissionInfo> byId)
    {
        MissionFile file = mission.File;
        MissionComm comm = file.Comm;
        switch (source)
        {
            case PointSource.LandingAccuracy:
            {
                float max = comm.GetCurve(ScoreCurveKind.LandingAccuracy).Maximum;

                // The Gyrocopter multiplies two landing measurements that share this curve.
                int value = mission.Id.Vehicle == Vehicle.Gyrocopter ? (int)((max * max) + 0.5f) : (int)(max + 0.001f);
                return new MissionPointRow(source, value, PointValueKind.Curve);
            }

            case PointSource.LandingImpact:
            case PointSource.Altitude:
                return new MissionPointRow(source, (int)(comm.GetCurve(PointSources.Curve(source)!.Value).Maximum + 0.001f), PointValueKind.Curve);

            case PointSource.Time:
                // The game adds no rounding help to the time points.
                return new MissionPointRow(source, (int)comm.GetCurve(ScoreCurveKind.Time).Maximum, PointValueKind.Curve);

            case PointSource.Rings:
                return new MissionPointRow(source, RingSum(file), PointValueKind.AllObjects);

            case PointSource.Balloons:
                return new MissionPointRow(source, BalloonSum(file.GetObjectData(MissionObjectKind.Balloon)), PointValueKind.AllObjects);

            case PointSource.Targets:
                return new MissionPointRow(source, ByteSum(file.GetObjectData(MissionObjectKind.Target), 0x20, 0x19), PointValueKind.AllObjects);

            case PointSource.HoverPads:
                return new MissionPointRow(source, ByteSum(file.GetObjectData(MissionObjectKind.HoverPad), 0x40, 0x1D), PointValueKind.AllObjects);

            case >= PointSource.CannonballTarget1 and <= PointSource.CannonballTarget4:
            {
                // The score of target n is the score of test n of this level (best of its shots).
                var target = new TestId(mission.Id.Class, mission.Id.Vehicle, source - PointSource.CannonballTarget1);
                return byId.TryGetValue(target, out MissionInfo? other)
                    ? new MissionPointRow(source, (int)(other.File.Comm.GetCurve(ScoreCurveKind.LandingAccuracy).Maximum + 0.001f), PointValueKind.OtherMission)
                    : new MissionPointRow(source, null, PointValueKind.None);
            }

            case PointSource.MechaHawks:
            case PointSource.Photo:
            case PointSource.Formation:
                return new MissionPointRow(source, null, PointValueKind.ComputedByGame);

            default:
                return new MissionPointRow(source, null, PointValueKind.None);
        }
    }

    /// <summary>
    /// Rings (0x84 bytes each): a ring gives the points of its type (subtype at 0x72, 3 = goal ring),
    /// a timed ring passed in time its own points (0x4C) instead (ringsGetPoints in rings.c).
    /// </summary>
    private static int RingSum(MissionFile file)
    {
        ReadOnlySpan<byte> data = file.GetObjectData(MissionObjectKind.Ring);
        int sum = 0;
        for (int offset = 0; offset + 0x84 <= data.Length; offset += 0x84)
        {
            int type = Math.Min((int)data[offset + 0x72], MissionComm.RingTypeCount - 1);
            sum += Math.Max(file.Comm.GetRingPoints(type), data[offset + 0x4C]);
        }

        return sum;
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
