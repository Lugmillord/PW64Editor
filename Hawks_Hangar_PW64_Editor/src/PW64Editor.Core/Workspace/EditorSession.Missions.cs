using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Text;

namespace PW64Editor.Core.Workspace;

/// <summary>Adding and removing missions (up to 128, up to five tests per class).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// The missions of the project in the order the game loads them: the game's mission files (the project's versions)
    /// without the removed ones, then the added ones.
    /// </summary>
    /// <exception cref="ProjectException">An added file is damaged or missing.</exception>
    /// <exception cref="IOException">A file of the project cannot be read.</exception>
    public IReadOnlyList<ProjectMission> CurrentMissions()
    {
        Dictionary<int, FileOverride> overrides = Project.GetOverrides().ToDictionary(o => o.TableIndex);
        var files = new List<GameFile>();
        foreach (GameFile file in CleanFileSystem.Files.Where(f => f.FileType == MissionFile.FormType))
        {
            files.Add(overrides.TryGetValue(file.TableIndex, out FileOverride? replacement) ? file with { Data = File.ReadAllBytes(replacement.Path) } : file);
        }

        files.AddRange(ProjectMissions.AddedFiles(Project, CleanFileSystem));
        return ProjectMissions.Missions(Project, files, CleanFileSystem.Files.Count);
    }

    /// <summary>The places of the missions, or null if they cannot be read (then the game's count for generated texts).</summary>
    private IReadOnlyList<TestId>? MissionIdsOrNull()
    {
        try
        {
            return [.. CurrentMissions().Select(m => m.Id)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectException or InvalidDataException)
        {
            return null;
        }
    }

    private bool HasOverride(int tableIndex) => Project.GetOverrides().Any(o => o.TableIndex == tableIndex);

    /// <summary>The file of an added mission as the build lays it out, or null for a mission of the game.</summary>
    private GameFile? AddedMissionFile(MissionInfo mission) =>
        mission.TableIndex >= CleanFileSystem.Files.Count
            ? new GameFile(mission.TableIndex, MissionFile.FormType, FileTypeLimits.UserFileGroup, mission.FileNumber, 0, [])
            : null;

    /// <summary>The saved-game layout of the project's missions (the game saves a result for each of them).</summary>
    public SaveGameLayout CurrentSaveLayout()
    {
        bool stage4 = Project.HasPatch(GamePatches.BonusGames);
        IReadOnlyList<TestId>? ids = MissionIdsOrNull();
        return ids is null
            ? stage4 ? SaveGameLayout.RetailWithBirdmanStage4 : SaveGameLayout.Retail
            : SaveGameLayout.FromTasks(ids, stage4, photoExtras: true);
    }

    /// <summary>
    /// Why a test cannot be added to a class (vehicle and class of the new test), or null if it can: adding needs the game
    /// patch "Bonus games and Birdman" and the code fix for more texts, only the license vehicles and the Birdman can get more
    /// tests, at most five per class and 128 missions.
    /// </summary>
    public string? CannotAddMission(Vehicle vehicle, int classIndex)
    {
        if (!Project.HasPatch(GamePatches.BonusGames))
        {
            return CoreText.F("Adding and removing missions needs the game patch \"{0}\" (File › Project settings).", CoreText.T(GamePatches.BonusGames.Name));
        }

        if (!CanAddTexts)
        {
            return CoreText.F("A new mission needs texts of its own, so the code fix \"{0}\" must be applied (File › Project settings).",
                CoreText.T(CodeFixes.ExpandedTexts.Name));
        }

        if (!MissionPositions.CanAddTests(vehicle))
        {
            return CoreText.T("Only Hang Glider, Rocket Belt, Gyrocopter and Birdman can get more missions: Cannonball has four targets per level, Sky Diving and the Jumble Hopper one mission per level.");
        }

        if (classIndex is < 0 or > 3)
        {
            return CoreText.T("The class does not exist.");
        }

        IReadOnlyList<ProjectMission> missions = CurrentMissions();
        if (missions.Count >= ProjectMissions.MaxMissions)
        {
            return CoreText.F("The game has room for {0} missions.", ProjectMissions.MaxMissions);
        }

        return missions.Count(m => m.Id.Vehicle == vehicle && m.Id.Class == classIndex) >= MissionPositions.MaxTests
            ? CoreText.F("A class has room for {0} missions.", MissionPositions.MaxTests)
            : null;
    }

    /// <summary>
    /// Adds a mission as a copy of another one of the same vehicle, as the last test of a class: its file behind the game's
    /// files (with the class's island and the texts of its place), its settings, and copies of its texts (title, task,
    /// hint, points texts) under the names of the new place.
    /// </summary>
    /// <param name="source">The mission to copy.</param>
    /// <param name="classIndex">The class of the new mission (the source's vehicle).</param>
    /// <returns>Class, vehicle and test of the new mission.</returns>
    /// <exception cref="ProjectException">The mission cannot be added (<see cref="CannotAddMission"/>) or a text has errors.</exception>
    public TestId AddMission(MissionInfo source, int classIndex)
    {
        Vehicle vehicle = source.Id.Vehicle;
        if (CannotAddMission(vehicle, classIndex) is { } problem)
        {
            throw new ProjectException(problem);
        }

        IReadOnlyList<ProjectMission> missions = CurrentMissions();
        var id = new TestId(classIndex, vehicle, missions.Count(m => m.Id.Vehicle == vehicle && m.Id.Class == classIndex));

        // The file: the new place, the class's island and the text base of the place.
        MissionComm comm = source.File.Comm.Clone();
        comm.Id = id;
        comm.Island = ClassIsland(vehicle, classIndex) ?? comm.Island;
        byte[] data = MissionFile.Parse(source.File.WithComm(comm)).WithTextBase(MissionPositions.StatementBase(id)!);
        int added = missions.Count(m => m.Added);
        int tableIndex = CleanFileSystem.Files.Count + added;
        int number = CleanFileSystem.Files.Count(f => f.Group == FileTypeLimits.UserFileGroup) + added;

        // The texts first (they can fail), then the file and the settings.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        AddStatementTexts(texts, source, id);
        AddPointsTexts(texts, source.MissionScreenTextName, source.ResultSheetTextName, id);
        WriteTexts(texts);

        Project.WriteOverride(new GameFile(tableIndex, MissionFile.FormType, FileTypeLimits.UserFileGroup, number, 0, data), data);
        Project.Settings.Missions.RemoveAll(m => m.Id == id);
        MissionSettings settings = source.Settings?.CloneFor(id) ?? MissionSettings.For(id);
        if (IsPhotoMission(source))
        {
            settings.PhotoMission = true;
        }

        // The result sheet shows the source's rows (the game's rows of the new place, if it has any, are another test's).
        if (Vehicles.IsMain(vehicle) && settings.ResultRows is null && ShownRows(source) is var rows && !rows.SequenceEqual(GameRows(id)))
        {
            settings.ResultRows = rows;
        }

        if (!settings.IsEmpty)
        {
            Project.Settings.Missions.Add(settings);
            Project.Settings.Missions.Sort((a, b) => (a.Vehicle, a.Class, a.Test).CompareTo((b.Vehicle, b.Class, b.Test)));
        }

        Project.Save();
        return id;
    }

    /// <summary>Why a mission cannot be removed, or null if it can (the class keeps at least one test).</summary>
    public string? CannotRemoveMission(MissionInfo mission)
    {
        if (!Project.HasPatch(GamePatches.BonusGames))
        {
            return CoreText.F("Adding and removing missions needs the game patch \"{0}\" (File › Project settings).", CoreText.T(GamePatches.BonusGames.Name));
        }

        if (!MissionPositions.CanAddTests(mission.Id.Vehicle))
        {
            return CoreText.T("Only missions of Hang Glider, Rocket Belt, Gyrocopter and Birdman can be removed.");
        }

        return CurrentMissions().Count(m => m.Id.Vehicle == mission.Id.Vehicle && m.Id.Class == mission.Id.Class) <= 1
            ? CoreText.T("Every class keeps at least one mission.")
            : null;
    }

    /// <summary>
    /// Removes a mission. The later tests of its class move up one place, with their texts (title, task, hint, points texts),
    /// their settings and the text base of their file. A mission the project added is deleted; one of the game stays in
    /// the ROM (so no file number moves) but leaves the mission list.
    /// </summary>
    /// <returns>Where the missions went (for an open saved game: the results go along with their missions).</returns>
    /// <exception cref="ProjectException">The mission cannot be removed, or a text has errors.</exception>
    public SavePlaceChanges RemoveMission(MissionInfo mission)
    {
        if (CannotRemoveMission(mission) is { } problem)
        {
            throw new ProjectException(problem);
        }

        TestId removed = mission.Id;
        IReadOnlyList<MissionInfo> catalog = LoadMissions().Missions;
        List<MissionInfo> later = [.. catalog.Where(m => m.Id.Vehicle == removed.Vehicle && m.Id.Class == removed.Class && m.Id.Test > removed.Test)
            .OrderBy(m => m.Id.Test)];

        // 1. Texts: each later test's texts move to the place in front of it.
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        GameTextLibrary library = LoadTexts();
        string? MarkupOf(string? name) => name is null ? null : library.Texts.FirstOrDefault(t => !t.IsFree && t.Name == name)?.Markup;
        MissionTables? tables = Tables();
        foreach (MissionInfo next in later)
        {
            var to = next.Id with { Test = next.Id.Test - 1 };
            foreach ((string? from, string? target) in new[]
                {
                    (next.TitleTextName, Statement(to, "_N")), (next.TaskTextName, Statement(to, "_M")), (next.HintTextName, Statement(to, "_H")),
                    (next.MissionScreenTextName, tables?.MissionScreenTextName(to)), (next.ResultSheetTextName, tables?.ResultSheetTextName(to)),
                })
            {
                if (target is not null && from is not null && from != target && MarkupOf(from) is { } markup)
                {
                    texts[target] = markup;
                }
            }
        }

        // The place that becomes free (the class's last one): its texts are removed if the editor added them.
        TestId last = later.Count > 0 ? later[^1].Id : removed;
        var vacated = new HashSet<int>();
        foreach (string? name in new[] { Statement(last, "_N"), Statement(last, "_M"), Statement(last, "_H"),
                     tables?.MissionScreenTextName(last), tables?.ResultSheetTextName(last) })
        {
            if (name is not null && !texts.ContainsKey(name) && library.Texts.FirstOrDefault(t => !t.IsFree && t.Name == name) is { } text
                && text.Index >= OriginalTextCount && (MissionPositions.IsNewPointsText(name) || name == Statement(last, name[^2..])))
            {
                vacated.Add(text.Index);
            }
        }

        WriteTexts(texts, vacated);

        // 2. The removed mission's file and settings.
        Project.Settings.Missions.RemoveAll(m => m.Id == removed);
        if (mission.TableIndex >= CleanFileSystem.Files.Count)
        {
            Project.RemoveOverride(mission.TableIndex);
        }
        else
        {
            Project.RemoveOverride(mission.TableIndex);
            if (!Project.Settings.RemovedMissions.Contains(mission.FileNumber))
            {
                Project.Settings.RemovedMissions.Add(mission.FileNumber);
                Project.Settings.RemovedMissions.Sort();
            }
        }

        // 3. The later tests: their place, their text base and their settings.
        foreach (MissionInfo next in later)
        {
            var to = next.Id with { Test = next.Id.Test - 1 };
            MissionComm comm = next.File.Comm.Clone();
            comm.Id = to;
            byte[] data = MissionFile.Parse(next.File.WithComm(comm)) is var file && MissionPositions.StatementBase(to) is { } textBase
                ? file.WithTextBase(textBase)
                : next.File.WithComm(comm);
            GameFile target = CleanFileSystem.Files.FirstOrDefault(f => f.TableIndex == next.TableIndex) ?? AddedMissionFile(next)!;
            Project.WriteOverride(target, data);
            foreach (MissionSettings settings in Project.Settings.Missions.Where(m => m.Id == next.Id))
            {
                settings.Test = to.Test;
            }

            // The rows go with the mission: the game's rows of the new place are those of another test.
            if (Vehicles.IsMain(to.Vehicle))
            {
                List<int> rows = ShownRows(next);
                MissionSettings? settings = Project.Settings.Missions.FirstOrDefault(m => m.Id == to);
                if (!rows.SequenceEqual(GameRows(to)))
                {
                    if (settings is null)
                    {
                        settings = MissionSettings.For(to);
                        Project.Settings.Missions.Add(settings);
                    }

                    settings.ResultRows = rows;
                }
                else if (settings is not null)
                {
                    settings.ResultRows = null; // the game's rows of the place
                }
            }
        }

        Project.Settings.Missions.RemoveAll(m => m.IsEmpty);

        // 4. Added files behind a removed added one close the gap (their table index and user file number go down by one).
        if (mission.TableIndex >= CleanFileSystem.Files.Count)
        {
            foreach (FileOverride file in Project.GetOverrides().Where(o => o.TableIndex > mission.TableIndex).OrderBy(o => o.TableIndex).ToList())
            {
                byte[] data = File.ReadAllBytes(file.Path);
                int number = CleanFileSystem.Files.Count(f => f.Group == FileTypeLimits.UserFileGroup) + (file.TableIndex - 1 - CleanFileSystem.Files.Count);
                File.Delete(file.Path);
                Project.WriteOverride(new GameFile(file.TableIndex - 1, MissionFile.FormType, FileTypeLimits.UserFileGroup, number, 0, data), data);
            }
        }

        Project.Settings.Missions.Sort((a, b) => (a.Vehicle, a.Class, a.Test).CompareTo((b.Vehicle, b.Class, b.Test)));
        Project.Save();
        return new SavePlaceChanges(later.ToDictionary(m => m.Id, m => m.Id with { Test = m.Id.Test - 1 }), [removed]);
    }

    /// <summary>True if the mission is a photo mission (<see cref="MissionSettings.PhotoMission"/> or one of the game's).</summary>
    public bool IsPhotoMission(MissionInfo mission) =>
        mission.Settings?.PhotoMission
        ?? (mission.TableIndex < CleanFileSystem.Files.Count && ProjectMissions.IsGamePhotoFile(CleanFileSystem, mission.TableIndex));

    private MissionTables? Tables() => Layout.Missions is { } layout
        ? new MissionTables(CleanRom.Data, layout, Project.HasPatch(GamePatches.BonusGames), ResultTables.BirdmanPointMissions(Project.Settings.Missions))
        : null;

    // The rows of a mission's result sheet, top row first: its own or the game's of its place.
    private static List<int> ShownRows(MissionInfo mission) =>
        mission.Settings?.ResultRows is { } own ? [.. own] : [.. mission.OriginalRowSources.Select(r => (int)r)];

    // The game's rows of a place, top row first (none for a place the game has no test at).
    private List<int> GameRows(TestId id) => Tables() is { } tables ? [.. tables.ResultRows(id).Where(r => r != 0).Reverse()] : [];

    private static string? Statement(TestId id, string suffix) => MissionPositions.StatementBase(id) is { } textBase ? textBase + suffix : null;

    /// <summary>The island of a class: the project's (Classes tab) or the game's (a new mission of the class gets it).</summary>
    public int? ClassIsland(Vehicle vehicle, int classIndex) =>
        Project.Settings.Classes.FirstOrDefault(c => c.VehicleId == vehicle && c.Class == classIndex)?.Island
        ?? (Layout.Missions is { } layout ? new MissionTables(CleanRom.Data, layout).ClassIsland(vehicle, classIndex) : null);

    // Title, task and hint of the source under the names of the new place.
    private void AddStatementTexts(Dictionary<string, string> texts, MissionInfo source, TestId id)
    {
        GameTextLibrary library = LoadTexts();
        foreach ((string? from, string suffix) in new[] { (source.TitleTextName, "_N"), (source.TaskTextName, "_M"), (source.HintTextName, "_H") })
        {
            if (from is not null && Statement(id, suffix) is { } target && library.Texts.FirstOrDefault(t => !t.IsFree && t.Name == from) is { } text)
            {
                texts[target] = text.Markup;
            }
        }
    }

    // The points texts of the source under the names of the new place (if the place has names of its own).
    private void AddPointsTexts(Dictionary<string, string> texts, string? screen, string? sheet, TestId id)
    {
        if (Tables() is not { } tables)
        {
            return;
        }

        GameTextLibrary library = LoadTexts();
        foreach ((string? from, string? target) in new[] { (screen, tables.MissionScreenTextName(id)), (sheet, tables.ResultSheetTextName(id)) })
        {
            if (from is not null && target is not null && from != target && library.Texts.FirstOrDefault(t => !t.IsFree && t.Name == from) is { } text)
            {
                texts[target] = text.Markup;
            }
        }
    }

    // Writes texts by name: existing ones get the new markup, missing ones are added as custom texts.
    private void WriteTexts(IReadOnlyDictionary<string, string> texts, IReadOnlyCollection<int>? remove = null)
    {
        remove ??= [];
        if (texts.Count == 0 && remove.Count == 0)
        {
            return;
        }

        GameTextLibrary library = LoadTexts();
        var changed = new Dictionary<int, string>();
        var added = new List<NewText>();
        var used = library.Texts.Where(t => !t.IsFree).Select(t => t.Index).ToHashSet();
        foreach ((string name, string markup) in texts)
        {
            if (library.Texts.FirstOrDefault(t => !t.IsFree && t.Name == name) is { } existing)
            {
                if (existing.Markup != markup)
                {
                    changed[existing.Index] = markup;
                }

                continue;
            }

            int index = CustomTexts.NextFreeIndex(used, OriginalTextCount)
                ?? throw new ProjectException(CoreText.F("The game can hold at most {0} texts.", CustomTexts.Capacity));
            used.Add(index);
            added.Add(new NewText(index, name, markup));
        }

        if (changed.Count > 0 || added.Count > 0 || remove.Count > 0)
        {
            SaveTexts(library, changed, added, remove);
        }
    }
}
