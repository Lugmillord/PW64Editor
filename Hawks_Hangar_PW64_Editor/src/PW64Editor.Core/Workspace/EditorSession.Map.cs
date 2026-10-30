using PW64Editor.Core.Map;
using PW64Editor.Core.Missions;

namespace PW64Editor.Core.Workspace;

/// <summary>The map editor's view of the project.</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// A library of the game files the map draws, reading the project's current files (replaced textures and models
    /// appear). Files read are kept by the library, so a new one is needed after files of the project changed.
    /// </summary>
    public MapLibrary CreateMapLibrary() => new(CleanFileSystem.Files, i => GetCurrentFileData(i, out _));

    /// <summary>
    /// A text that changes whenever a file of the project is added, removed or written (its replaced files with their
    /// times), a mission is removed or a class gets another island, so the map knows when to read the files again.
    /// </summary>
    public string FilesStamp() => string.Join("|", Project.GetOverrides()
            .Select(o => $"{o.TableIndex}:{(File.Exists(o.Path) ? File.GetLastWriteTimeUtc(o.Path).Ticks : 0)}"))
        + "/" + string.Join(",", Project.Settings.RemovedMissions)
        + "/" + string.Join(",", Project.Settings.Classes.Select(c => $"{(int)c.VehicleId}.{c.Class}.{c.Island}"));

    /// <summary>The island a mission is flown on: its class's island from the Classes tab, otherwise the mission's own.</summary>
    public int MissionIsland(MissionInfo mission) => ClassIsland(mission.Id.Vehicle, mission.Id.Class) is { } island and >= 0 and < 4
        ? island
        : mission.Island;
}
