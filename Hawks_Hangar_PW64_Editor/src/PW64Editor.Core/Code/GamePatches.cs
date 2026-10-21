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
    /// Bonus games and Birdman: texts of their own for every Sky Diving and Jumble Hopper level, medal limits and
    /// four saved stages for the Birdman.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    ///   <item>resultInit (results.c, the only reader of the three result tables): the tables are read from the data
    ///         area (<see cref="ResultTables"/>, [7][4][5] instead of [6][4][3]). In the index computation "x * 4 - x"
    ///         (× 3) becomes "x * 4 + x" (× 5) for the vehicle and the class, and the addresses point to the new
    ///         tables.</item>
    ///   <item>saveFileWrite and saveFileLoad (save.c): the game saved 4 classes for the main vehicles and 3 for all
    ///         others, so the Birdman's stage 4 was lost when the game was switched off. Now only Cannonball, Sky
    ///         Diving and the Jumble Hopper have 3 ("v0 = 4 - ((veh - 3) &lt; 3)"); the Birdman gets 4. The four
    ///         results take 28 bits, so the photo album of a saved game lies 28 bits further back than without the
    ///         patch.</item>
    ///   <item>gMedalPointRequirements: entry 7 (the Birdman, medal index = vehicle + 1) was 0/0/0 and becomes
    ///         280/320/360 points per stage.</item>
    ///   <item>levelSetPointsToNextMedal goes through the module: with every medal reached the game showed
    ///         "100 - points" to go for all but the main vehicles; the Birdman gets "100 × missions - points".</item>
    /// </list>
    /// <para>Function and table names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
    /// </remarks>
    public static readonly GamePatch BonusGames = new(
        Id: "bonus-birdman-v1",
        Name: CoreText.K("Bonus games and Birdman"),
        Description: CoreText.K("Every Sky Diving level gets its own mission screen and result sheet, every Jumble Hopper level " +
            "its own result sheet (the game shares one text for all levels; the editor adds the texts as copies). The Birdman " +
            "gets medal limits (280/320/360 points per stage) and the game saves its stage 4 too, so the photos of a save " +
            "file lie at another place than without the patch. The original missions do not change."),
        Patches: BonusGamesPatches());

    private static List<CodePatch> BonusGamesPatches()
    {
        static uint Hi(uint address) => (address + 0x8000) >> 16;
        static uint Lo(uint address) => address & 0xFFFF;
        uint screen = ResultTables.MissionScreenNamesAddress, sheet = ResultTables.ResultSheetNamesAddress, rows = ResultTables.RowsAddress;
        var patches = new List<CodePatch>
        {
            // resultInit, result sheet names (0x8032E0E8): subu -> addu (× 5) for vehicle and class, then the table.
            CodePatch.FromWords("result sheet names, resultInit", 0xB5618,
                [0x0307C023, 0x00197080, 0x01D97023],
                [0x0307C021, 0x00197080, 0x01D97021]),
            CodePatch.FromWords("result sheet name table, resultInit", 0xB563C,
                [0x3C108035, 0x020E8021, 0x1000000E, 0x8E10FC40],
                [0x3C100000 | Hi(sheet), 0x020E8021, 0x1000000E, 0x8E100000 | Lo(sheet)]),
            // Mission screen names (0x8032E120).
            CodePatch.FromWords("mission screen names, resultInit", 0xB5650,
                [0x0327C823, 0x0019C900, 0x000FC080, 0x030FC023],
                [0x0327C821, 0x0019C900, 0x000FC080, 0x030FC021]),
            CodePatch.FromWords("mission screen name table, resultInit", 0xB5674,
                [0x3C108035, 0x02188021, 0x8E10FD60],
                [0x3C100000 | Hi(screen), 0x02188021, 0x8E100000 | Lo(screen)]),
            // Point rows (0x8032E208, 0x8032E22C, 0x8032E2A4).
            CodePatch.FromWords("point rows, resultInit", 0xB5738,
                [0x0327C823, 0x0302C023],
                [0x0327C821, 0x0302C021]),
            CodePatch.FromWords("point row table, resultInit", 0xB575C,
                [0x3C068035, 0x8DEE009C],
                [0x3C060000 | Hi(rows), 0x8DEE009C]),
            CodePatch.FromWords("point row table (load), resultInit", 0xB57D4,
                [0x90C6FE80],
                [0x90C60000 | Lo(rows)]),

            // saveFileWrite (0x802E84CC, 0x802E855C) and saveFileLoad (0x802E881C, 0x802E88B4): classes per vehicle.
            // "at = (veh - 3) < 3 (unsigned); v0 = 4 - at"; the outer loops jump to the second instruction with the first
            // one in their delay slot.
            SaveClasses("classes per vehicle, saveFileWrite", 0x6F9FC, 22, 0x0000A825),
            SaveClasses("classes per vehicle (next class), saveFileWrite", 0x6FA8C, 22, 0x26B50001),
            CodePatch.FromWords("classes per vehicle (next vehicle), saveFileWrite", 0x6FAB8,
                [0x5420FFD1, 0x2AC10003], [0x5420FFD1, 0x26C1FFFD]),
            SaveClasses("classes per vehicle, saveFileLoad", 0x6FD4C, 23, 0x0000B025),
            SaveClasses("classes per vehicle (next class), saveFileLoad", 0x6FDE4, 23, 0x26D60001),
            CodePatch.FromWords("classes per vehicle (next vehicle), saveFileLoad", 0x6FE10,
                [0x5420FFCF, 0x2AE10003], [0x5420FFCF, 0x26E1FFFD]),

            // gMedalPointRequirements entries 6 (Jumble Hopper, unchanged) and 7 (Birdman: 0/0/0 -> 280/320/360).
            new CodePatch("Birdman medal limits, gMedalPointRequirements", 0xD7128,
                [0x00, 0x46, 0x00, 0x50, 0x00, 0x5A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
                [0x00, 0x46, 0x00, 0x50, 0x00, 0x5A, 0x01, 0x18, 0x01, 0x40, 0x01, 0x68]),
        };

        // levelSetPointsToNextMedal: all eight calls go through the module.
        foreach (int call in PointsToMedalCalls)
        {
            patches.Add(CodePatch.FromWords($"points to the next medal, call at 0x{call:X6}", call,
                [0x0C0CB05D], [CodeArea.Jal(GameModuleImage.PointsToMedalAddress)]));
        }

        return patches;
    }

    /// <summary>ROM offsets of the eight calls of levelSetPointsToNextMedal (0x8032C174).</summary>
    public static IReadOnlyList<int> PointsToMedalCalls => [0x93C64, 0xA9114, 0xB311C, 0xB3194, 0xB31D0, 0xCE518, 0xCE530, 0xCE944];

    /// <summary>
    /// "slti at, sV, 3; bnez at, +3; (delay); b +2; li v0, 3; li v0, 4" becomes
    /// "addiu at, sV, -3; sltiu at, at, 3; (delay); li v0, 4; subu v0, v0, at; nop".
    /// </summary>
    private static CodePatch SaveClasses(string description, int romOffset, int register, uint delay)
    {
        uint r = (uint)register << 21;
        return CodePatch.FromWords(description, romOffset,
            [0x28010003 | r, 0x14200003, delay, 0x10000002, 0x24020003, 0x24020004],
            [0x24010000 | r | 0xFFFD, 0x2C210003, delay, 0x24020004, 0x00411023, 0x00000000]);
    }

    /// <summary>
    /// The missions that use what a game patch adds, with the reason (translated), for the warning
    /// before the patch is undone. Without the patch these missions would not work as set.
    /// </summary>
    public static IReadOnlyList<string> MissionsUsing(GamePatch patch, IEnumerable<MissionInfo> missions)
    {
        var uses = new List<string>();
        if (patch.Id == BonusGames.Id)
        {
            foreach (MissionInfo mission in missions)
            {
                string[] own = [.. new[] { ResultTables.LevelTextName(mission.Id, false), ResultTables.LevelTextName(mission.Id, true) }.OfType<string>()];
                if (own.Length > 0)
                {
                    uses.Add(CoreText.F("{0}: own texts {1} (without the patch the game shows the shared text again; the texts stay in the project)",
                        MissionCatalog.Label(mission.Id), string.Join(", ", own)));
                }
            }

            return uses;
        }

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
    public static IReadOnlyList<GamePatch> All { get; } = [GoalObjects, BonusGames];

    /// <summary>Ids of all game patches.</summary>
    public static IReadOnlyList<string> AllIds { get; } = All.Select(p => p.Id).ToList();

    /// <summary>The patch with this id, or null.</summary>
    public static GamePatch? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}
