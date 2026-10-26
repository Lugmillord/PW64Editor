using PW64Editor.Core.Build;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Iff;
using PW64Editor.Core.Localization;
using PW64Editor.Core.Patching;
using PW64Editor.Core.Rom;

namespace PW64Editor.Core.Project;

/// <summary>
/// Builds a hack project (clean ROM + replacement files = hack ROM) and creates BPS patches.
/// </summary>
/// <remarks>
/// Building and patching are separate on purpose: saving in the editor rebuilds and overwrites
/// the hack ROM, while a patch is only created when the user asks for one.
/// </remarks>
public static class ProjectBuilder
{
    /// <summary>
    /// Builds the project in memory. Nothing is written to disk; use <see cref="WriteRom"/>.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="cleanRom">The clean base ROM. Must match the project's SHA-1.</param>
    /// <param name="layout">The fixed addresses of the game release.</param>
    /// <exception cref="ProjectException">Wrong base ROM, or an invalid replacement file.</exception>
    /// <exception cref="FileSystemFullException">The files do not fit.</exception>
    public static ProjectBuildResult Build(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(cleanRom);
        ArgumentNullException.ThrowIfNull(layout);

        string sha1 = cleanRom.ComputeSha1();
        if (!string.Equals(sha1, project.Settings.BaseRomSha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProjectException(
                CoreText.F("The ROM does not match this project (SHA-1 {0}, expected {1}).",
                    sha1, project.Settings.BaseRomSha1));
        }

        GameFileSystem fileSystem = GameFileSystem.Read(cleanRom, layout);
        List<GameFile> files = fileSystem.Files.ToList();
        IReadOnlyList<FileOverride> overrides = project.GetOverrides();

        foreach (FileOverride replacement in overrides.Where(o => o.TableIndex < fileSystem.Files.Count))
        {
            int position = files.FindIndex(f => f.TableIndex == replacement.TableIndex);
            string name = Path.GetFileName(replacement.Path);
            if (position < 0)
            {
                throw new ProjectException(CoreText.F("'{0}': the game has no file with table index {1}.",
                    name, replacement.TableIndex));
            }

            files[position] = ApplyOverride(files[position], replacement, name);
        }

        // Added missions: behind the game's files. Removed ones stay (so no file number moves), with "no vehicle" in their
        // COMM block, so nothing takes them for the mission of their place; the mission list leaves them out.
        files.AddRange(ProjectMissions.AddedFiles(project, fileSystem));
        for (int i = 0; i < fileSystem.Files.Count; i++)
        {
            if (files[i].FileType == Missions.MissionFile.FormType && project.Settings.RemovedMissions.Contains(files[i].GroupIndex))
            {
                Missions.MissionFile removed = Missions.MissionFile.Parse(files[i].Data);
                Missions.MissionComm comm = removed.Comm.Clone();
                comm.Id = comm.Id with { Vehicle = (SaveGame.Vehicle)Missions.MissionComm.NoVehicle };
                files[i] = files[i] with { Data = removed.WithComm(comm) };
            }
        }

        IReadOnlyList<ProjectMission> missionList = ProjectMissions.Missions(project, files, fileSystem.Files.Count);
        if (ProjectMissions.Problem([.. missionList.Select(m => m.Id)]) is { } missionProblem)
        {
            throw new ProjectException(missionProblem);
        }

        IReadOnlyList<SaveGame.TestId> missionIds = [.. missionList.Select(m => m.Id)];
        if (missionList.Select(m => Missions.MissionObjects.Problem(m.Comm)).FirstOrDefault(p => p is not null) is { } objectProblem)
        {
            throw new ProjectException(objectProblem);
        }

        AddLevelTexts(project, fileSystem, files, missionIds);
        CheckTextFile(project, fileSystem, files);
        ApplyMissionSettings(project, files);
        bool addedEnvironments = AddEnvironments(files);

        RomBuildResult romBuild;
        try
        {
            romBuild = RomBuilder.Build(cleanRom, files, layout,
                project.BuildOptions with
                {
                    SequenceFile = BuildMusic(project, cleanRom, layout),
                    DataPatches = MissionPatches(project, cleanRom, layout),
                    MinimumRomSize = HackRomSize,
                    GameModule = ModuleOptions(project, fileSystem, files) with
                    {
                        ResultTables = BonusResultTables(project, cleanRom, layout),
                        BirdmanPointMissions = project.HasPatch(GamePatches.BonusGames)
                            ? [.. Missions.ResultTables.BirdmanPointMissions(project.Settings.Missions)]
                            : [],
                        ClassTable = Classes.ClassLimits.Table(project.Settings.Classes, project.HasPatch(GamePatches.BonusGames),
                            HasOwnClassTexts(project), missionIds),
                        MissionFiles = [.. missionList.Select(m => m.File.GroupIndex)],
                        PhotoMissions = ProjectMissions.PhotoMissions(missionList, fileSystem, project.Settings.Missions),
                        UnlockTable = Classes.MenuUnlocks.Table(project.Settings.Unlocks, project.Settings.Features.AllFieldsOpen),
                        EnvironmentTable = addedEnvironments ? Missions.WeatherEnvironments.Table() : null,
                    },
                    GameModuleInfo = ModuleInfo(project),
                });
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("Build failed: {0}", ex.Message), ex);
        }

        return new ProjectBuildResult(romBuild, overrides.Count);
    }

    /// <summary>
    /// Writes the built ROM to <see cref="HackProject.OutputRomPath"/>, overwriting the previous one.
    /// </summary>
    /// <returns>The path written to.</returns>
    /// <exception cref="ProjectException">The output path is the clean ROM.</exception>
    public static string WriteRom(HackProject project, ProjectBuildResult result)
    {
        string path = Path.GetFullPath(project.OutputRomPath);

        // The one thing that must never happen: overwriting the clean ROM. Every future build
        // and every patch depends on it. (SetOutputRomPath checks this too; this is the last line
        // of defense, e.g. if project.user.json was edited by hand.)
        if (project.IsCleanRomPath(path))
        {
            throw new ProjectException(CoreText.T("The output ROM path points to the clean ROM. Choose a different " +
                "output path."));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        result.RomBuild.Rom.Save(path);
        return path;
    }

    /// <summary>
    /// Creates a BPS patch from the clean ROM to a built hack ROM and verifies it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The patch does not reproduce the ROM (a bug).</exception>
    public static byte[] CreatePatch(HackProject project, N64Rom cleanRom, N64Rom builtRom)
    {
        byte[] patch = BpsWriter.Create(cleanRom.Data, builtRom.Data, CreatePatchMetadata(project));

        // Self-check, so a broken patch can never leave the editor.
        byte[] check = BpsReader.Apply(cleanRom.Data, patch);
        if (!check.AsSpan().SequenceEqual(builtRom.Data))
        {
            throw new InvalidOperationException("Internal error: the created patch does not reproduce the built ROM.");
        }

        return patch;
    }

    /// <summary>
    /// A text file with more texts than the game's tables hold would crash the game: refuse to build it.
    /// </summary>
    /// <summary>
    /// The music file with the project's imported songs and changes of songs, or null if the
    /// project changes no song.
    /// </summary>
    /// <exception cref="ProjectException">A change cannot be made.</exception>
    public static byte[]? BuildMusic(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        if (layout.Audio is null)
        {
            return null;
        }

        try
        {
            Audio.SequenceBank bank = Audio.SequenceBank.Parse(
                cleanRom.Data.AsSpan(layout.Audio.SequenceOffset, layout.Audio.SequenceSize));
            var sequences = bank.Sequences.ToList();
            bool changed = false;
            for (int i = 0; i < sequences.Count; i++)
            {
                if (project.ReadImportedSong(i) is { } imported)
                {
                    sequences[i] = imported;
                    changed = true;
                }
            }

            foreach (Audio.SongSettings song in project.Settings.Music.Where(s => !s.IsEmpty))
            {
                if (song.Song < 0 || song.Song >= sequences.Count)
                {
                    throw new ProjectException(CoreText.F("The project changes song {0}, which the game does not have.", song.Song));
                }

                sequences[song.Song] = Audio.SequenceEditor.Apply(sequences[song.Song], song);
                changed = true;
            }

            if (!changed)
            {
                return null;
            }

            // The game reserves room for the longest original song only.
            int maxSize = bank.Sequences.Max(s => s.Length);
            for (int i = 0; i < sequences.Count; i++)
            {
                if (sequences[i].Length > maxSize)
                {
                    throw new ProjectException(CoreText.F("Song {0:D2} needs {1:N0} bytes, but the game has room for at most {2:N0}.",
                        i, sequences[i].Length, maxSize));
                }
            }

            return bank.With(sequences).Build();
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The music could not be changed: {0}", ex.Message), ex);
        }
    }

    /// <summary>
    /// Writes the mission settings that live in the mission files: the condition byte of every
    /// mission with conditions (so the file always matches them; projects saved by earlier
    /// versions may still have the game's "at least N" mode there, which would make the game say
    /// "please land" and remove the other objects), the start fuel and the widths of the runways.
    /// </summary>
    public static void ApplyMissionSettings(HackProject project, List<GameFile> files)
    {
        ApplyClassIslands(project, files);
        List<Missions.MissionSettings> withConditions = project.Settings.Missions
            .Where(m => m.Conditions is not null || m.StartFuel is not null || m.RunwayWidths is not null).ToList();
        if (withConditions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < files.Count; i++)
        {
            if (files[i].FileType != Missions.MissionFile.FormType)
            {
                continue;
            }

            Missions.MissionFile mission = Missions.MissionFile.Parse(files[i].Data);
            if (withConditions.FirstOrDefault(m => m.Id == mission.Comm.Id) is not { } settings)
            {
                continue;
            }

            if (settings.Conditions is { } conditions)
            {
                Missions.MissionComm comm = mission.Comm.Clone();
                conditions.ApplyTo(comm);
                if (!comm.ToArray().AsSpan().SequenceEqual(mission.Comm.ToArray()))
                {
                    files[i] = files[i] with { Data = mission.WithComm(comm) };
                    mission = Missions.MissionFile.Parse(files[i].Data);
                }
            }

            if (settings.StartFuel is { } fuel && mission.StartFuel != fuel)
            {
                files[i] = files[i] with { Data = mission.WithStartFuel(fuel) };
                mission = Missions.MissionFile.Parse(files[i].Data);
            }

            if (settings.RunwayWidths is { } widths && !mission.RunwayWidths.SequenceEqual(widths.Take(mission.RunwayWidths.Count)))
            {
                files[i] = files[i] with { Data = mission.WithRunwayWidths(widths) };
            }
        }
    }

    /// <summary>
    /// When a mission uses a time of day / weather its island lacks in the game (dark overcast on Holiday and Ever-Frost
    /// Island, sunset on Crescent and Ever-Frost Island): adds the environments and their levels to the game files
    /// (<see cref="Missions.WeatherEnvironments"/>). Returns true if it did; the module then gets the island table.
    /// </summary>
    /// <exception cref="ProjectException">The project replaces the environment or level file in a way the editor cannot extend.</exception>
    public static bool AddEnvironments(List<GameFile> files)
    {
        if (!Missions.WeatherEnvironments.Needed(files))
        {
            return false;
        }

        try
        {
            Missions.WeatherEnvironments.AddTo(files);
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The times of day the islands lack cannot be added: {0}", ex.Message), ex);
        }

        return true;
    }

    /// <summary>
    /// The islands of classes with an own island (Classes tab): COMM byte 3 of every mission of the class, which decides
    /// the island the game loads (taskInitTest). The test screen's table follows in <see cref="MissionPatches"/>.
    /// </summary>
    public static void ApplyClassIslands(HackProject project, List<GameFile> files)
    {
        List<Classes.ClassSettings> islands = [.. project.Settings.Classes.Where(c => c.Island is not null)];
        if (islands.Count == 0)
        {
            return;
        }

        for (int i = 0; i < files.Count; i++)
        {
            if (files[i].FileType != Missions.MissionFile.FormType)
            {
                continue;
            }

            Missions.MissionFile mission = Missions.MissionFile.Parse(files[i].Data);
            SaveGame.TestId id = mission.Comm.Id;
            if (islands.FirstOrDefault(c => c.VehicleId == id.Vehicle && c.Class == id.Class) is { Island: { } island }
                && mission.Comm.Island != island)
            {
                Missions.MissionComm comm = mission.Comm.Clone();
                comm.Island = island;
                files[i] = files[i] with { Data = mission.WithComm(comm) };
            }
        }
    }

    /// <summary>
    /// What the game module does for this project: the features (debug output, live values), the
    /// mission conditions it checks with the HUD text "Time requirement met", and the distance row.
    /// </summary>
    public static Code.GameModuleOptions ModuleOptions(HackProject project, GameFileSystem cleanFileSystem, IReadOnlyList<GameFile> files)
    {
        IReadOnlyList<GameModuleCondition> conditions = ModuleConditions(project, files);
        IReadOnlyList<ushort> metText = [];
        if (conditions.Count > 0)
        {
            var codec = new Text.TextCodec(Text.TextFont.Load(cleanFileSystem));
            Text.TextEncodeResult encoded = codec.Encode(RequirementMetText);
            if (encoded.Success)
            {
                // As the game holds texts in RAM (textLoadBlock): line break 0xFE -> 0xFFE, end 0xFF -> 0xFFFF.
                metText = encoded.Codes.Select(c => c switch
                {
                    Text.TextCodec.CodeLineBreak => (ushort)0x0FFE,
                    Text.TextCodec.CodeEnd => (ushort)0xFFFF,
                    _ => c,
                }).ToList();
            }
        }

        IReadOnlyList<IReadOnlyList<ushort>> countdown = [];
        if (conditions.Any(c => c.HardEnd != HardEndScoring.Retail))
        {
            var codec = new Text.TextCodec(Text.TextFont.Load(cleanFileSystem));
            var texts = CountdownTexts.Select(t => codec.Encode(t)).ToList();
            if (texts.All(t => t.Success))
            {
                // One line each: without the line break in front of the end (it would count as a space when centred).
                countdown = texts.Select(t => (IReadOnlyList<ushort>)ToRamCodes(t.Codes).Where(c => c != 0x0FFE).ToList()).ToList();
            }
        }

        IReadOnlyList<ushort> digits = [];
        if (NeedsPenaltyMessages(files))
        {
            var codec = new Text.TextCodec(Text.TextFont.Load(cleanFileSystem));
            Text.TextEncodeResult encoded = codec.Encode("[b]0123456789[/b]");
            if (encoded.Success)
            {
                digits = encoded.Codes.Where(c => c is not (Text.TextCodec.CodeLineBreak or Text.TextCodec.CodeEnd)).ToList();
            }
        }

        return project.Settings.Features.ModuleOptions with
        {
            RequireExpansionPak = true,
            GoalObjects = project.HasPatch(GamePatches.GoalObjects),
            Conditions = conditions,
            RequirementMetText = metText,
            PenaltyDigits = digits.Count == 10 ? digits : [],
            CountdownTexts = countdown,
            DistanceRow = project.Settings.Missions.Any(m => m.ResultRows?.Contains((int)Missions.PointSource.Distance) == true),
        };
    }

    /// <summary>
    /// True if a mission deducts other points than its fixed HUD message says: "5 pts. deducted" for a
    /// Gyrocopter hit, "2 pts. deducted" for a Rocket Belt hit and when the Jumble Hopper leaves the area.
    /// </summary>
    public static bool NeedsPenaltyMessages(IReadOnlyList<GameFile> files) =>
        files.Where(f => f.FileType == Missions.MissionFile.FormType)
            .Select(f => Missions.MissionFile.Parse(f.Data).Comm)
            .Any(comm => comm.Id.Vehicle switch
            {
                SaveGame.Vehicle.Gyrocopter => comm.HitDeduction != -5,
                SaveGame.Vehicle.RocketBelt => comm.HitDeduction != -2,
                SaveGame.Vehicle.JumbleHopper => comm.OutOfAreaDeduction != 2,
                _ => false,
            });

    /// <summary>
    /// Size of every hack ROM built from a project (16 MiB): room for later growth, so the ROM size
    /// never changes between versions of a hack. Every hack also requires the Expansion Pak.
    /// </summary>
    public const int HackRomSize = 16 * 1024 * 1024;

    /// <summary>The countdown of the hard end time (in the game's language, English; bold like the HUD numbers).</summary>
    public static readonly IReadOnlyList<string> CountdownTexts =
        ["[b]5[/b]", "[b]4[/b]", "[b]3[/b]", "[b]2[/b]", "[b]1[/b]", "[b]Time up![/b]"];

    /// <summary>Text codes as the game holds them in RAM (textLoadBlock): line break 0xFE -> 0xFFE, end 0xFF -> 0xFFFF.</summary>
    private static List<ushort> ToRamCodes(IEnumerable<ushort> codes) => codes.Select(c => c switch
    {
        Text.TextCodec.CodeLineBreak => (ushort)0x0FFE,
        Text.TextCodec.CodeEnd => (ushort)0xFFFF,
        _ => c,
    }).ToList();

    /// <summary>The HUD message when the conditions of the time points are met (in the game's language, English).</summary>
    public const string RequirementMetText = "Time requirement met";

    /// <summary>
    /// The missions with an entry in the game module's table (the data area), read from the mission
    /// files as they are built: conditions only the module can check (combinations the retail
    /// condition byte cannot express), unlimited fuel and the scoring of the hard end time.
    /// </summary>
    public static IReadOnlyList<GameModuleCondition> ModuleConditions(HackProject project, IReadOnlyList<GameFile> files)
    {
        var result = new List<GameModuleCondition>();
        foreach (GameFile file in files.Where(f => f.FileType == Missions.MissionFile.FormType))
        {
            Missions.MissionComm comm = Missions.MissionFile.Parse(file.Data).Comm;
            Missions.MissionSettings? settings = project.Settings.Missions.FirstOrDefault(m => m.Id == comm.Id);
            bool fuel = settings?.UnlimitedFuel == true && comm.Id.Vehicle is SaveGame.Vehicle.RocketBelt or SaveGame.Vehicle.Gyrocopter;
            HardEndScoring hardEnd = comm.HardEndTime <= 0 ? HardEndScoring.Retail
                : settings?.HardEndZero == true ? HardEndScoring.Zero : HardEndScoring.Scored;
            byte music = hardEnd == HardEndScoring.Retail ? (byte)0
                : Missions.HardEndJingles.Music(comm.Id.Vehicle, Missions.HardEndJingles.Effective(settings));
            // Landing: the Gyrocopter's touchdown curve, the pad radius and the speed limits of the vehicle.
            SaveGame.Vehicle vehicle = comm.Id.Vehicle;
            IReadOnlyList<Missions.CurvePoint>? touchdown = vehicle == SaveGame.Vehicle.Gyrocopter && settings?.TouchdownCurve is { Count: > 0 } curve
                ? curve
                : null;
            float? padRadius = settings is not null && settings.Landing.TryGetValue(Missions.LandingValue.PadRadius, out float radius)
                && Missions.LandingValues.UsesPads(vehicle)
                ? radius
                : null;
            // The Birdman's values only count in its missions with points (game patch "Bonus games and Birdman").
            IReadOnlyList<Missions.LandingValue> speeds = vehicle != SaveGame.Vehicle.Birdman
                || (settings?.BirdmanPoints == true && project.HasPatch(GamePatches.BonusGames))
                ? Missions.LandingValues.SpeedsOf(vehicle)
                : [];
            IReadOnlyList<float> limits = settings is not null && speeds.Any(settings.Landing.ContainsKey)
                ? speeds.Select(v => Missions.LandingValues.Effective(settings, v)).ToList()
                : [];

            GameModuleCondition entry;
            if (settings?.Conditions is { } conditions && conditions.NeedsModule(comm))
            {
                entry = conditions.ToModuleCondition(comm);
            }
            else
            {
                // No conditions of its own: the game's condition byte keeps counting.
                SaveGame.TestId id = comm.Id;
                entry = new GameModuleCondition((byte)id.Class, (byte)id.Vehicle, (byte)id.Test, false, null,
                    new byte[GameModule.ObjectTypes], null);
            }

            entry = entry with
            {
                UnlimitedFuel = fuel,
                HardEnd = hardEnd,
                HardEndMusic = music,
                TouchdownCurve = touchdown,
                PadRadius = padRadius,
                VehicleLimits = limits,
                Weather = Missions.WeatherCatalog.ToModule(settings?.Weather, comm),
            };
            if (entry.HasConditions || fuel || hardEnd != HardEndScoring.Retail || entry.HasLandingValues || entry.Weather is not null)
            {
                result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>The line the debug output starts with: project, version and build time.</summary>
    private static string ModuleInfo(HackProject project) =>
        $"project \"{project.Settings.Name}\" {project.Settings.Version}, built {DateTime.Now:yyyy-MM-dd HH:mm}";

    /// <summary>The mission changes of the game code: the changed result rows (a table) and the hidden "View Demo" buttons.</summary>
    /// <exception cref="ProjectException">A mission's rows cannot be stored.</exception>
    public static IReadOnlyList<DataPatch> MissionPatches(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        var patches = new List<DataPatch>();

        // The "View Demo" button of the Beginner hint screens.
        List<SaveGame.Vehicle> hiddenDemos = project.Settings.Missions
            .Where(m => m.HideDemo && Missions.DemoButton.HasButton(m.Id))
            .Select(m => m.Id.Vehicle)
            .Distinct()
            .ToList();
        if (hiddenDemos.Count > 0)
        {
            if (layout.Missions is null || !Missions.DemoButton.MatchesRom(cleanRom.Data))
            {
                throw new ProjectException(CoreText.T("The \"View Demo\" button can only be hidden in the US version of the game."));
            }

            patches.AddRange(Missions.DemoButton.Patches(hiddenDemos));
        }

        // The island of a class on the test screen (globe, island name: D_8034F420[7][4] in test_summary.c).
        foreach (Classes.ClassSettings island in project.Settings.Classes.Where(c => c.Island is not null))
        {
            if (layout.Missions is null || island.Island is not (>= 0 and < 4) || island.Vehicle is < 0 or > 6 || island.Class is < 0 or > 3)
            {
                throw new ProjectException(CoreText.T("The island of a class cannot be stored in this game version."));
            }

            patches.Add(new DataPatch(layout.Missions.ClassIslandsOffset + (island.Vehicle * 4) + island.Class, [(byte)island.Island.Value], "class island"));
        }

        // The Birdman and the places the game has no test at (a fourth test, ...) have no entry in the game's table: their
        // rows only exist in the tables of the game patch "Bonus games and Birdman" (BonusResultTables).
        List<Missions.MissionSettings> changed = project.Settings.Missions
            .Where(m => m.ResultRows is not null && m.Id.Vehicle != SaveGame.Vehicle.Birdman
                && Missions.MissionPositions.NewPointsTextName(m.Id, false) is null).ToList();
        if (changed.Count == 0)
        {
            return patches;
        }

        if (layout.Missions is null)
        {
            throw new ProjectException(CoreText.T("The result sheets of this game version are unknown, so changed point rows cannot be built."));
        }

        var tables = new Missions.MissionTables(cleanRom.Data, layout.Missions);
        foreach (Missions.MissionSettings mission in changed)
        {
            List<int> rows = mission.ResultRows!;
            if (tables.ResultRowsOffset(mission.Id) is not { } offset || rows.Count > Missions.MissionLayout.ResultRows
                || rows.Any(r => r is < 0 or > 255 || !Missions.PointSources.IsKnown(r)))
            {
                throw new ProjectException(CoreText.F("The point rows of {0} cannot be stored.", Missions.MissionCatalog.Label(mission.Id)));
            }

            // The table lists the bottom row first; empty rows are 0.
            byte[] data = new byte[Missions.MissionLayout.ResultRows];
            for (int i = 0; i < rows.Count; i++)
            {
                data[i] = (byte)rows[rows.Count - 1 - i];
            }

            patches.Add(new DataPatch(offset, data, "result rows"));
        }

        return patches;
    }

    /// <summary>
    /// The result tables of the game patch <see cref="GamePatches.BonusGames"/> (with the project's point rows), or null
    /// without the patch.
    /// </summary>
    /// <exception cref="ProjectException">The patch is applied, but the game version is not the US one.</exception>
    public static byte[]? BonusResultTables(HackProject project, N64Rom cleanRom, RomLayout layout)
    {
        if (!project.HasPatch(GamePatches.BonusGames))
        {
            return null;
        }

        if (layout.Missions is null)
        {
            throw new ProjectException(CoreText.T("The result sheets of this game version are unknown, so changed point rows cannot be built."));
        }

        return Missions.ResultTables.Create(cleanRom.Data, layout.Missions, project.Settings.Missions);
    }

    /// <summary>
    /// The texts the editor adds or writes itself, added to the built text file so the game always finds them (until a
    /// text is saved they are only in memory, see <see cref="Workspace.EditorSession.LoadTexts"/>): with the game patch
    /// <see cref="GamePatches.BonusGames"/> the texts of the levels and of the Birdman missions with points, and the
    /// medal texts of classes with own limits (<see cref="Classes.ClassTexts"/>).
    /// </summary>
    private static void AddLevelTexts(HackProject project, GameFileSystem cleanFileSystem, List<GameFile> files, IReadOnlyList<SaveGame.TestId> missions)
    {
        GameFile originalText;
        try
        {
            originalText = Text.GameTextFile.FindFile(cleanFileSystem);
        }
        catch (InvalidDataException)
        {
            return; // not the retail layout (test data)
        }

        int position = files.FindIndex(f => f.TableIndex == originalText.TableIndex);
        try
        {
            if (AddGeneratedTexts(project.HasPatch(GamePatches.BonusGames), HasOwnClassTexts(project), project.Settings.Missions,
                    project.Settings.Classes, files[position].Data, Text.TextFont.Load(cleanFileSystem), missions) is { } withTexts)
            {
                files[position] = files[position] with { Data = withTexts };
            }
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The project's text file is damaged: {0}", ex.Message), ex);
        }
    }

    /// <summary>True if classes with own limits get own medal texts: the code fix for more texts is applied.</summary>
    public static bool HasOwnClassTexts(HackProject project) => project.Settings.AppliedCodeFixes.Contains(CodeFixes.ExpandedTexts.Id);

    /// <summary>
    /// Adds the texts the editor writes itself to a text file: the level and Birdman texts of the game patch "Bonus games
    /// and Birdman" (<paramref name="bonusPatch"/>) and the medal texts of classes with own limits (<paramref name="classTexts"/>).
    /// </summary>
    /// <returns>The new text file, or null if nothing changed.</returns>
    /// <exception cref="InvalidDataException">The text file is damaged.</exception>
    /// <param name="missionIds">The missions of the project (null: the game's own): points texts of new places, TRY4/TRY5 and the
    /// medal texts of classes with another number of tests.</param>
    public static byte[]? AddGeneratedTexts(bool bonusPatch, bool classTexts, IEnumerable<Missions.MissionSettings> missions,
        IEnumerable<Classes.ClassSettings> classes, byte[] textFile, Text.TextFont font, IReadOnlyList<SaveGame.TestId>? missionIds = null)
    {
        List<Classes.ClassSettings> classList = [.. classes];
        byte[] data = textFile;
        bool changed = false;
        if (bonusPatch)
        {
            Dictionary<int, IReadOnlyList<int>> birdmanLimits = classList
                .Where(c => c.VehicleId == SaveGame.Vehicle.Birdman && c.Medals is { Count: 3 })
                .ToDictionary(c => c.Class, c => (IReadOnlyList<int>)c.Medals!);
            if (Missions.ResultTables.AddLevelTexts(data, Missions.ResultTables.BirdmanPointMissions(missions),
                    Workspace.EditorSession.EncodeFor(font), birdmanLimits, missionIds) is { } withLevels)
            {
                data = withLevels;
                changed = true;
            }
        }

        if (classTexts && Classes.ClassTexts.Add(data, classList, font, missionIds) is { } withClasses)
        {
            data = withClasses;
            changed = true;
        }

        return changed ? data : null;
    }

    private static void CheckTextFile(HackProject project, GameFileSystem cleanFileSystem, List<GameFile> files)
    {
        GameFile originalText;
        try
        {
            originalText = Text.GameTextFile.FindFile(cleanFileSystem);
        }
        catch (InvalidDataException)
        {
            return; // not the retail layout (test data): nothing to check
        }

        GameFile current = files.First(f => f.TableIndex == originalText.TableIndex);
        if (ReferenceEquals(current.Data, originalText.Data))
        {
            return;
        }

        int count, originalCount;
        try
        {
            count = Text.GameTextFile.Parse(current.Data).Entries.Count;
            originalCount = Text.GameTextFile.Parse(originalText.Data).Entries.Count;
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("The project's text file is damaged: {0}", ex.Message), ex);
        }

        if (Text.CustomTexts.CountProblem(count, originalCount,
                project.Settings.AppliedCodeFixes.Contains(Code.CodeFixes.ExpandedTexts.Id)) is { } problem)
        {
            throw new ProjectException(problem);
        }
    }

    private static GameFile ApplyOverride(GameFile original, FileOverride replacement, string name)
    {
        if (!string.Equals(replacement.FileType, original.FileType, StringComparison.Ordinal))
        {
            throw new ProjectException(
                CoreText.F("'{0}': the name says type '{1}', but file {2} is '{3}'.",
                    name, replacement.FileType, original.TableIndex, original.FileType));
        }

        byte[] data = File.ReadAllBytes(replacement.Path);

        string formType;
        try
        {
            formType = IffForm.ReadHeader(data).FormType;
        }
        catch (InvalidDataException ex)
        {
            throw new ProjectException(CoreText.F("'{0}' is not a valid game file: {1}", name, ex.Message), ex);
        }

        if (formType != original.FileType)
        {
            throw new ProjectException(
                CoreText.F("'{0}': its content is a '{1}' file, but file {2} must be '{3}'.",
                    name, formType, original.TableIndex, original.FileType));
        }

        return original with { Data = data };
    }

    /// <summary>
    /// Text stored inside the BPS patch. Most patchers ignore it, some display it.
    /// </summary>
    private static string CreatePatchMetadata(HackProject project)
    {
        ProjectSettings s = project.Settings;
        string author = s.Author.Length > 0 ? $" by {s.Author}" : string.Empty;
        return $"{s.Name} {s.Version}{author} - made with Hawk's Hangar for Pilotwings 64 (USA)";
    }
}
