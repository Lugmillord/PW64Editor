using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Missions;

/// <summary>Where the value of a point row comes from.</summary>
public enum PointValueKind
{
    /// <summary>The maximum of a score curve: exactly what the game gives at most.</summary>
    Curve,

    /// <summary>
    /// The sum over all objects of the mission. Linked rings (only one of a group counts) or a
    /// required count can make the reachable points lower.
    /// </summary>
    AllObjects,

    /// <summary>The score of another mission (Cannonball: the best shot at each target).</summary>
    OtherMission,

    /// <summary>Computed by the game while playing (photos, formations); not stored in the mission.</summary>
    ComputedByGame,

    /// <summary>The row is empty or unknown.</summary>
    None,
}

/// <summary>A point row of the result sheet with the most points the mission data gives for it.</summary>
/// <param name="Source">The kind of points.</param>
/// <param name="Value">The most points, or null if they cannot be read from the mission (see <paramref name="Kind"/>).</param>
/// <param name="Kind">Where the value comes from.</param>
public sealed record MissionPointRow(PointSource Source, int? Value, PointValueKind Kind);

/// <summary>
/// A mission (test) with everything the Missions tab shows: its file, its texts, its point rows
/// and the tables of the game code that belong to it.
/// </summary>
public sealed class MissionInfo
{
    internal MissionInfo(GameFileRef file, MissionFile mission)
    {
        TableIndex = file.TableIndex;
        FileNumber = file.GroupIndex;
        FromProject = file.FromProject;
        File = mission;
    }

    /// <summary>Index of the mission file in the file table.</summary>
    public int TableIndex { get; }

    /// <summary>Number of the mission among the user files (UPWT files start at 5).</summary>
    public int FileNumber { get; }

    /// <summary>True if the mission file comes from the project.</summary>
    public bool FromProject { get; }

    /// <summary>The mission file.</summary>
    public MissionFile File { get; }

    /// <summary>Class, vehicle and test.</summary>
    public TestId Id => File.Comm.Id;

    /// <summary>The island the mission is played on (COMM byte 3).</summary>
    public int Island => File.Comm.Island;

    /// <summary>The island the class menu shows for this vehicle and class (table in the game code).</summary>
    public int? ClassIsland { get; internal set; }

    /// <summary>Name of the title text ("…_N"), or null without text base.</summary>
    public string? TitleTextName => File.TextBase.Length > 0 ? File.TextBase + "_N" : null;

    /// <summary>Name of the task text ("…_M"), or null without text base.</summary>
    public string? TaskTextName => File.TextBase.Length > 0 ? File.TextBase + "_M" : null;

    /// <summary>Name of the hint text ("…_H"), or null without text base.</summary>
    public string? HintTextName => File.TextBase.Length > 0 ? File.TextBase + "_H" : null;

    /// <summary>Name of the points text of the mission screen ("…_S1", without slash).</summary>
    public string? MissionScreenTextName { get; internal set; }

    /// <summary>Name of the points text of the result sheet ("…_S2", with slash).</summary>
    public string? ResultSheetTextName { get; internal set; }

    /// <summary>The other missions that use the same mission screen text (empty if none).</summary>
    public IReadOnlyList<TestId> SharesMissionScreenTextWith { get; internal set; } = [];

    /// <summary>The other missions that use the same result sheet text (empty if none).</summary>
    public IReadOnlyList<TestId> SharesResultSheetTextWith { get; internal set; } = [];

    /// <summary>The other missions with the same title, task and hint texts (Cannonball targets of a level).</summary>
    public IReadOnlyList<TestId> SharesStatementWith { get; internal set; } = [];

    /// <summary>The point rows of the result sheet, top row first (empty rows left out).</summary>
    public IReadOnlyList<MissionPointRow> Rows { get; internal set; } = [];

    /// <summary>True if the vehicle is Birdman: no score sheet in the original game.</summary>
    public bool IsBirdman => Id.Vehicle == Vehicle.Birdman;

    /// <summary>Sum of the rows whose value is known.</summary>
    public int KnownRowSum => Rows.Sum(r => r.Value ?? 0);

    /// <summary>True if every row has a known value.</summary>
    public bool AllRowsKnown => Rows.All(r => r.Value is not null);
}

/// <summary>A game file as the catalog needs it.</summary>
internal sealed record GameFileRef(int TableIndex, int GroupIndex, bool FromProject);
