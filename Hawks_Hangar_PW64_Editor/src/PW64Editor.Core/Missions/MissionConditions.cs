using PW64Editor.Core.Code;
using PW64Editor.Core.Localization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>The object types a condition can count, in the order of the game module's table.</summary>
public enum ConditionObject
{
    Rings = 0,
    Balloons = 1,
    Targets = 2,
    HoverPads = 3,
    MecaHawks = 4,
    BallTargets = 5,
    HopperGoals = 6,
}

/// <summary>
/// The conditions of a mission, freely combined: "a crash gives 0 points", and for the time
/// points a number of objects per type and a minimum altitude (all must be met).
/// </summary>
/// <remarks>
/// <para>The retail game has only one condition per mission (COMM byte 4, <see cref="MissionCondition"/>).
/// Whatever that byte can express is written there (<see cref="ApplyTo"/>), so the game's own
/// helpers keep working: the "n left" counter and "land now" for a number of objects, the
/// altitude mark of the Hang Glider. Combinations are checked by the game module instead (its
/// conditions table, <see cref="GameModule"/>); the byte is then 0.</para>
/// <para>Counting is the game's: rings passed, balloons popped, targets and Meca Hawks destroyed,
/// hover pads used, ball targets hit, hopper goals reached.</para>
/// </remarks>
public sealed class MissionConditions
{
    /// <summary>"All objects of the type" in <see cref="Required"/>.</summary>
    public const int All = -1;

    /// <summary>A crash gives 0 points in total.</summary>
    public bool CrashGivesZero { get; set; }

    /// <summary>Time points only if this altitude was reached; null: no altitude condition.</summary>
    public float? MinimumAltitude { get; set; }

    /// <summary>Time points only if at least this many objects of the type are cleared (<see cref="All"/>: all of them).</summary>
    public Dictionary<ConditionObject, int> Required { get; set; } = [];

    /// <summary>
    /// The game's own behaviour for "at least N objects" (retail condition 3): as soon as the number
    /// is reached it says "please land" and removes the other objects. Only possible for one
    /// object type that is the only one the game counts in the mission, without other conditions.
    /// </summary>
    public bool LandWhenReached { get; set; }

    /// <summary>True if <see cref="LandWhenReached"/> is possible with these conditions in this mission.</summary>
    public bool CanLandWhenReached(MissionComm comm) =>
        !CrashGivesZero && MinimumAltitude is null && Required.Count == 1
        && Required.First() is var (type, count) && type != ConditionObject.Balloons && count != All
        && Enum.GetValues<ConditionObject>().Where(t => t != ConditionObject.Balloons && comm.GetObjectCount(Kind(t)) > 0).SequenceEqual([type]);

    /// <summary>True if no condition is set.</summary>
    public bool IsEmpty => !CrashGivesZero && MinimumAltitude is null && Required.Count == 0;

    /// <summary>True if the time points have a condition.</summary>
    public bool HasTimeConditions => MinimumAltitude is not null || Required.Count > 0;

    /// <summary>The mission object kind behind a condition object.</summary>
    public static MissionObjectKind Kind(ConditionObject type) => type switch
    {
        ConditionObject.Rings => MissionObjectKind.Ring,
        ConditionObject.Balloons => MissionObjectKind.Balloon,
        ConditionObject.Targets => MissionObjectKind.Target,
        ConditionObject.HoverPads => MissionObjectKind.HoverPad,
        ConditionObject.MecaHawks => MissionObjectKind.MecaHawk,
        ConditionObject.BallTargets => MissionObjectKind.BallTarget,
        _ => MissionObjectKind.HopperGoal,
    };

    /// <summary>Name of an object type (plural) in the editor's language.</summary>
    public static string Name(ConditionObject type) => type switch
    {
        ConditionObject.Rings => CoreText.T("rings"),
        ConditionObject.Balloons => CoreText.T("balloons"),
        ConditionObject.Targets => CoreText.T("targets"),
        ConditionObject.HoverPads => CoreText.T("floating pads"),
        ConditionObject.MecaHawks => "Meca Hawks",
        ConditionObject.BallTargets => CoreText.T("ball targets"),
        _ => CoreText.T("hopper goals"),
    };

    /// <summary>A copy.</summary>
    public MissionConditions Clone() => new()
    {
        CrashGivesZero = CrashGivesZero,
        MinimumAltitude = MinimumAltitude,
        LandWhenReached = LandWhenReached,
        Required = new Dictionary<ConditionObject, int>(Required),
    };

    /// <summary>True if both describe the same conditions.</summary>
    public bool SameAs(MissionConditions? other) =>
        other is not null
        && CrashGivesZero == other.CrashGivesZero
        && LandWhenReached == other.LandWhenReached
        && MinimumAltitude == other.MinimumAltitude
        && Required.Count == other.Required.Count
        && Required.All(r => other.Required.TryGetValue(r.Key, out int v) && v == r.Value);

    /// <summary>The conditions a COMM block describes with its retail condition byte.</summary>
    public static MissionConditions FromComm(MissionComm comm)
    {
        var conditions = new MissionConditions();
        switch (comm.Condition)
        {
            case MissionCondition.CrashGivesZero:
                conditions.CrashGivesZero = true;
                break;
            case MissionCondition.AllObjects:
                foreach (ConditionObject type in Enum.GetValues<ConditionObject>().Where(t => comm.GetObjectCount(Kind(t)) > 0))
                {
                    conditions.Required[type] = All;
                }

                break;
            case MissionCondition.SomeObjects:
            {
                // The game counts all types except balloons together; the retail missions have one type.
                ConditionObject type = Enum.GetValues<ConditionObject>()
                    .Where(t => t != ConditionObject.Balloons)
                    .FirstOrDefault(t => comm.GetObjectCount(Kind(t)) > 0, ConditionObject.Rings);
                conditions.Required[type] = Math.Max(1, (int)MathF.Round(comm.RequiredObjects));
                conditions.LandWhenReached = true;
                break;
            }

            case MissionCondition.MinimumAltitude:
                conditions.MinimumAltitude = comm.MinimumAltitude;
                break;
        }

        return conditions;
    }

    /// <summary>
    /// The retail condition byte that expresses these conditions exactly for this mission, or null
    /// if only the game module can check them.
    /// </summary>
    public MissionCondition? RetailMode(MissionComm comm)
    {
        if (IsEmpty)
        {
            return MissionCondition.None;
        }

        if (CrashGivesZero)
        {
            return HasTimeConditions ? null : MissionCondition.CrashGivesZero;
        }

        if (MinimumAltitude is not null)
        {
            return Required.Count == 0 ? MissionCondition.MinimumAltitude : null;
        }

        // All objects of every type the mission has.
        ConditionObject[] present = Enum.GetValues<ConditionObject>().Where(t => comm.GetObjectCount(Kind(t)) > 0).ToArray();
        if (Required.Values.All(v => v == All) && present.Length > 0 && present.ToHashSet().SetEquals(Required.Keys))
        {
            return MissionCondition.AllObjects;
        }

        // A number of objects of the only type the game counts in this mission, with the game's
        // "please land" when it is reached; without that, the module counts.
        if (LandWhenReached && CanLandWhenReached(comm))
        {
            return MissionCondition.SomeObjects;
        }

        return null;
    }

    /// <summary>True if the game module has to check these conditions for this mission.</summary>
    public bool NeedsModule(MissionComm comm) => RetailMode(comm) is null;

    /// <summary>
    /// Writes the conditions into a COMM block: the retail condition byte and its values where
    /// possible, otherwise byte 0 (the module checks them). The altitude and count fields keep
    /// their values for the game's display helpers.
    /// </summary>
    public void ApplyTo(MissionComm comm)
    {
        MissionCondition mode = RetailMode(comm) ?? MissionCondition.None;
        comm.Condition = mode;
        if (MinimumAltitude is { } altitude)
        {
            comm.MinimumAltitude = altitude;
        }

        if (mode == MissionCondition.SomeObjects)
        {
            comm.RequiredObjects = Required.Values.First();
        }
    }

    /// <summary>The entry of the game module's conditions table for a mission.</summary>
    public GameModuleCondition ToModuleCondition(MissionComm comm)
    {
        TestId id = comm.Id;
        byte[] required = new byte[GameModule.ObjectTypes];
        foreach ((ConditionObject type, int count) in Required)
        {
            required[(int)type] = count == All ? GameModule.RequiredAll : (byte)Math.Clamp(count, 1, 254);
        }

        return new GameModuleCondition((byte)id.Class, (byte)id.Vehicle, (byte)id.Test, CrashGivesZero, MinimumAltitude, required);
    }
}
