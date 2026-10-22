namespace PW64Editor.Core.SaveGame;

/// <summary>Medal of a class or level.</summary>
public enum Medal
{
    None,
    Bronze,
    Silver,
    Gold,
}

/// <summary>
/// The game's rules for points, medals and what is unlocked, applied to a saved game
/// (src/app/code_B2900.c, level_select.c, test_menu.c).
/// </summary>
/// <remarks>
/// <para><b>Values</b>: a test has 0-100 points (Cannonball: 0-25 per target, 100 per level), or
/// <see cref="SaveSlot.NoResult"/> (127). Sums count 127 as 0.</para>
/// <para><b>License classes</b>: a class has a medal if the sum reaches the limit (Beginner 70/80/90, A 140/160/180,
/// B and Pilot 210/240/270, or the project's limits of the vehicle and class). Class A is open when all
/// three vehicles have a medal in Beginner; Class B and Pilot are open per vehicle when the class
/// before has a medal.</para>
/// <para><b>Bonus games</b>: Cannonball opens when all three vehicles have silver in Class A, Sky
/// Diving with silver in Class B, Jumble Hopper with silver in the Pilot class. Level 2 and 3 open
/// when the level before has at least 70 points.</para>
/// <para><b>Birdman</b>: stage 1 opens with silver in Beginner for all three vehicles; stage 2-4
/// with silver in Class A/B/Pilot for all three and at least 80 points in every level of
/// Cannonball / Sky Diving / Jumble Hopper. Birdman has no points.</para>
/// </remarks>
public sealed class SaveProgress
{
    /// <summary>Highest points of a test.</summary>
    public const int MaxPoints = 100;

    /// <summary>Highest points of a Cannonball target.</summary>
    public const int MaxTargetPoints = 25;

    /// <summary>Medal limits (bronze, silver, gold) per class; index 4 is used for all bonus games (gMedalPointRequirements).</summary>
    private static readonly int[][] MedalLimits =
    [
        [70, 80, 90],
        [140, 160, 180],
        [210, 240, 270],
        [210, 240, 270],
        [70, 80, 90],
    ];

    private const int BonusLimits = 4;

    public SaveProgress(SaveSlot slot) : this(slot, null)
    {
    }

    /// <param name="slot">The saved game.</param>
    /// <param name="limits">The medal limits per vehicle and class (bronze, silver, gold; Classes tab), or null for the
    /// original ones.</param>
    public SaveProgress(SaveSlot slot, Func<Vehicle, int, IReadOnlyList<int>?>? limits) : this(slot, limits, null, null)
    {
    }

    /// <param name="slot">The saved game.</param>
    /// <param name="limits">The medal limits per vehicle and class (bronze, silver, gold; Classes tab), or null for the
    /// original ones.</param>
    /// <param name="unlocks">The unlock conditions of the menu fields (Menu tab), or null for the game's rules.</param>
    /// <param name="birdmanPoints">The Birdman missions with points (game patch "Bonus games and Birdman"): a stage with
    /// such missions counts only them, as the game does.</param>
    public SaveProgress(SaveSlot slot, Func<Vehicle, int, IReadOnlyList<int>?>? limits, IReadOnlyList<Classes.FieldUnlock>? unlocks,
        IReadOnlySet<TestId>? birdmanPoints)
    {
        Slot = slot;
        _limits = limits;
        _rules = Classes.MenuUnlocks.Effective(unlocks);
        _birdmanPoints = birdmanPoints ?? new HashSet<TestId>();
    }

    private readonly Func<Vehicle, int, IReadOnlyList<int>?>? _limits;

    private readonly List<Classes.FieldUnlock> _rules;

    private readonly IReadOnlySet<TestId> _birdmanPoints;

    /// <summary>The limit of a medal of a class of a vehicle (the project's, or the original one).</summary>
    public int LimitOf(Vehicle vehicle, int classIndex, Medal medal) =>
        _limits?.Invoke(vehicle, classIndex) is { Count: 3 } own
            ? own[(int)medal - 1]
            : Limit(classIndex, medal, bonus: !Vehicles.IsMain(vehicle));

    public SaveSlot Slot { get; }

    private SaveGameLayout Layout => Slot.Layout;

    /// <summary>Medal limit of a license class (0-3) or of the bonus games (<paramref name="bonus"/>).</summary>
    public static int Limit(int classIndex, Medal medal, bool bonus = false) =>
        MedalLimits[bonus ? BonusLimits : classIndex][(int)medal - 1];

    /// <summary>
    /// The nearest allowed number of points (0-100). Use <see cref="SaveSlot.NoResult"/> for "no result".
    /// </summary>
    public static int NearestPoints(int value) => Math.Clamp(value, 0, MaxPoints);

    // ----- License tests -----

    /// <summary>Sum of a class of a vehicle (127 counts as 0, as in levelGetTotalPoints).</summary>
    public int ClassTotal(Vehicle vehicle, int classIndex)
    {
        int sum = 0;
        for (int t = 0; t < Layout.TestCount(classIndex, vehicle); t++)
        {
            int value = Slot.GetResult(new TestId(classIndex, vehicle, t));
            sum += value == SaveSlot.NoResult ? 0 : value;
        }

        return sum;
    }

    /// <summary>True if no test of the class is 127 (func_8032BE8C).</summary>
    public bool HasAllResults(Vehicle vehicle, int classIndex)
    {
        for (int t = 0; t < Layout.TestCount(classIndex, vehicle); t++)
        {
            if (Slot.GetResult(new TestId(classIndex, vehicle, t)) == SaveSlot.NoResult)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Medal of a license class of a main vehicle. Only the sum counts (as the editor's game code decides it; with the
    /// original limits a medal is only reachable when every test has a result anyway).
    /// </summary>
    public Medal ClassMedal(Vehicle vehicle, int classIndex)
    {
        int total = ClassTotal(vehicle, classIndex);
        for (Medal medal = Medal.Gold; medal >= Medal.Bronze; medal--)
        {
            if (total >= LimitOf(vehicle, classIndex, medal))
            {
                return medal;
            }
        }

        return Medal.None;
    }

    /// <summary>True if the game lets the player choose this class with this vehicle.</summary>
    public bool IsClassOpen(Vehicle vehicle, int classIndex) => IsFieldOpen(vehicle, classIndex);

    /// <summary>True if Class A is still closed for a vehicle (it can be opened, see <see cref="OpenField"/>).</summary>
    public bool CanOpenClassA => !IsClassOpen(Vehicle.HangGlider, 1);

    /// <summary>Opens Class A for all three vehicles (<see cref="OpenField"/>).</summary>
    public void OpenClassA()
    {
        foreach (Vehicle vehicle in Vehicles.Main)
        {
            OpenField(vehicle, 1);
        }
    }

    /// <summary>True if a class of a vehicle is closed (it can be opened, see <see cref="OpenField"/>).</summary>
    public bool CanOpenClass(Vehicle vehicle, int classIndex) => !IsClassOpen(vehicle, classIndex);

    /// <summary>Opens a class of a vehicle (<see cref="OpenField"/>).</summary>
    public void OpenClass(Vehicle vehicle, int classIndex) => OpenField(vehicle, classIndex);

    // ----- Bonus games (Cannonball, Sky Diving, Jumble Hopper) -----

    /// <summary>The license class whose silver medals open a bonus game (Class A, B, Pilot).</summary>
    public static int RequiredClass(Vehicle bonusGame) => bonusGame switch
    {
        Vehicle.Cannonball => 1,
        Vehicle.SkyDiving => 2,
        Vehicle.JumbleHopper => 3,
        _ => throw new ArgumentException("Not a bonus game with points.", nameof(bonusGame)),
    };

    /// <summary>
    /// The value of a level as shown in the editor: the sum of its tests (Cannonball: of its
    /// four targets), or <see cref="SaveSlot.NoResult"/> if none of them has a result.
    /// </summary>
    public int LevelValue(Vehicle bonusGame, int level)
    {
        int count = Layout.TestCount(level, bonusGame);
        bool any = false;
        int sum = 0;
        for (int t = 0; t < count; t++)
        {
            int value = Slot.GetResult(new TestId(level, bonusGame, t));
            if (value != SaveSlot.NoResult)
            {
                any = true;
                sum += value;
            }
        }

        return any ? sum : SaveSlot.NoResult;
    }

    /// <summary>Sum of a level (127 counts as 0).</summary>
    public int LevelTotal(Vehicle bonusGame, int level)
    {
        int value = LevelValue(bonusGame, level);
        return value == SaveSlot.NoResult ? 0 : value;
    }

    /// <summary>
    /// Sets the value of a level. Cannonball spreads the points over its targets (25 each, in order),
    /// because the game only stores the targets. <see cref="SaveSlot.NoResult"/> clears all of them.
    /// </summary>
    public void SetLevelValue(Vehicle bonusGame, int level, int value)
    {
        int count = Layout.TestCount(level, bonusGame);
        if (count == 0)
        {
            return;
        }

        if (value == SaveSlot.NoResult)
        {
            for (int t = 0; t < count; t++)
            {
                Slot.SetResult(new TestId(level, bonusGame, t), SaveSlot.NoResult);
            }

            return;
        }

        value = NearestPoints(value);
        if (count == 1)
        {
            Slot.SetResult(new TestId(level, bonusGame, 0), value);
            return;
        }

        int perTest = Math.Min(MaxPoints / count, MaxPoints);
        for (int t = 0; t < count; t++)
        {
            int part = Math.Min(perTest, value);
            Slot.SetResult(new TestId(level, bonusGame, t), part);
            value -= part;
        }
    }

    /// <summary>Medal of a level of a bonus game.</summary>
    public Medal LevelMedal(Vehicle bonusGame, int level)
    {
        int total = LevelTotal(bonusGame, level);
        for (Medal medal = Medal.Gold; medal >= Medal.Bronze; medal--)
        {
            if (total >= LimitOf(bonusGame, level, medal))
            {
                return medal;
            }
        }

        return Medal.None;
    }

    /// <summary>True if all three vehicles have silver in the class that opens the bonus game.</summary>
    public bool IsBonusGameOpen(Vehicle bonusGame)
    {
        int classIndex = RequiredClass(bonusGame);
        return Vehicles.Main.All(v => ClassMedal(v, classIndex) >= Medal.Silver);
    }

    /// <summary>True if the game lets the player choose this level.</summary>
    public bool IsLevelOpen(Vehicle bonusGame, int level) => IsFieldOpen(bonusGame, level);

    /// <summary>True if the level is closed (it can be opened, see <see cref="OpenField"/>).</summary>
    public bool CanOpenLevel(Vehicle bonusGame, int level) => !IsLevelOpen(bonusGame, level);

    /// <summary>Opens a level (<see cref="OpenField"/>).</summary>
    public void OpenLevel(Vehicle bonusGame, int level) => OpenField(bonusGame, level);

    // ----- Birdman -----

    /// <summary>Number of Birdman stages.</summary>
    public const int BirdmanStages = 4;

    /// <summary>True if the game opens this Birdman stage (0-3).</summary>
    public bool IsBirdmanStageOpen(int stage) => IsFieldOpen(Vehicle.Birdman, stage);

    /// <summary>True if the Birdman stage is closed (it can be opened, see <see cref="OpenField"/>).</summary>
    public bool CanOpenBirdmanStage(int stage) => stage is >= 0 and < BirdmanStages && !IsBirdmanStageOpen(stage);

    /// <summary>Opens a Birdman stage (<see cref="OpenField"/>).</summary>
    public void OpenBirdmanStage(int stage) => OpenField(Vehicle.Birdman, stage);

    // ----- Menu fields (any vehicle and class) -----

    /// <summary>
    /// The sum of a menu field as the game counts it: a license class or Birdman stage (a stage with missions with points
    /// only those), a bonus level; 127 counts as 0.
    /// </summary>
    public int FieldTotal(Vehicle vehicle, int classIndex)
    {
        if (vehicle is Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper)
        {
            return LevelTotal(vehicle, classIndex);
        }

        if (vehicle == Vehicle.Birdman && BirdmanPointTests(classIndex) is { Count: > 0 } tests)
        {
            return tests.Sum(t => Slot.GetResult(t) is var v && v == SaveSlot.NoResult ? 0 : v);
        }

        return ClassTotal(vehicle, classIndex);
    }

    /// <summary>Points of a perfect score of a field.</summary>
    public int FieldPerfect(Vehicle vehicle, int classIndex) => vehicle switch
    {
        Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper => MaxPoints,
        Vehicle.Birdman when BirdmanPointTests(classIndex).Count > 0 => MaxPoints * BirdmanPointTests(classIndex).Count,
        _ => MaxPoints * Layout.TestCount(classIndex, vehicle),
    };

    /// <summary>The medal of a field as a number: 0 none, 1 bronze, 2 silver, 3 gold, 4 perfect.</summary>
    public int FieldMedal(Vehicle vehicle, int classIndex)
    {
        int total = FieldTotal(vehicle, classIndex);
        if (total >= FieldPerfect(vehicle, classIndex) && total >= LimitOf(vehicle, classIndex, Medal.Gold))
        {
            return 4;
        }

        for (Medal medal = Medal.Gold; medal >= Medal.Bronze; medal--)
        {
            if (total >= LimitOf(vehicle, classIndex, medal))
            {
                return (int)medal;
            }
        }

        return 0;
    }

    /// <summary>True if a condition is met.</summary>
    public bool IsMet(Classes.UnlockCondition condition) =>
        condition.Kind == Classes.UnlockKind.Medal
            ? FieldMedal((Vehicle)condition.Vehicle, condition.Class) >= condition.Value
            : FieldTotal((Vehicle)condition.Vehicle, condition.Class) >= condition.Value;

    /// <summary>The conditions of a field (the project's or the game's).</summary>
    public IReadOnlyList<Classes.UnlockCondition> ConditionsOf(Vehicle vehicle, int classIndex) =>
        _rules.FirstOrDefault(f => f.Vehicle == (int)vehicle && f.Class == classIndex)?.Conditions ?? [];

    /// <summary>True if the game lets the player choose this field (all its conditions are met).</summary>
    public bool IsFieldOpen(Vehicle vehicle, int classIndex) => ConditionsOf(vehicle, classIndex).All(IsMet);

    /// <summary>The conditions of a field that are not met yet.</summary>
    public IReadOnlyList<Classes.UnlockCondition> MissingConditions(Vehicle vehicle, int classIndex) =>
        [.. ConditionsOf(vehicle, classIndex).Where(c => !IsMet(c))];

    /// <summary>
    /// Opens a field: every condition that is not met gets its points (the field it names is opened first, and its tests
    /// are raised one after the other, each up to 100, until the sum is reached), and the tests of the field that have no
    /// result get 0 points.
    /// </summary>
    public void OpenField(Vehicle vehicle, int classIndex) => OpenField(vehicle, classIndex, []);

    private void OpenField(Vehicle vehicle, int classIndex, HashSet<(Vehicle, int)> opening)
    {
        if (!opening.Add((vehicle, classIndex)))
        {
            return; // conditions that go round in a circle
        }

        foreach (Classes.UnlockCondition condition in ConditionsOf(vehicle, classIndex))
        {
            if (IsMet(condition))
            {
                continue;
            }

            var other = (Vehicle)condition.Vehicle;
            if (!IsFieldOpen(other, condition.Class))
            {
                OpenField(other, condition.Class, opening);
            }

            int target = condition.Kind == Classes.UnlockKind.Points
                ? condition.Value
                : condition.Value >= 4 ? FieldPerfect(other, condition.Class) : LimitOf(other, condition.Class, (Medal)condition.Value);
            RaiseField(other, condition.Class, target);
        }

        if (vehicle is Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper)
        {
            if (LevelValue(vehicle, classIndex) == SaveSlot.NoResult)
            {
                SetLevelValue(vehicle, classIndex, 0);
            }
        }
        else
        {
            ClearNoResults(vehicle, classIndex);
        }
    }

    private void RaiseField(Vehicle vehicle, int classIndex, int target)
    {
        if (vehicle is Vehicle.Cannonball or Vehicle.SkyDiving or Vehicle.JumbleHopper)
        {
            if (LevelTotal(vehicle, classIndex) < target)
            {
                SetLevelValue(vehicle, classIndex, Math.Min(target, MaxPoints));
            }

            return;
        }

        IReadOnlyList<TestId> tests = vehicle == Vehicle.Birdman && BirdmanPointTests(classIndex) is { Count: > 0 } points
            ? points
            : [.. Enumerable.Range(0, Layout.TestCount(classIndex, vehicle)).Select(t => new TestId(classIndex, vehicle, t))];
        ClearNoResults(vehicle, classIndex);
        foreach (TestId id in tests)
        {
            int total = FieldTotal(vehicle, classIndex);
            if (total >= target)
            {
                break;
            }

            int value = Slot.GetResult(id);
            Slot.SetResult(id, Math.Min(MaxPoints, value + (target - total)));
        }
    }

    /// <summary>The saved Birdman missions with points of a stage.</summary>
    private IReadOnlyList<TestId> BirdmanPointTests(int stage) =>
        [.. _birdmanPoints.Where(t => t.Vehicle == Vehicle.Birdman && t.Class == stage && Layout.IsSaved(t)).OrderBy(t => t.Test)];

    // ----- All results -----

    /// <summary>
    /// Gives every saved test the highest points: 100 per test, 25 per Cannonball target (100 per
    /// level). Birdman has no points and stays as it is.
    /// </summary>
    public void SetPerfectScore()
    {
        foreach (TestId test in Layout.SavedTests)
        {
            if (test.Vehicle == Vehicle.Birdman)
            {
                continue;
            }

            int count = Layout.TestCount(test.Class, test.Vehicle);
            Slot.SetResult(test, Vehicles.IsMain(test.Vehicle) || count <= 1 ? MaxPoints : MaxPoints / count);
        }
    }

    // ----- Helpers -----

    /// <summary>
    /// Raises the tests of a class one after the other (each up to 100) until the sum reaches
    /// <paramref name="target"/>. Tests without a result count as 0 and get a result, so the class
    /// can have a medal.
    /// </summary>
    private void RaiseClass(Vehicle vehicle, int classIndex, int target)
    {
        ClearNoResults(vehicle, classIndex);
        for (int t = 0; t < Layout.TestCount(classIndex, vehicle); t++)
        {
            int total = ClassTotal(vehicle, classIndex);
            if (total >= target)
            {
                break;
            }

            var id = new TestId(classIndex, vehicle, t);
            int value = Slot.GetResult(id);
            Slot.SetResult(id, Math.Min(MaxPoints, value + (target - total)));
        }
    }

    /// <summary>Gives every test of the class that has no result 0 points.</summary>
    private void ClearNoResults(Vehicle vehicle, int classIndex)
    {
        for (int t = 0; t < Layout.TestCount(classIndex, vehicle); t++)
        {
            var id = new TestId(classIndex, vehicle, t);
            if (Slot.GetResult(id) == SaveSlot.NoResult)
            {
                Slot.SetResult(id, 0);
            }
        }
    }
}
