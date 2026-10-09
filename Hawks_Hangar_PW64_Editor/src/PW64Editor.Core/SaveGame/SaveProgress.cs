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
/// <para><b>License classes</b>: a class has a medal if none of its tests is 127 and the sum reaches
/// the limit (Beginner 70/80/90, A 140/160/180, B and Pilot 210/240/270). Class A is open when all
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

    public SaveProgress(SaveSlot slot)
    {
        Slot = slot;
    }

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

    /// <summary>Medal of a license class of a main vehicle.</summary>
    public Medal ClassMedal(Vehicle vehicle, int classIndex)
    {
        if (!HasAllResults(vehicle, classIndex))
        {
            return Medal.None;
        }

        int total = ClassTotal(vehicle, classIndex);
        for (Medal medal = Medal.Gold; medal >= Medal.Bronze; medal--)
        {
            if (total >= Limit(classIndex, medal))
            {
                return medal;
            }
        }

        return Medal.None;
    }

    /// <summary>True if the game lets the player choose this class with this vehicle.</summary>
    public bool IsClassOpen(Vehicle vehicle, int classIndex)
    {
        if (classIndex == 0)
        {
            return true;
        }

        // Class A for any vehicle needs a Beginner medal with all three vehicles.
        if (Vehicles.Main.Any(v => ClassMedal(v, 0) == Medal.None))
        {
            return false;
        }

        for (int c = 1; c < classIndex; c++)
        {
            if (ClassMedal(vehicle, c) == Medal.None)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if Class A is still closed and the Beginner class can be raised to open it.</summary>
    public bool CanOpenClassA => !IsClassOpen(Vehicle.HangGlider, 1);

    /// <summary>
    /// Opens Class A: raises the Beginner results of all three vehicles to the bronze limit (70),
    /// and gives the Class A tests that have no result 0 points.
    /// </summary>
    public void OpenClassA()
    {
        foreach (Vehicle vehicle in Vehicles.Main)
        {
            RaiseClass(vehicle, 0, Limit(0, Medal.Bronze));
        }

        foreach (Vehicle vehicle in Vehicles.Main)
        {
            ClearNoResults(vehicle, 1);
        }
    }

    /// <summary>True if Class B or Pilot (<paramref name="classIndex"/> 2 or 3) can be opened for the vehicle.</summary>
    public bool CanOpenClass(Vehicle vehicle, int classIndex) =>
        classIndex >= 2 && IsClassOpen(vehicle, classIndex - 1) && !IsClassOpen(vehicle, classIndex);

    /// <summary>
    /// Opens Class B or Pilot for a vehicle: raises the class before to its bronze limit, and gives the
    /// tests of the new class that have no result 0 points.
    /// </summary>
    public void OpenClass(Vehicle vehicle, int classIndex)
    {
        if (!CanOpenClass(vehicle, classIndex))
        {
            return;
        }

        RaiseClass(vehicle, classIndex - 1, Limit(classIndex - 1, Medal.Bronze));
        ClearNoResults(vehicle, classIndex);
    }

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
            if (total >= Limit(0, medal, bonus: true))
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
    public bool IsLevelOpen(Vehicle bonusGame, int level)
    {
        if (!IsBonusGameOpen(bonusGame))
        {
            return false;
        }

        for (int l = 0; l < level; l++)
        {
            if (LevelTotal(bonusGame, l) < Limit(0, Medal.Bronze, bonus: true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True if the level is closed and can be opened in one step: level 1 when the required license
    /// class is open for all three vehicles, the other levels when the level before is open.
    /// </summary>
    public bool CanOpenLevel(Vehicle bonusGame, int level)
    {
        if (IsLevelOpen(bonusGame, level))
        {
            return false;
        }

        return level == 0
            ? Vehicles.Main.All(v => IsClassOpen(v, RequiredClass(bonusGame)))
            : IsLevelOpen(bonusGame, level - 1);
    }

    /// <summary>
    /// Opens a level: level 1 raises the required license class of all three vehicles to silver,
    /// the other levels raise the level before to 70 points. The new level's tests that have no
    /// result get 0 points.
    /// </summary>
    public void OpenLevel(Vehicle bonusGame, int level)
    {
        if (!CanOpenLevel(bonusGame, level))
        {
            return;
        }

        if (level == 0)
        {
            int classIndex = RequiredClass(bonusGame);
            foreach (Vehicle vehicle in Vehicles.Main)
            {
                RaiseClass(vehicle, classIndex, Limit(classIndex, Medal.Silver));
            }
        }
        else
        {
            int needed = Limit(0, Medal.Bronze, bonus: true);
            if (LevelTotal(bonusGame, level - 1) < needed)
            {
                SetLevelValue(bonusGame, level - 1, needed);
            }
        }

        if (LevelValue(bonusGame, level) == SaveSlot.NoResult)
        {
            SetLevelValue(bonusGame, level, 0);
        }
    }

    // ----- Birdman -----

    /// <summary>Number of Birdman stages.</summary>
    public const int BirdmanStages = 4;

    /// <summary>True if the game opens this Birdman stage (0-3).</summary>
    public bool IsBirdmanStageOpen(int stage)
    {
        if (!Vehicles.Main.All(v => ClassMedal(v, stage) >= Medal.Silver))
        {
            return false;
        }

        if (stage == 0)
        {
            return true;
        }

        Vehicle bonusGame = Vehicles.BonusGames[stage - 1];
        int silver = Limit(0, Medal.Silver, bonus: true);
        for (int level = 0; level < 3; level++)
        {
            if (LevelTotal(bonusGame, level) < silver)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True if the Birdman stage is closed and can be opened in one step: the license class it needs
    /// (Beginner, A, B, Pilot) is open for all three vehicles.
    /// </summary>
    public bool CanOpenBirdmanStage(int stage) =>
        stage is >= 0 and < BirdmanStages && !IsBirdmanStageOpen(stage) && Vehicles.Main.All(v => IsClassOpen(v, stage));

    /// <summary>
    /// Opens a Birdman stage: raises the license class it needs to silver for all three vehicles,
    /// and for stage 2-4 every level of Cannonball / Sky Diving / Jumble Hopper to 80 points.
    /// </summary>
    public void OpenBirdmanStage(int stage)
    {
        if (!CanOpenBirdmanStage(stage))
        {
            return;
        }

        foreach (Vehicle vehicle in Vehicles.Main)
        {
            RaiseClass(vehicle, stage, Limit(stage, Medal.Silver));
        }

        if (stage == 0)
        {
            return;
        }

        Vehicle bonusGame = Vehicles.BonusGames[stage - 1];
        int silver = Limit(0, Medal.Silver, bonus: true);
        for (int level = 0; level < 3; level++)
        {
            if (LevelTotal(bonusGame, level) < silver)
            {
                SetLevelValue(bonusGame, level, silver);
            }
        }
    }

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
