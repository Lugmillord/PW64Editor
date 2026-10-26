using PW64Editor.Core.Code;
using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Missions;

/// <summary>
/// What an island of the game needs, measured once (stage 8): the size of its files and, in flight, the heap and the display
/// objects that are no objects of the mission (the island's own moving objects, the vehicle, effects).
/// </summary>
/// <param name="Island">0 Holiday Island … 3 Ever-Frost Island.</param>
/// <param name="FilesUnpacked">Bytes of its models, terrain contours and textures, unpacked.</param>
/// <param name="FilesPacked">The same as stored in the ROM.</param>
/// <param name="HeapInFlight">Bytes of the heap in use while flying (Gyrocopter, measured in mupen64plus).</param>
/// <param name="DisplayObjects">Display objects in flight that are no objects of the mission.</param>
public sealed record IslandBudget(int Island, int FilesUnpacked, int FilesPacked, int HeapInFlight, int DisplayObjects)
{
    /// <summary>The heap of a hack with the Expansion Pak (up to <see cref="HighDataArea.Start"/>, without the reserved blocks).</summary>
    /// <remarks>(0x800DA800 − 0x8004181C) + (0x801F1000 − 0x80125800) + (0x803DA800 − 0x803905E0) + (0x80780000 − 0x80400000).</remarks>
    public const int Heap = 0x52EA04;

    /// <summary>The four islands of the game (Mission_Scoring.md 13.2, Level_Editor_Prep.md 3 and 8).</summary>
    public static IReadOnlyList<IslandBudget> Game { get; } =
    [
        new(0, 424 * 1024, 251 * 1024, 902 * 1024, 10),
        new(1, 657 * 1024, 423 * 1024, 1184 * 1024, 24),
        new(2, 1112 * 1024, 680 * 1024, 1729 * 1024, 30),
        new(3, 611 * 1024, 376 * 1024, 1122 * 1024, 42),
    ];

    /// <summary>The values of an island, or null for an unknown one.</summary>
    public static IslandBudget? Of(int island) => Game.FirstOrDefault(b => b.Island == island);
}

/// <summary>
/// The shared limits of a mission's objects (stage 8): the display objects (256 together with the island's own) and the
/// radar's waypoints (70). Going beyond them crashes nothing: the objects that do not fit are not drawn or not on the
/// radar. The numbers are estimates (one display object and one waypoint per ring, balloon, hover pad, target …).
/// </summary>
public static class MissionBudget
{
    /// <summary>Waypoints of the radar (HUDRadar.waypoints).</summary>
    public const int RadarWaypoints = 70;

    /// <summary>The mission's own display objects (estimate).</summary>
    public static int DisplayObjects(MissionComm comm) =>
        comm.GetObjectCount(MissionObjectKind.Ring) + comm.GetObjectCount(MissionObjectKind.Balloon)
        + comm.GetObjectCount(MissionObjectKind.HoverPad) + comm.GetObjectCount(MissionObjectKind.Target)
        + comm.GetObjectCount(MissionObjectKind.BallTarget) + comm.GetObjectCount(MissionObjectKind.HopperGoal)
        + comm.GetObjectCount(MissionObjectKind.LandingPad) + Math.Min(1, comm.GetObjectCount(MissionObjectKind.MecaHawk));

    /// <summary>The mission's objects on the radar (estimate).</summary>
    public static int Waypoints(MissionComm comm) =>
        comm.GetObjectCount(MissionObjectKind.Ring) + comm.GetObjectCount(MissionObjectKind.Balloon)
        + comm.GetObjectCount(MissionObjectKind.HoverPad) + comm.GetObjectCount(MissionObjectKind.Target)
        + Math.Min(1, comm.GetObjectCount(MissionObjectKind.MecaHawk));

    /// <summary>Notes about a mission that comes near or beyond the shared limits (empty if there is room).</summary>
    public static IReadOnlyList<string> Warnings(MissionComm comm)
    {
        var warnings = new List<string>();
        int own = DisplayObjects(comm);
        int island = IslandBudget.Of(comm.Island)?.DisplayObjects ?? IslandBudget.Game.Max(b => b.DisplayObjects);
        if (own + island > HighDataArea.DisplayObjects)
        {
            warnings.Add(CoreText.F("The mission's objects need about {0} display objects, the island about {1}; the game has {2}. The objects that do not fit are not drawn.",
                own, island, HighDataArea.DisplayObjects));
        }

        int waypoints = Waypoints(comm);
        if (waypoints > RadarWaypoints)
        {
            warnings.Add(CoreText.F("The radar has room for {0} of the mission's objects, the mission has {1}; the others are never shown on it.",
                RadarWaypoints, waypoints));
        }

        return warnings;
    }
}
