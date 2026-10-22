using System.Text.Json.Serialization;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>
/// What the project changes about a mission besides its mission file (stored in project.json):
/// the rows of its result sheet (a table in the game code) and whether the editor writes its
/// point texts.
/// </summary>
public sealed class MissionSettings
{
    /// <summary>Vehicle number (Vehicle).</summary>
    public int Vehicle { get; set; }

    /// <summary>Class (bonus games: level, Birdman: stage), from 0.</summary>
    public int Class { get; set; }

    /// <summary>Test within the class, from 0.</summary>
    public int Test { get; set; }

    /// <summary>
    /// The point rows of the result sheet, top row first (PointSource numbers), or null for the
    /// rows of the original game.
    /// </summary>
    public List<int>? ResultRows { get; set; }

    /// <summary>
    /// True if the editor writes the point texts ("…_S1" and "…_S2") of this mission from its points.
    /// Set as soon as the points are changed in the Missions tab.
    /// </summary>
    public bool GenerateTexts { get; set; }

    /// <summary>Lines of the footnote of the mission screen text (without the leading "*").</summary>
    public List<string> Footnote { get; set; } = [];

    /// <summary>
    /// True: the editor writes the footnote from the settings (time limit, conditions, altitude);
    /// false: <see cref="Footnote"/> as typed in; null (older projects): as typed in.
    /// </summary>
    public bool? AutoFootnote { get; set; }

    /// <summary>The conditions of the mission, or null for the retail condition in its mission file.</summary>
    public MissionConditions? Conditions { get; set; }

    /// <summary>
    /// Numbers the point texts show for points that are not a curve of this mission (objects, photos,
    /// formations), by PointSource number. Only the texts use them; the game counts the objects.
    /// </summary>
    public Dictionary<int, int> TextPoints { get; set; } = [];

    /// <summary>
    /// Beginner missions of Hang Glider, Rocket Belt and Gyrocopter: the hint screen has no
    /// "View Demo" button (see <see cref="DemoButton"/>).
    /// </summary>
    public bool HideDemo { get; set; }

    /// <summary>Rocket Belt and Gyrocopter: the fuel at the start (0 to 1), or null for the mission file's.</summary>
    public float? StartFuel { get; set; }

    /// <summary>Rocket Belt and Gyrocopter: the fuel never runs out (the game module refills it every frame).</summary>
    public bool UnlimitedFuel { get; set; }

    /// <summary>
    /// When the hard end time of the mission (COMM, 0 = off) is reached: true = 0 points, false = the
    /// flight is scored like one that ends in the air (game module).
    /// </summary>
    public bool HardEndZero { get; set; }

    /// <summary>
    /// The jingle when the hard end time is reached, or null for the default (landing OK when the flight is
    /// scored, landing missed with 0 points).
    /// </summary>
    public HardEndJingle? HardEndJingle { get; set; }

    /// <summary>
    /// Gyrocopter: its own curve for the touchdown point (the game multiplies it with the landing accuracy
    /// curve of the resting position), or null: both measurements use the landing accuracy curve, as in the
    /// original game. In the game's form (distance in game units, points like the curve of the mission file).
    /// </summary>
    public List<CurvePoint>? TouchdownCurve { get; set; }

    /// <summary>The landing values of this mission that differ from the original game (stored form, see <see cref="LandingValues"/>).</summary>
    public Dictionary<LandingValue, float> Landing { get; set; } = [];

    /// <summary>The width of each runway for a good landing (game units, in the order of the mission file), or null for the mission file's.</summary>
    public List<float>? RunwayWidths { get; set; }

    /// <summary>
    /// Birdman: the mission has points (game patch "Bonus games and Birdman"): a landing ends it (a good one on a landing
    /// pad), the impact is its speed at the touchdown (km/h as the HUD shows it), and it has its own mission screen and result sheet (BD_E1_S1 …) instead of the free flight text
    /// BD_ALL_S1/S2.
    /// </summary>
    public bool BirdmanPoints { get; set; }

    /// <summary>The mission these settings belong to.</summary>
    [JsonIgnore]
    public TestId Id => new(Class, (Vehicle)Vehicle, Test);

    /// <summary>True if the settings change nothing.</summary>
    [JsonIgnore]
    public bool IsEmpty => ResultRows is null && !GenerateTexts && Footnote.Count == 0 && TextPoints.Count == 0
        && AutoFootnote is null && Conditions is null && !HideDemo && StartFuel is null && !UnlimitedFuel && !HardEndZero && HardEndJingle is null
        && TouchdownCurve is null && Landing.Count == 0 && RunwayWidths is null && !BirdmanPoints;

    /// <summary>Empty settings for a mission.</summary>
    public static MissionSettings For(TestId id) => new() { Vehicle = (int)id.Vehicle, Class = id.Class, Test = id.Test };

    /// <summary>A copy.</summary>
    public MissionSettings Clone() => new()
    {
        Vehicle = Vehicle,
        Class = Class,
        Test = Test,
        ResultRows = ResultRows is null ? null : [.. ResultRows],
        GenerateTexts = GenerateTexts,
        Footnote = [.. Footnote],
        AutoFootnote = AutoFootnote,
        Conditions = Conditions?.Clone(),
        TextPoints = new Dictionary<int, int>(TextPoints),
        HideDemo = HideDemo,
        StartFuel = StartFuel,
        UnlimitedFuel = UnlimitedFuel,
        HardEndZero = HardEndZero,
        HardEndJingle = HardEndJingle,
        TouchdownCurve = TouchdownCurve is null ? null : [.. TouchdownCurve],
        Landing = new Dictionary<LandingValue, float>(Landing),
        RunwayWidths = RunwayWidths is null ? null : [.. RunwayWidths],
        BirdmanPoints = BirdmanPoints,
    };
}

/// <summary>The jingle the game plays when a mission's hard end time is reached.</summary>
public enum HardEndJingle
{
    /// <summary>The vehicle's "landing OK" jingle (Jumble Hopper: its goal jingle).</summary>
    LandingOk = 1,

    /// <summary>The vehicle's "landing missed" jingle (Jumble Hopper: the Rocket Belt's, Birdman: its crash jingle).</summary>
    LandingMissed = 2,

    /// <summary>No jingle.</summary>
    None = 3,
}

/// <summary>The music numbers of the hard end jingles per vehicle.</summary>
public static class HardEndJingles
{
    /// <summary>The music number (BGM) of a jingle for a vehicle, 0 for none.</summary>
    public static byte Music(Vehicle vehicle, HardEndJingle jingle) => (vehicle, jingle) switch
    {
        (_, HardEndJingle.None) => 0,
        (Vehicle.HangGlider, HardEndJingle.LandingOk) => 0x05,
        (Vehicle.HangGlider, _) => 0x06,
        (Vehicle.RocketBelt, HardEndJingle.LandingOk) => 0x09,
        (Vehicle.RocketBelt, _) => 0x0A,
        (Vehicle.Gyrocopter, HardEndJingle.LandingOk) => 0x0D,
        (Vehicle.Gyrocopter, _) => 0x0E,
        (Vehicle.SkyDiving, HardEndJingle.LandingOk) => 0x14,
        (Vehicle.SkyDiving, _) => 0x15,
        (Vehicle.JumbleHopper, HardEndJingle.LandingOk) => 0x18, // goal
        (Vehicle.JumbleHopper, _) => 0x0A,                       // the Rocket Belt's "landing missed" (it has none of its own)
        (Vehicle.Birdman, HardEndJingle.LandingOk) => 0x1A,
        (Vehicle.Birdman, _) => 0x1B,                            // crash (it has no "landing missed")
        _ => 0,
    };

    /// <summary>The jingle of a mission: as set, or the default for its scoring.</summary>
    public static HardEndJingle Effective(MissionSettings? settings) =>
        settings?.HardEndJingle ?? (settings?.HardEndZero == true ? HardEndJingle.LandingMissed : HardEndJingle.LandingOk);
}

