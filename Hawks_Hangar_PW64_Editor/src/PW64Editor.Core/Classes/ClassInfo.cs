using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Classes;

/// <summary>A class of a vehicle as the Classes tab shows it (<see cref="Workspace.EditorSession.LoadClasses"/>).</summary>
/// <param name="Vehicle">The vehicle.</param>
/// <param name="Class">Class, level of a bonus game or stage of the Birdman.</param>
/// <param name="Settings">The project's settings of the class, or null for none.</param>
/// <param name="OriginalMedals">The original limits (bronze, silver, gold).</param>
/// <param name="DefaultMedals">The limits without own ones (automatic for a Birdman stage with points, or original).</param>
/// <param name="Medals">The limits that count (own, automatic for a Birdman stage with points, or original).</param>
/// <param name="Perfect">Points of a perfect score.</param>
/// <param name="TestCount">Missions of the class.</param>
/// <param name="BirdmanPointMissions">Birdman: missions with points of the stage (0 for other vehicles).</param>
/// <param name="OriginalIsland">The island of the game's table, or null if unknown.</param>
/// <param name="Island">The island that counts.</param>
/// <param name="OwnTexts">True if own limits get own medal texts (code fix for more texts applied, not the Birdman).</param>
public sealed record ClassInfo(Vehicle Vehicle, int Class, ClassSettings? Settings, IReadOnlyList<int> OriginalMedals,
    IReadOnlyList<int> DefaultMedals, IReadOnlyList<int> Medals, int Perfect, int TestCount, int BirdmanPointMissions, int? OriginalIsland, int? Island, bool OwnTexts)
{
    /// <summary>True if the class has own limits.</summary>
    public bool HasOwnMedals => Settings?.Medals is { Count: 3 };

    /// <summary>True for a Birdman stage whose limits follow its missions with points.</summary>
    public bool IsAutomatic => !HasOwnMedals && Vehicle == Vehicle.Birdman && BirdmanPointMissions > 0;
}
