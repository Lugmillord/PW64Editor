using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// What the game takes from the place of a mission (vehicle, class, test) rather than from its file, and the names the
/// editor gives the places the game has no texts for.
/// </summary>
/// <remarks>
/// <para>The test screen (testMenuInitText in test_menu.c) builds the names of the title and the hint from the place:
/// "%s_%s_%d_N" with the class letter (E, A, B, P), the vehicle (HG, RP, GC, BD) and the test number, e.g. B_HG_3_N; the task
/// text is the mission file's text base (JPTX) with "_M". The editor keeps the text base equal to the place, so a mission
/// shows the texts of its place: when tests move up, their texts move with them.</para>
/// <para>The points texts of the mission screen and the result sheet come from the result tables by place (HG_B3_S1,
/// HG_B3_S2). The game has them for its tests; a fourth or fifth test (or a second Beginner test) gets new names:
/// HG_B4_S1, HG_E2_S2 ….</para>
/// <para>Names of the game's functions from the decompilation (https://github.com/gcsmith/Pilotwings64Decomp, MIT license).</para>
/// </remarks>
public static partial class MissionPositions
{
    /// <summary>Tests per class the game can hold (MAX_TESTS).</summary>
    public const int MaxTests = SaveGameLayout.MaxTests;

    /// <summary>The class letters of the game's text names (sClassShortNames).</summary>
    public const string ClassLetters = "EABP";

    /// <summary>The vehicles' short names of the game's text names (gVehShortNames).</summary>
    private static readonly string[] Short = ["HG", "RP", "GC", "EX", "EX", "EX", "BD"];

    /// <summary>The vehicles' prefixes of the points texts (HG_A1_S1, RP_…, GC_…).</summary>
    private static readonly string[] PointsPrefix = ["HG", "RP", "GC"];

    /// <summary>
    /// True if the vehicle's classes can get more tests (Hang Glider, Rocket Belt, Gyrocopter and the Birdman's stages):
    /// Cannonball has four fixed targets per level, and the test screen of Sky Diving and the Jumble Hopper shows the
    /// levels as their tests.
    /// </summary>
    public static bool CanAddTests(Vehicle vehicle) => Vehicles.IsMain(vehicle) || vehicle == Vehicle.Birdman;

    /// <summary>The tests of the game's classes (Beginner 1, Class A 2, Class B and Pilot 3; the Birdman 4 per stage).</summary>
    public static int GameTestCount(Vehicle vehicle, int classIndex) => SaveGameLayout.RetailWithBirdmanStage4.TestCount(classIndex, vehicle);

    /// <summary>
    /// The base name of the title, task and hint texts of a place (B_HG_3), or null where the game does not build it from
    /// the place (bonus games) or outside the four classes.
    /// </summary>
    public static string? StatementBase(TestId id) =>
        CanAddTests(id.Vehicle) && id.Class is >= 0 and < 4 && id.Test is >= 0 and < MaxTests
            ? $"{ClassLetters[id.Class]}_{Short[(int)id.Vehicle]}_{id.Test + 1}"
            : null;

    /// <summary>
    /// The name the editor gives the points text of a place of a main vehicle the game has none for (HG_A3_S1, HG_E2_S2),
    /// or null for the game's places and other vehicles.
    /// </summary>
    /// <param name="id">The place.</param>
    /// <param name="resultSheet">True for the result sheet ("…_S2"), false for the mission screen ("…_S1").</param>
    public static string? NewPointsTextName(TestId id, bool resultSheet) =>
        Vehicles.IsMain(id.Vehicle) && id.Class is >= 0 and < 4 && id.Test >= GameTestCount(id.Vehicle, id.Class) && id.Test < MaxTests
            ? $"{PointsPrefix[(int)id.Vehicle]}_{ClassLetters[id.Class]}{id.Test + 1}_S{(resultSheet ? 2 : 1)}"
            : null;

    /// <summary>The game's points text a new one starts as a copy of: that of the class's first test (HG_A1_S1, HG_E_S1).</summary>
    public static string FirstTestPointsTextName(TestId id, bool resultSheet) =>
        id.Class == 0
            ? $"{PointsPrefix[(int)id.Vehicle]}_E_S{(resultSheet ? 2 : 1)}"
            : $"{PointsPrefix[(int)id.Vehicle]}_{ClassLetters[Math.Clamp(id.Class, 0, 3)]}1_S{(resultSheet ? 2 : 1)}";

    /// <summary>All places of new points texts (for the result tables).</summary>
    public static IEnumerable<TestId> NewPointsPlaces =>
        from v in Vehicles.Main
        from c in Enumerable.Range(0, 4)
        from t in Enumerable.Range(GameTestCount(v, c), MaxTests - GameTestCount(v, c))
        select new TestId(c, v, t);

    /// <summary>True for the name of a title, task or hint of a place (B_HG_4_N, E_BD_2_H).</summary>
    public static bool IsStatementText(string name) => StatementPattern().IsMatch(name);

    [System.Text.RegularExpressions.GeneratedRegex("^[EABP]_(HG|RP|GC|BD)_[1-5]_[NMH]$")]
    private static partial System.Text.RegularExpressions.Regex StatementPattern();

    /// <summary>True for a name of <see cref="NewPointsTextName"/>.</summary>
    public static bool IsNewPointsText(string name) =>
        NewPointsPlaces.Any(p => NewPointsTextName(p, false) == name || NewPointsTextName(p, true) == name);
}
