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

    /// <summary>The mission these settings belong to.</summary>
    [JsonIgnore]
    public TestId Id => new(Class, (Vehicle)Vehicle, Test);

    /// <summary>True if the settings change nothing.</summary>
    [JsonIgnore]
    public bool IsEmpty => ResultRows is null && !GenerateTexts && Footnote.Count == 0 && TextPoints.Count == 0
        && AutoFootnote is null && Conditions is null && !HideDemo && StartFuel is null && !UnlimitedFuel;

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
    };
}
