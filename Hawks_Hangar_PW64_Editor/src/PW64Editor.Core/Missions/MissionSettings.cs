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
    /// Numbers the point texts show for points that are not a curve of this mission (objects, photos,
    /// formations), by PointSource number. Only the texts use them; the game counts the objects.
    /// </summary>
    public Dictionary<int, int> TextPoints { get; set; } = [];

    /// <summary>The mission these settings belong to.</summary>
    [JsonIgnore]
    public TestId Id => new(Class, (Vehicle)Vehicle, Test);

    /// <summary>True if the settings change nothing.</summary>
    [JsonIgnore]
    public bool IsEmpty => ResultRows is null && !GenerateTexts && Footnote.Count == 0 && TextPoints.Count == 0;

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
        TextPoints = new Dictionary<int, int>(TextPoints),
    };
}
