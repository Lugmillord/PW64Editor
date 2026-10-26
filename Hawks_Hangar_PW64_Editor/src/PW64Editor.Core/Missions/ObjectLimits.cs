using PW64Editor.Core.Localization;

namespace PW64Editor.Core.Missions;

/// <summary>A list of the game with room for a fixed number of a mission's objects of one type (stage 8).</summary>
/// <param name="Kind">The object type.</param>
/// <param name="GameLimit">How many the game has room for.</param>
/// <param name="Cap">How many a project can raise the limit to (0 = the limit cannot be raised).</param>
/// <param name="ElementSize">Bytes per object in the game's list (0 for lists that cannot be raised).</param>
/// <param name="GameAddress">RAM address of the game's list (0 for lists that cannot be raised).</param>
public sealed record ObjectList(MissionObjectKind Kind, int GameLimit, int Cap, int ElementSize, uint GameAddress)
{
    /// <summary>True if a project can raise the limit.</summary>
    public bool CanRaise => Cap > GameLimit;
}

/// <summary>
/// How many objects of each type a mission can have (stage 8). The game keeps the objects of a type in a list of fixed
/// size (rings.c, balls.c …); a mission with more loads none of that type ("too many … defined in level"). A project can
/// raise the limits of six types up to their caps, in steps of at most <see cref="MaxStep"/>; the build then gives the game
/// lists of exactly that size in the high data area (<see cref="Code.HighDataArea"/>). Thermals stop at 20: the radar copies
/// all of them into its own list of 20 (HUDRadar.therms, hud.c).
/// </summary>
/// <remarks>Names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</remarks>
public static class ObjectLimits
{
    /// <summary>The most a limit can be raised at once.</summary>
    public const int MaxStep = 10;

    /// <summary>The lists of the game, in the order the build lays out the raised ones.</summary>
    public static IReadOnlyList<ObjectList> Lists { get; } =
    [
        new(MissionObjectKind.Ring, 30, 64, 0x1CC, 0x8036DA78),        // gRings
        new(MissionObjectKind.Balloon, 20, 40, 0x1AC, 0x80357218),     // gBalls (with the balloons that split off)
        new(MissionObjectKind.HoverPad, 20, 40, 0x70, 0x80369588),     // gHoverPads
        new(MissionObjectKind.Target, 40, 64, 0x4C, 0x80378CF8),       // sMissileTargets
        new(MissionObjectKind.Thermal, 16, 20, 0x18, 0x8037AB58),      // gThermals
        new(MissionObjectKind.LocalWind, 6, 16, 0xA8, 0x8037F518),     // sLocalWinds
        new(MissionObjectKind.TakeoffPad, 3, 0, 0, 0),                 // sTakeoffPads
        new(MissionObjectKind.LandingPad, 14, 0, 0, 0),                // gLandingPads
        new(MissionObjectKind.Runway, 2, 0, 0, 0),                     // gLandingStrips
        new(MissionObjectKind.BallTarget, 5, 0, 0, 0),                 // sBallTgts
        new(MissionObjectKind.HopperGoal, 5, 0, 0, 0),                 // D_80368B80
        new(MissionObjectKind.MecaHawk, 12, 0, 0, 0),                  // sFalcoDomains
        new(MissionObjectKind.CannonTarget, 1, 0, 0, 0),               // sCannonTargets
    ];

    /// <summary>The name of an object type in the editor's language ("rings").</summary>
    public static string Name(MissionObjectKind kind) => kind switch
    {
        MissionObjectKind.Ring => CoreText.T("rings"),
        MissionObjectKind.Balloon => CoreText.T("balloons"),
        MissionObjectKind.HoverPad => CoreText.T("hover pads"),
        MissionObjectKind.Target => CoreText.T("missile targets"),
        MissionObjectKind.Thermal => CoreText.T("thermals"),
        MissionObjectKind.LocalWind => CoreText.T("local winds"),
        MissionObjectKind.TakeoffPad => CoreText.T("takeoff pads"),
        MissionObjectKind.LandingPad => CoreText.T("landing pads"),
        MissionObjectKind.Runway => CoreText.T("runways"),
        MissionObjectKind.BallTarget => CoreText.T("ball targets"),
        MissionObjectKind.HopperGoal => CoreText.T("hopper goals"),
        MissionObjectKind.MecaHawk => CoreText.T("Meca Hawk areas"),
        MissionObjectKind.CannonTarget => CoreText.T("cannon targets"),
        _ => kind.ToString(),
    };

    /// <summary>The list of a type, or null if the game has none for it (no limit but the object memory).</summary>
    public static ObjectList? Of(MissionObjectKind kind) => Lists.FirstOrDefault(l => l.Kind == kind);

    /// <summary>The limit of a type in a project (the project's raised one, or the game's), or null without a list.</summary>
    public static int? Limit(IReadOnlyDictionary<string, int>? raised, MissionObjectKind kind) =>
        Of(kind) is not { } list
            ? null
            : list.CanRaise && raised is not null && raised.TryGetValue(kind.ToString(), out int limit) && limit > list.GameLimit
                ? Math.Min(limit, list.Cap)
                : list.GameLimit;

    /// <summary>The raised limits of a project that are valid (type known and raisable, above the game's, at most the cap).</summary>
    public static IReadOnlyDictionary<MissionObjectKind, int> Raised(IReadOnlyDictionary<string, int>? raised)
    {
        var result = new Dictionary<MissionObjectKind, int>();
        foreach (ObjectList list in Lists.Where(l => l.CanRaise))
        {
            if (Limit(raised, list.Kind) is { } limit && limit > list.GameLimit)
            {
                result[list.Kind] = limit;
            }
        }

        return result;
    }

    /// <summary>
    /// The highest limit one raise can set: the current one plus <see cref="MaxStep"/>, at most the cap. The shared
    /// budget of a mission (display objects, radar, object memory) is checked separately.
    /// </summary>
    public static int NextMaximum(IReadOnlyDictionary<string, int>? raised, MissionObjectKind kind) =>
        Of(kind) is { CanRaise: true } list ? Math.Min(list.Cap, (Limit(raised, kind) ?? list.GameLimit) + MaxStep) : Limit(raised, kind) ?? 0;

    /// <summary>
    /// Raises the limit of a type in the project's settings (the dictionary is changed).
    /// </summary>
    /// <exception cref="ArgumentException">The type cannot be raised, or the value is below the current limit or above
    /// <see cref="NextMaximum"/>.</exception>
    public static void Raise(IDictionary<string, int> raised, MissionObjectKind kind, int limit)
    {
        ArgumentNullException.ThrowIfNull(raised);
        if (Of(kind) is not { CanRaise: true })
        {
            throw new ArgumentException(CoreText.T("The limit of this object type cannot be raised."), nameof(kind));
        }

        int current = Limit((IReadOnlyDictionary<string, int>)new Dictionary<string, int>(raised), kind)!.Value;
        int maximum = NextMaximum((IReadOnlyDictionary<string, int>)new Dictionary<string, int>(raised), kind);
        if (limit < current || limit > maximum)
        {
            throw new ArgumentException(CoreText.F("The limit can be set from {0} to {1}.", current, maximum), nameof(limit));
        }

        raised[kind.ToString()] = limit;
    }

    /// <summary>What is wrong with a mission's object counts, or null: more objects of a type than its list has room for.</summary>
    public static string? Problem(MissionComm comm, IReadOnlyDictionary<string, int>? raised)
    {
        foreach (ObjectList list in Lists)
        {
            int count = comm.GetObjectCount(list.Kind);
            int limit = Limit(raised, list.Kind)!.Value;
            if (count > limit)
            {
                return CoreText.F("{0} has {1} {2}, the game has room for {3}; it would load none of them.",
                    MissionCatalog.Label(comm.Id), count, Name(list.Kind), limit);
            }
        }

        return null;
    }
}
