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
    ///   <item>Birdman missions with points (<see cref="MissionSettings.BirdmanPoints"/>, a mask in the data area header):
    ///         resultInit takes the Birdman's texts from the tables; the result sheet and the test screen show line,
    ///         numbers and top score for them (checks "veh != BIRDMAN" through the module); the test screen reads their
    ///         points from (class, test); "all perfect" needs 100 points in each of them. The module ends such a mission
    ///         at a soft touchdown (landing) and measures the impact (see module.c, birdman_frame). Their stage shows the
    ///         level banner and the tests to go on the stage screen, the HUD radar and timer, the objects on the map, and
    ///         "Next" leads to the total screen with only these missions, medal limits of 70/80/90 points each and the
    ///         menu Retry / Another test / Quit (module.c, module_tot_…).</item>
    /// </list>
    /// <para>Function and table names from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
    /// </remarks>
    public static readonly GamePatch BonusGames = new(
        Id: "bonus-birdman-v1",
        Name: CoreText.K("Bonus games and Birdman"),
        Description: CoreText.K("Every Sky Diving level gets its own mission screen and result sheet, every Jumble Hopper level " +
            "its own result sheet (the game shares one text for all levels; the editor adds the texts as copies). The Birdman " +
            "gets medal limits (280/320/360 points per stage) and the game saves its stage 4 too, so the photos of a save " +
            "file lie at another place than without the patch. Birdman missions can get points (Missions tab): a landing ends " +
            "them, they have a score sheet and a total screen, and the medal limits of their stage become 70/80/90 points " +
            "per mission with points. The original missions do not change."),
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

        // Birdman missions with points (data area header +0x14): resultInit takes the Birdman's names from the tables too
        // (0x8032E0B4: "bne veh, 6" becomes "b"; the tables hold BD_ALL_S1/S2 for the free flights).
        patches.Add(CodePatch.FromWords("Birdman names, resultInit", 0xB55E4, [0x14E10008], [0x10000008]));

        // "veh != BIRDMAN" of the result sheet (line above the total, the numbers) and of the test screen (top score box,
        // its label, the points): "lhu d, 2(b); li at, 6" (or the other way round) becomes "jal module_bm_d_b; lhu d, 2(b)".
        patches.Add(CodePatch.FromWords("Birdman with points, line of the result sheet", 0xB5FDC,
            [0x954B0002, 0x24010006], [CodeArea.Jal(GameModuleImage.BirdmanCheckT3T2Address), 0x954B0002]));
        patches.Add(CodePatch.FromWords("Birdman with points, numbers of the result sheet", 0xB62A4,
            [0x97190002, 0x24010006], [CodeArea.Jal(GameModuleImage.BirdmanCheckT9T8Address), 0x97190002]));
        patches.Add(CodePatch.FromWords("Birdman with points, top score box, testMenuDraw", 0xD1344,
            [0x24010006, 0x95F90002], [CodeArea.Jal(GameModuleImage.BirdmanCheckT9T7Address), 0x95F90002]));
        patches.Add(CodePatch.FromWords("Birdman with points, top score label (hint), testMenuDraw", 0xD13CC,
            [0x24010006, 0x95AE0002], [CodeArea.Jal(GameModuleImage.BirdmanCheckT6T5Address), 0x95AE0002]));
        patches.Add(CodePatch.FromWords("Birdman with points, top score label, testMenuDraw", 0xD142C,
            [0x95F90002, 0x24010006], [CodeArea.Jal(GameModuleImage.BirdmanCheckT9T7Address), 0x95F90002]));
        patches.Add(CodePatch.FromWords("Birdman with points, top score, testMenuDraw", 0xD1800,
            [0x95F90002, 0x24010006], [CodeArea.Jal(GameModuleImage.BirdmanCheckT9T7Address), 0x95F90002]));

        // The map ("View map", map3dLoad) leaves out rings and objects for the Birdman; with points it shows them.
        patches.Add(CodePatch.FromWords("Birdman with points, objects on the map, map3dLoad", 0x977E8,
            [0x95090002, 0x24010006], [CodeArea.Jal(GameModuleImage.BirdmanCheckT1T0Address), 0x95090002]));

        // The stage screen (test_summary.c): the banner "Level N" instead of "Free Flight", the line with the tests and the
        // medal, the points and "N tests to go" of the missions with points.
        patches.Add(CodePatch.FromWords("Birdman with points, banner of the stage screen", 0x939FC,
            [0x24030007, 0x3C188035], [CodeArea.Jal(GameModuleImage.BirdmanBannerAddress), 0x3C188035]));
        patches.Add(CodePatch.FromWords("Birdman with points, medal line of the stage screen", 0x93E84,
            [0x24010006, 0x97280002], [CodeArea.Jal(GameModuleImage.BirdmanCheckT0T9Address), 0x97280002]));
        patches.Add(CodePatch.FromWords("Birdman with points, tests of the stage screen", 0x93CB8,
            [0x0C0D0866], [CodeArea.Jal(GameModuleImage.TryTextAddress)]));

        // "Next" on the result sheet: to the total screen (resultMenuChoose, "beql veh, 6" becomes "j module_res_next").
        patches.Add(CodePatch.FromWords("Birdman with points, total screen after the result sheet, resultMenuChoose", 0xB5D24,
            [0x50410006, 0x240E0002], [CodeArea.J(GameModuleImage.ResultNextAddress), 0x240E0002]));

        // The total screen (total_results.c): only the missions with points, their medal texts and the menu.
        foreach (int call in new[] { 0xCE884, 0xCE90C, 0xCEFC4 })
        {
            patches.Add(CodePatch.FromWords($"Birdman with points, tests of the total screen at 0x{call:X6}", call,
                [0x0C0D13BC], [CodeArea.Jal(GameModuleImage.TotalTestCountAddress)]));
        }

        patches.Add(CodePatch.FromWords("Birdman with points, points of the total screen, totResultInit", 0xCE8AC,
            [0x0C0CAF87], [CodeArea.Jal(GameModuleImage.TotalPointCountAddress)]));
        patches.Add(CodePatch.FromWords("Birdman with points, medal text of the total screen, totResultInit", 0xCEA44,
            [0x0C0D083D], [CodeArea.Jal(GameModuleImage.TotalMedalTextAddress)]));
        patches.Add(CodePatch.FromWords("Birdman with points, menu of the total screen, totResultInit", 0xCEA50,
            [0x0C0D1D53], [CodeArea.Jal(GameModuleImage.TotalMenuAddress)]));
        foreach (int call in new[] { 0xCEC04, 0xCEC38 })
        {
            patches.Add(CodePatch.FromWords($"Birdman with points, choice of the total screen at 0x{call:X6}", call,
                [0x0C0D1C54], [CodeArea.Jal(GameModuleImage.TotalChoiceAddress)]));
        }

        // The HUD (hudMainRender): radar and time for a Birdman mission with points.
        patches.Add(CodePatch.FromWords("Birdman with points, radar and time, hudMainRender", 0x9FBC4,
            [0x0C0C67BF], [CodeArea.Jal(GameModuleImage.HudBirdmanAddress)]));

        // testMenuInitText: the Birdman's points from (class, test) instead of (test, 0), through the module.
        patches.Add(CodePatch.FromWords("Birdman points of the test screen, testMenuInitText", 0xCFDB8,
            [0x0C0CAF87], [CodeArea.Jal(GameModuleImage.PointCountAddress)]));

        // "All perfect" (func_8032C27C) with the Birdman missions with points: its three calls go through the module.
        foreach (int call in AllPerfectCalls)
        {
            patches.Add(CodePatch.FromWords($"all perfect with the Birdman, call at 0x{call:X6}", call,
                [0x0C0CB09F], [CodeArea.Jal(GameModuleImage.AllPerfectAddress)]));
        }

        // levelSetPointsToNextMedal: all eight calls go through the module (the medal ceremony's to its own function,
        // which gives the Birdman its own limits instead of entry 4 of the bonus games).
        foreach (int call in PointsToMedalCalls)
        {
            patches.Add(CodePatch.FromWords($"points to the next medal, call at 0x{call:X6}", call,
                [0x0C0CB05D], [CodeArea.Jal(call == CeremonyMedalCall ? GameModuleImage.CeremonyMedalAddress : GameModuleImage.PointsToMedalAddress)]));
        }

        // levelGetTotalPoints (0x8032BD20) itself goes to the module: a Birdman stage with points counts only its missions
        // with points everywhere (stage screen, level select, scoring, medal ceremony); all others as the original.
        patches.Add(CodePatch.FromWords("Birdman with points, points of a stage, levelGetTotalPoints", 0xB3250,
            [0x27BDFFD0, 0xAFB30020], [CodeArea.J(GameModuleImage.LevelPointsAddress), 0x00000000]));

        return patches;
    }

    /// <summary>ROM offsets of the three calls of func_8032C27C ("all perfect").</summary>
    public static IReadOnlyList<int> AllPerfectCalls => [0x70944, 0x7099C, 0x94180];

    /// <summary>ROM offsets of the eight calls of levelSetPointsToNextMedal (0x8032C174).</summary>
    public static IReadOnlyList<int> PointsToMedalCalls => [0x93C64, 0xA9114, 0xB311C, 0xB3194, 0xB31D0, 0xCE518, 0xCE530, 0xCE944];

    /// <summary>The call of levelSetPointsToNextMedal in the medal ceremony (code_A8C30), which passes entry 4 for every bonus vehicle.</summary>
    public const int CeremonyMedalCall = 0xA9114;

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

                if (mission.IsBirdman && mission.Settings?.BirdmanPoints == true)
                {
                    uses.Add(CoreText.F("{0}: mission with points (without the patch it is a free flight again)", MissionCatalog.Label(mission.Id)));
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
