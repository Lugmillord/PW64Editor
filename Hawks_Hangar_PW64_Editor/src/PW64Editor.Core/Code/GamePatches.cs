using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Code;

/// <summary>
/// A change of the game that adds or extends a feature (unlike a <see cref="CodeFix"/>, which only
/// corrects a bug). A game patch can be applied and undone in the project settings; every build
/// starts again from the clean ROM, so undoing simply leaves it out of the next build.
/// </summary>
/// <param name="Id">Stable identifier, stored in project.json. Never change it once released.</param>
/// <param name="Name">Short name for the user (English, translated where it is shown).</param>
/// <param name="Description">What the patch does (English, translated where it is shown).</param>
/// <param name="Patches">The changed instructions of the game code; each is checked against the original before it is written.</param>
public sealed record GamePatch(string Id, string Name, string Description, IReadOnlyList<CodePatch> Patches)
{
    /// <summary>The instruction changes as a <see cref="CodeFix"/>, so the builder applies them like a fix.</summary>
    public CodeFix AsCodeFix => new(Id, Name, Description, string.Empty, Patches);
}

/// <summary>The game patches the editor knows (US version).</summary>
public static class GamePatches
{
    /// <summary>
    /// Goal ring, ball target and Jumble Hopper goals for every vehicle (game module flag 64).
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item>taskFrameUpdate (task.c): when the ball target or the goal ring ends the mission, the
    ///         game set the vehicle to Rocket Belt ("veh = ROCKET_BELT" instead of a comparison) and
    ///         played the Rocket Belt's jingle. The assignment and the check for the Hang Glider are
    ///         removed, and sndSetMusic goes to the module, which plays the jingle of the vehicle being
    ///         flown. Result screen and saved points then belong to the right vehicle (without the patch a Hang
    ///         Glider at a goal ring crashes the game, checked in the emulator).</item>
    ///   <item>func_802FB22C (code_82520.c): the loader of the hopper goals moved its data pointer and
    ///         used the index, so goal 2 read the data of goal 3 and so on. The pointer no longer moves.</item>
    ///   <item>The scoring function (through the module): the vehicles pass only 16 bits of flags, so
    ///         the points of ball targets (0x10000) and hopper goals (0x20000) only counted after a crash
    ///         or the hard end. With the patch they always count. All retail ball targets and hopper
    ///         goals have 0 points, so the original missions do not change.</item>
    /// </list>
    /// <para>Independent of the patch, the module adds the time points when the goal ring or the ball target
    /// ends the mission, and gives a vehicle that ends in the air before it set its flags (Hang Glider,
    /// Birdman: 0 until they land) the flags of a flight without a good landing.</para>
    /// </remarks>
    public static readonly GamePatch GoalObjects = new(
        Id: "goal-objects-v1",
        Name: CoreText.K("Goal ring, ball target and hopper goals for every vehicle"),
        Description: CoreText.K("The goal ring and the ball target end the mission for every vehicle, with the vehicle's own " +
            "jingle (the original game turns every vehicle into the Rocket Belt there and can crash). Missions can have up to five " +
            "Jumble Hopper goals, and the points of hopper goals and ball targets count. The original missions do not change."),
        Patches:
        [
            // Ball target ends the mission (0x80345548): sh t2, 0xE(t3) -> nop; beqz t5 (Hang Glider) -> nop;
            // jal sndSetMusic -> jal module_goal_jingle.
            CodePatch.FromWords("goal jingle, ball target, taskFrameUpdate", 0xCCA78,
                [0xA56A000E, 0x8D8C2690, 0x958D000E, 0x11A00009, 0x00000000, 0x0C0CFDD2, 0x24040009],
                [0x00000000, 0x8D8C2690, 0x958D000E, 0x00000000, 0x00000000, CodeArea.Jal(GameModuleImage.GoalJingleAddress), 0x24040009]),
            // Goal ring ends the mission (0x803455B0): the same.
            CodePatch.FromWords("goal jingle, goal ring, taskFrameUpdate", 0xCCAE0,
                [0xA519000E, 0x8D292690, 0x952A000E, 0x11400009, 0x00000000, 0x0C0CFDD2, 0x24040009],
                [0x00000000, 0x8D292690, 0x952A000E, 0x00000000, 0x00000000, CodeArea.Jal(GameModuleImage.GoalJingleAddress), 0x24040009]),
            // Hopper goals (0x802FB2E8): "sw t3, 0x28(sp)" (hopd++) in the delay slot of the loop -> nop.
            CodePatch.FromWords("hopper goal data, func_802FB22C", 0x8280C,
                [0x254B0020, 0x26100018, 0x1420FFF0, 0xAFAB0028],
                [0x254B0020, 0x26100018, 0x1420FFF0, 0x00000000]),
        ]);

    /// <summary>
    /// The missions that use what a game patch adds, with the reason (translated), for the warning
    /// before the patch is undone. Without the patch these missions would not work as set.
    /// </summary>
    public static IReadOnlyList<string> MissionsUsing(GamePatch patch, IEnumerable<MissionInfo> missions)
    {
        var uses = new List<string>();
        if (patch.Id != GoalObjects.Id)
        {
            return uses;
        }

        foreach (MissionInfo mission in missions)
        {
            Vehicle vehicle = mission.Id.Vehicle;
            int goalRings = MissionCatalog.RingTypeCounts(mission.File)[3];
            if (goalRings > 0 && vehicle != Vehicle.RocketBelt)
            {
                uses.Add(CoreText.F("{0}: goal ring (without the patch the game turns the vehicle into the Rocket Belt and can crash)", MissionCatalog.Label(mission.Id)));
            }

            if (mission.File.Comm.GetObjectCount(MissionObjectKind.BallTarget) > 0 && vehicle != Vehicle.RocketBelt)
            {
                uses.Add(CoreText.F("{0}: ball target (without the patch the game turns the vehicle into the Rocket Belt and can crash)", MissionCatalog.Label(mission.Id)));
            }

            int hopperGoals = mission.File.Comm.GetObjectCount(MissionObjectKind.HopperGoal);
            if (hopperGoals > 1)
            {
                uses.Add(CoreText.F("{0}: {1} hopper goals (only the first one works without the patch)", MissionCatalog.Label(mission.Id), hopperGoals));
            }
        }

        return uses;
    }

    /// <summary>All game patches, in the order the editor shows them.</summary>
    public static IReadOnlyList<GamePatch> All { get; } = [GoalObjects];

    /// <summary>Ids of all game patches.</summary>
    public static IReadOnlyList<string> AllIds { get; } = All.Select(p => p.Id).ToList();

    /// <summary>The patch with this id, or null.</summary>
    public static GamePatch? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}
