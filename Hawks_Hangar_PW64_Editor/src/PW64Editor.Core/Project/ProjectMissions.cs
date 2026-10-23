using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;
using PW64Editor.Core.SaveGame;

namespace PW64Editor.Core.Project;

/// <summary>One mission of a project: its file and where it lies.</summary>
/// <param name="File">The mission file as the build uses it (table index and user file number in the built ROM).</param>
/// <param name="Added">True for a mission the project adds (a file behind the game's files).</param>
public sealed record ProjectMission(GameFile File, bool Added)
{
    /// <summary>The mission's COMM block.</summary>
    public MissionComm Comm { get; } = MissionFile.Parse(File.Data).Comm;

    /// <summary>Class, vehicle and test.</summary>
    public TestId Id => Comm.Id;
}

/// <summary>
/// The missions of a project: the game's mission files without the removed ones (<see cref="ProjectSettings.RemovedMissions"/>)
/// and the added ones, files of the project behind the game's files (table index from the game's file count on, user file
/// number from 111 on), so no number of the game moves.
/// </summary>
public static class ProjectMissions
{
    /// <summary>Most missions of a hack (the mission list of the lower data area, the settings table, the saved game).</summary>
    public const int MaxMissions = DataExtension.MaxMissions;

    /// <summary>
    /// The files the project adds behind the game's (replacement files with a table index behind the game's last file), in
    /// table order with their table index and user file number.
    /// </summary>
    /// <exception cref="ProjectException">An added file is no mission, is damaged, or the numbers have a gap.</exception>
    public static IReadOnlyList<GameFile> AddedFiles(HackProject project, GameFileSystem clean)
    {
        var result = new List<GameFile>();
        int next = clean.Files.Count;
        int userNumber = clean.Files.Count(f => f.Group == FileTypeLimits.UserFileGroup);
        foreach (FileOverride added in project.GetOverrides().Where(o => o.TableIndex >= clean.Files.Count).OrderBy(o => o.TableIndex))
        {
            string name = Path.GetFileName(added.Path);
            if (added.TableIndex != next)
            {
                throw new ProjectException(CoreText.F("'{0}': the added mission files must follow each other (file {1} is missing).", name, next));
            }

            if (added.FileType != MissionFile.FormType)
            {
                throw new ProjectException(CoreText.F("'{0}': only missions can be added behind the game's files.", name));
            }

            byte[] data = File.ReadAllBytes(added.Path);
            try
            {
                if (IffForm.ReadHeader(data).FormType != MissionFile.FormType)
                {
                    throw new InvalidDataException(CoreText.T("The file is no mission."));
                }

                MissionFile.Parse(data);
            }
            catch (InvalidDataException ex)
            {
                throw new ProjectException(CoreText.F("'{0}' is not a valid game file: {1}", name, ex.Message), ex);
            }

            result.Add(new GameFile(next, MissionFile.FormType, FileTypeLimits.UserFileGroup, userNumber, 0, data));
            next++;
            userNumber++;
        }

        return result;
    }

    /// <summary>
    /// The missions in the order the game loads them: the game's (in the order of its mission list, without the removed
    /// ones), then the added ones.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="files">All files as the build uses them (the game's files with the project's replacements and the added ones).</param>
    /// <param name="cleanFileCount">Number of the game's files.</param>
    public static IReadOnlyList<ProjectMission> Missions(HackProject project, IReadOnlyList<GameFile> files, int cleanFileCount)
    {
        var byNumber = files.Where(f => f.FileType == MissionFile.FormType).ToDictionary(f => f.GroupIndex);
        var result = new List<ProjectMission>();
        foreach (int number in DataExtension.GameMissionFiles.Where(n => !project.Settings.RemovedMissions.Contains(n)))
        {
            if (byNumber.TryGetValue(number, out GameFile? file) && file.TableIndex < cleanFileCount)
            {
                result.Add(new ProjectMission(file, false));
            }
        }

        result.AddRange(files.Where(f => f.FileType == MissionFile.FormType && f.TableIndex >= cleanFileCount)
            .OrderBy(f => f.TableIndex)
            .Select(f => new ProjectMission(f, true)));
        return result;
    }

    /// <summary>
    /// What is wrong with the missions, or null: more than <see cref="MaxMissions"/>, two missions at one place, a class
    /// whose tests do not start at 1 or have a gap, more than five tests.
    /// </summary>
    public static string? Problem(IReadOnlyList<TestId> missions)
    {
        if (missions.Count > MaxMissions)
        {
            return CoreText.F("The game has room for {0} missions, the project has {1}.", MaxMissions, missions.Count);
        }

        if (missions.GroupBy(m => m).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            return CoreText.F("Two missions have the same place: {0}.", MissionCatalog.Label(twice.Key));
        }

        foreach (var cls in missions.GroupBy(m => (m.Vehicle, m.Class)))
        {
            List<int> tests = [.. cls.Select(m => m.Test).Order()];
            if (tests.Count > MissionPositions.MaxTests || tests.Where((t, i) => t != i).Any())
            {
                return CoreText.F("The tests of {0} {1} must be numbered 1 to at most {2} without gaps.",
                    Vehicles.Name(cls.Key.Vehicle), MissionCatalog.ClassName(cls.Key.Vehicle, cls.Key.Class), MissionPositions.MaxTests);
            }
        }

        return null;
    }

    /// <summary>
    /// The photo missions: those with <see cref="MissionSettings.PhotoMission"/> set, and the game's photo missions (by
    /// their file, wherever their test moved).
    /// </summary>
    /// <param name="missions">The missions of the project.</param>
    /// <param name="clean">The game's files (the original place of each of its mission files).</param>
    /// <param name="settings">The project's settings of the missions.</param>
    public static IReadOnlyList<TestId> PhotoMissions(IReadOnlyList<ProjectMission> missions, GameFileSystem clean, IEnumerable<MissionSettings> settings)
    {
        Dictionary<TestId, MissionSettings> byId = settings.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.Last());
        var result = new List<TestId>();
        foreach (ProjectMission mission in missions)
        {
            bool photo = byId.GetValueOrDefault(mission.Id)?.PhotoMission
                ?? (!mission.Added && IsGamePhotoFile(clean, mission.File.TableIndex));
            if (photo)
            {
                result.Add(mission.Id);
            }
        }

        return result;
    }

    /// <summary>True if the game's mission file at this table index is one of the game's photo missions.</summary>
    public static bool IsGamePhotoFile(GameFileSystem clean, int tableIndex) =>
        clean.Files.FirstOrDefault(f => f.TableIndex == tableIndex) is { FileType: MissionFile.FormType } file
        && DataExtension.GamePhotoMissions.Contains(MissionFile.Parse(file.Data).Comm.Id);
}
