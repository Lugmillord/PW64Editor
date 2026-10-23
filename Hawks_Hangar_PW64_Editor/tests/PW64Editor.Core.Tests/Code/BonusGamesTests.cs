using System.Buffers.Binary;
using System.Text;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Code;

/// <summary>The game patch "Bonus games and Birdman" (<see cref="GamePatches.BonusGames"/>).</summary>
public class BonusGamesTests
{
    private const uint AppRamDelta = 0x80278AD0; // RAM address - ROM offset of the game code and its data

    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static string StringAt(byte[] rom, uint address)
    {
        int offset = (int)(address - AppRamDelta);
        int end = Array.IndexOf(rom, (byte)0, offset);
        return Encoding.ASCII.GetString(rom, offset, end - offset);
    }

    /// <summary>The data area image of a built ROM (the module header names it).</summary>
    private static DataImages DataImage(byte[] built) => GameModule.ReadDataImages(built, GameModule.ReadHeader(built)!)!;

    private static readonly int Sheet = (int)(ResultTables.ResultSheetNamesAddress - ResultTables.Address);

    /// <summary>A name in the tables: a pointer into the game's data, or into the tables (the new names).</summary>
    private static string Name(byte[] rom, byte[] tables, uint pointer) =>
        pointer >= ResultTables.Address && pointer < ResultTables.Address + tables.Length
            ? Encoding.ASCII.GetString(tables, (int)(pointer - ResultTables.Address), Array.IndexOf(tables, (byte)0, (int)(pointer - ResultTables.Address)) - (int)(pointer - ResultTables.Address))
            : StringAt(rom, pointer);

    [RealRomFact]
    public void Patch_FitsTheCleanRom_AndTheBirdmanNamesAreWhereExpected()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.Equal(CodeFixState.NotApplied,
            GamePatches.BonusGames.AsCodeFix.GetState(rom.Data, CodeFixes.CodeStart, CodeFixes.CodeEnd(RomLayout.PilotwingsUsa)));
        Assert.Equal("BD_ALL_S1", StringAt(rom.Data, ResultTables.BirdmanMissionScreenName));
        Assert.Equal("BD_ALL_S2", StringAt(rom.Data, ResultTables.BirdmanResultSheetName));
        Assert.Equal(new MedalLimits(0, 0, 0), new MissionTables(rom.Data, RomLayout.PilotwingsUsa.Missions!).MedalLimits()[7]);

        // The new tables lie in the lower data area, in front of the class table.
        Assert.Equal(0x801F1100u, ResultTables.Address);
        Assert.True(ResultTables.Address + ResultTables.Size <= PW64Editor.Core.Classes.ClassLimits.TableAddress);
    }

    [RealRomFact]
    public void Tables_KeepWhatTheGameRead_AndGiveTheLevelsTheirOwnTexts()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionLayout layout = RomLayout.PilotwingsUsa.Missions!;
        var clean = new MissionTables(rom.Data, layout);
        MissionSettings own = MissionSettings.For(new TestId(1, Vehicle.HangGlider, 1));
        own.ResultRows = [(int)PointSource.Time, (int)PointSource.Rings];
        byte[] tables = ResultTables.Create(rom.Data, layout, [own]);
        Assert.Equal(0, tables.Length % 8);

        for (int v = 0; v < ResultTables.VehicleCount; v++)
        {
            for (int c = 0; c < ResultTables.GameClasses; c++)
            {
                for (int t = 0; t < ResultTables.Tests; t++)
                {
                    var id = new TestId(c, (Vehicle)v, t);
                    int entry = (((v * 8) + c) * 5) + t;
                    uint screen = Word(tables, entry * 4);
                    uint sheet = Word(tables, Sheet + (entry * 4));
                    int rows = ResultTables.RowsOffset(id)!.Value;
                    if (id.Vehicle == Vehicle.Birdman)
                    {
                        Assert.Equal(ResultTables.BirdmanMissionScreenName, screen);
                        Assert.Equal(ResultTables.BirdmanResultSheetName, sheet);
                        continue;
                    }

                    string? screenName = ResultTables.LevelTextName(id, false) ?? (t < 3 ? clean.MissionScreenTextName(id) : null);
                    string? sheetName = ResultTables.LevelTextName(id, true) ?? (t < 3 ? clean.ResultSheetTextName(id) : null);
                    if (screenName is not null)
                    {
                        Assert.Equal(screenName, Name(rom.Data, tables, screen));
                    }

                    if (sheetName is not null)
                    {
                        Assert.Equal(sheetName, Name(rom.Data, tables, sheet));
                    }

                    if (t < 3 && id != own.Id)
                    {
                        Assert.Equal(clean.ResultRows(id), tables.AsSpan(rows, 4).ToArray().Select(b => (int)b));
                    }
                }
            }
        }

        // The project's rows, bottom row first.
        Assert.Equal([(int)PointSource.Rings, (int)PointSource.Time, 0, 0],
            tables.AsSpan(ResultTables.RowsOffset(own.Id)!.Value, 4).ToArray().Select(b => (int)b));
        Assert.Equal("SD_L2_S1", Name(rom.Data, tables, Word(tables, ((((4 * 8) + 1) * 5) + 0) * 4)));
        Assert.Equal("HP_L3_S2", Name(rom.Data, tables, Word(tables, Sheet + (((((5 * 8) + 2) * 5) + 0) * 4))));
        Assert.Equal("HP_L1_S1", Name(rom.Data, tables, Word(tables, ((((5 * 8) + 0) * 5) + 0) * 4))); // already per level
        Assert.Equal("CB_L123_S2", Name(rom.Data, tables, Word(tables, Sheet + (((((3 * 8) + 1) * 5) + 2) * 4))));
    }

    [RealRomFact]
    public void NewProject_HasTheLevelTexts_AndTheBuildUsesTheTables()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        Assert.True(session.Project.HasPatch(GamePatches.BonusGames));

        // The nine texts, copies of the shared ones.
        GameTextLibrary texts = session.LoadTexts();
        foreach ((string name, string source) in ResultTables.NewTexts)
        {
            GameText text = texts.Texts.Single(t => t.Name == name);
            Assert.True(text.Index >= session.OriginalTextCount);
            Assert.Equal(texts.Texts.Single(t => t.Name == source).Markup, text.Markup);
            Assert.True(text.IsCustom && text.IsPatchText);
            Assert.Equal(texts.Texts.Single(t => t.Name == source).Category.Area, text.Category.Area); // sorted with the shared text
            Assert.Equal("Score sheets", text.Category.Section);
        }

        Assert.Empty(session.Project.GetOverrides()); // only in memory until a text is saved

        // The missions show the texts of their level.
        IReadOnlyList<MissionInfo> missions = session.LoadMissions().Missions;
        MissionInfo sky2 = missions.Single(m => m.Id == new TestId(1, Vehicle.SkyDiving, 0));
        Assert.Equal("SD_L2_S1", sky2.MissionScreenTextName);
        Assert.Equal("SD_L2_S2", sky2.ResultSheetTextName);
        MissionInfo hopper3 = missions.Single(m => m.Id == new TestId(2, Vehicle.JumbleHopper, 0));
        Assert.Equal("HP_L3_S1", hopper3.MissionScreenTextName);
        Assert.Equal("HP_L3_S2", hopper3.ResultSheetTextName);
        Assert.Empty(sky2.SharesResultSheetTextWith);
        Assert.Equal(new MedalLimits(280, 320, 360), session.LoadMedalLimits()[7]);

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        DataImages image = DataImage(built);
        byte[]? tables = DataArea.ReadResultTables(image);
        Assert.NotNull(tables);
        Assert.Equal(ResultTables.Create(rom.Data, RomLayout.PilotwingsUsa.Missions!, session.Project.Settings.Missions), tables);
        Assert.Empty(DataArea.ReadImage(image.Main)); // no mission settings, only the tables

        Assert.Equal(0x3C100000u | ((ResultTables.ResultSheetNamesAddress + 0x8000) >> 16), Word(built, 0xB563C)); // lui s0, hi(names)
        Assert.Equal(0x8E100000u | (ResultTables.ResultSheetNamesAddress & 0xFFFF), Word(built, 0xB5648)); // lw s0, lo(...)(s0)
        Assert.Equal(0x8E100000u | (ResultTables.MissionScreenNamesAddress & 0xFFFF), Word(built, 0xB567C)); // mission screen names
        Assert.Equal(0x90C60000u | (ResultTables.RowsAddress & 0xFFFF), Word(built, 0xB57D4));             // point rows
        Assert.Equal(0x0018C140u, Word(built, 0xB562C));                 // the vehicle's stride: 8 classes
        Assert.Equal(0x0307C021u, Word(built, 0xB5618));                 // × 5
        Assert.Equal(0x2C210003u, Word(built, 0x6FA00));                 // save: (veh - 3) < 3
        Assert.Equal(0x26E1FFFDu, Word(built, 0x6FE14));                 // load: the delay slot of the vehicle loop
        Assert.Equal(new MedalLimits(280, 320, 360), new MissionTables(built, RomLayout.PilotwingsUsa.Missions!).MedalLimits()[7]);
        foreach (int call in GamePatches.PointsToMedalCalls)
        {
            Assert.Equal(CodeArea.Jal(call == GamePatches.CeremonyMedalCall ? GameModuleImage.CeremonyMedalAddress : GameModuleImage.PointsToMedalAddress),
                Word(built, call));
        }

        // The texts are in the built text file.
        var fileSystem = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        GameTextFile textFile = GameTextFile.Parse(GameTextFile.FindFile(fileSystem).Data);
        Assert.All(ResultTables.NewTexts, t => Assert.Contains(textFile.Entries, e => e.Name == t.Name));
    }

    [RealRomFact]
    public void Undo_RestoresTheCode_AndKeepsTheTexts()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        IReadOnlyList<string> uses = GamePatches.MissionsUsing(GamePatches.BonusGames, session.LoadMissions().Missions);
        Assert.Equal(6, uses.Count); // three Sky Diving and three Jumble Hopper levels

        session.SaveTexts(session.LoadTexts(), new Dictionary<int, string> { [440] = "Changed" }); // now the texts are in the project
        Assert.Equal("SD_L2_S1", session.LoadTexts().Texts[440].Name);
        session.SetGamePatch(GamePatches.BonusGames, false);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x3C108035u, Word(built, 0xB563C));
        Assert.Equal(0x2AC10003u, Word(built, 0x6F9FC));
        Assert.Equal(0x0C0CB05Du, Word(built, GamePatches.PointsToMedalCalls[0]));
        Assert.Null(DataArea.ReadResultTables(DataImage(built)));
        Assert.Equal("SD_L123_S1", session.LoadMissions().Missions.Single(m => m.Id == new TestId(1, Vehicle.SkyDiving, 0)).MissionScreenTextName);
        Assert.Contains(session.LoadTexts().Texts, t => t.Name == "SD_L1_S1"); // stays as a custom text

        // Applying it again: the texts are not added twice.
        int count = session.LoadTexts().Texts.Count;
        session.SetGamePatch(GamePatches.BonusGames, true);
        Assert.Equal(count, session.LoadTexts().Texts.Count);
    }

    [RealRomFact]
    public void ProjectWithoutTheTexts_GetsThemInTheBuild()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("plain"), "Plain", rom.ComputeSha1(), null); // no session: no texts
        Assert.Empty(project.GetOverrides());
        byte[] built = ProjectBuilder.Build(project, rom, RomLayout.PilotwingsUsa).RomBuild.Rom.Data;
        var fileSystem = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa);
        GameTextFile textFile = GameTextFile.Parse(GameTextFile.FindFile(fileSystem).Data);
        GameTextFile original = GameTextFile.Parse(GameTextFile.FindFile(GameFileSystem.Read(rom, RomLayout.PilotwingsUsa)).Data);
        Assert.Equal(original.Entries.Count + 9, textFile.Entries.Count);
        Assert.Equal(original.Entries.Single(e => e.Name == "SD_L123_S2").Data, textFile.Entries.Single(e => e.Name == "SD_L3_S2").Data);
    }

    [RealRomFact]
    public void Patch_NeedsTheExpandedTexts()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Bonus", false, rom, TestRomLocator.RomPath!);
        session.SetGamePatch(GamePatches.BonusGames, false);
        session.Project.Settings.AppliedCodeFixes.Remove(CodeFixes.ExpandedTexts.Id);
        Assert.Throws<ProjectException>(() => session.SetGamePatch(GamePatches.BonusGames, true));
        Assert.False(session.Project.HasPatch(GamePatches.BonusGames));
    }

    [RealRomFact]
    public void BirdmanWithPoints_GetsItsTextsRowsMaskAndLandingValues()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Birdman", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(1, Vehicle.Birdman, 2);
        MissionInfo bm = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(bm, null);
        Assert.False(draft.HasScoreSheet);
        Assert.False(draft.CanChangeSources);
        Assert.Empty(draft.LandingValuesShown);

        draft.SetBirdmanPoints(true);
        Assert.True(draft.HasScoreSheet && draft.Settings.GenerateTexts);
        Assert.Equal([PointSource.LandingAccuracy], draft.Rows); // the original missions have a landing curve of 60 points
        Assert.Null(draft.SetRowSource(1, PointSource.LandingImpact, 20));
        Assert.Equal(LandingValues.SpeedsOf(Vehicle.Birdman), draft.LandingValuesShown);
        draft.SetLandingValue(LandingValue.BirdmanLandingForward, 40);
        Assert.Equal([new CurvePoint(0, 20), new CurvePoint(21, 20), new CurvePoint(30, 10), new CurvePoint(40, 0)],
            draft.Comm.GetCurve(ScoreCurveKind.Impact).Points); // the Birdman's own impact curve (its speed in km/h)
        session.SaveMission(bm, draft.Comm, draft.SettingsToSave());

        // Names, texts (in memory, copies of the free flight's) and the shown medal limits.
        bm = session.LoadMissions().Missions.Single(m => m.Id == id);
        Assert.Equal("BD_A3_S1", bm.MissionScreenTextName);
        Assert.Equal("BD_A3_S2", bm.ResultSheetTextName);
        Assert.Equal("BD_ALL_S1", session.LoadMissions().Missions.Single(m => m.Id == new TestId(1, Vehicle.Birdman, 0)).MissionScreenTextName);
        GameTextLibrary texts = session.LoadTexts();
        GameText sheet = texts.Texts.Single(t => t.Name == "BD_A3_S2");
        Assert.True(sheet.IsPatchText);
        Assert.Equal("Birdman", sheet.Category.Area);
        Assert.Equal(texts.Texts.Single(t => t.Name == "BD_ALL_S2").Markup, sheet.Markup);
        Assert.Contains(GamePatches.MissionsUsing(GamePatches.BonusGames, session.LoadMissions().Missions), u => u.Contains("mission with points", StringComparison.Ordinal));

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        DataImages image = DataImage(built);
        Assert.Equal([new TestId(1, Vehicle.Birdman, 2)], DataArea.ReadBirdmanPoints(image));
        byte[] tables = DataArea.ReadResultTables(image)!;
        int entry = (((6 * 8) + 1) * 5) + 2;
        Assert.Equal("BD_A3_S1", Name(built, tables, Word(tables, entry * 4)));
        Assert.Equal("BD_A3_S2", Name(built, tables, Word(tables, Sheet + (entry * 4))));
        Assert.Equal(ResultTables.BirdmanMissionScreenName, Word(tables, (((6 * 8) + 1) * 5) * 4)); // stage 2, mission 1: free flight
        Assert.Equal([(int)PointSource.LandingImpact, (int)PointSource.LandingAccuracy, 0, 0],
            tables.AsSpan(ResultTables.RowsOffset(id)!.Value, 4).ToArray().Select(b => (int)b));
        Assert.Equal(0, tables.AsSpan(ResultTables.RowsOffset(new TestId(1, Vehicle.Birdman, 0))!.Value, 4).ToArray().Sum(b => b));

        // The Birdman's entry with its four speeds (original ones where not set), and its patched landing code.
        GameModuleCondition condition = DataArea.ReadImage(image.Main).Single(c => c.Vehicle == 6);
        Assert.Equal([50f, 40f], condition.VehicleLimits);
        Assert.Equal(0x3C01801Fu, Word(built, 0x57A24));
        Assert.Equal(0xC4285050u, Word(built, 0x57A30));
        Assert.Equal(0xC4325054u, Word(built, 0x57A5C));
        Assert.Equal(CodeArea.Jal(GameModuleImage.FlightFrameHookAddress), Word(built, 0x74BBC)); // the Birdman's flight frame call
    }

    [RealRomFact]
    public void BirdmanWithPoints_IsImportedAgain()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Birdman", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(3, Vehicle.Birdman, 3);
        MissionInfo bm = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(bm, null);
        draft.SetBirdmanPoints(true);
        Assert.Null(draft.SetRowSource(1, PointSource.Time, 40));
        session.SaveMission(bm, draft.Comm, draft.SettingsToSave());
        N64Rom hack = session.Build(false).Result.RomBuild.Rom;

        RomImport import = ProjectImporter.Analyze(hack, rom, RomLayout.PilotwingsUsa, "file");
        MissionSettings imported = import.Settings.Missions.Single(m => m.Id == id);
        Assert.True(imported.BirdmanPoints);
        Assert.Equal([(int)PointSource.LandingAccuracy, (int)PointSource.Time], imported.ResultRows);
        Assert.DoesNotContain(import.ChangedFiles, f => f.FileType == GameTextFile.FormType); // the texts the build adds by itself

        (EditorSession copy, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, hack, temp.Combine("imported"), "Birdman", false, rom, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
    }

    [Fact]
    public void BirdmanMask_AndNames()
    {
        TestId[] missions = [new(0, Vehicle.Birdman, 0), new(3, Vehicle.Birdman, 4), new(2, Vehicle.Birdman, 1)];
        byte[] ext = DataExtension.Create(DataExtension.GameMissionFiles, missions, DataExtension.GamePhotoMissions);
        Assert.Equal([1, 0, 2, 16, 0, 0, 0, 0], ext[DataExtension.BirdmanOffset..(DataExtension.BirdmanOffset + 8)]);
        Assert.Equal<IEnumerable<TestId>>(missions.OrderBy(m => (m.Class, m.Test)), DataExtension.ReadBirdmanPoints(ext));
        Assert.Equal("BD_P4_S2", ResultTables.BirdmanTextName(new TestId(3, Vehicle.Birdman, 3), true));
        Assert.Null(ResultTables.BirdmanTextName(new TestId(0, Vehicle.HangGlider, 0), true));
        Assert.True(ResultTables.IsLevelText("BD_E1_S1"));
        Assert.False(ResultTables.IsLevelText("BD_ALL_S1"));
        IReadOnlyList<TestId> all = [.. Enumerable.Range(0, 20).Select(b => new TestId(b / 5, Vehicle.Birdman, b % 5))];
        Assert.True(ResultTables.Address + ResultTables.SizeFor(all) <= PW64Editor.Core.Classes.ClassLimits.TableAddress);
        Assert.Equal(9 + 40, ResultTables.TextsNeeded(all).Count);
    }

    [Fact]
    public void GeneratedTexts_HaveTheLimitsAndLinesOfTheMissionsWithPoints()
    {
        IReadOnlyList<(string Name, string Markup)> texts =
            ResultTables.GeneratedTexts([new(0, Vehicle.Birdman, 0), new(0, Vehicle.Birdman, 3), new(0, Vehicle.HangGlider, 1)]);
        Assert.Equal(["BD_E_S3_BLONDS", "BD_E_S3_SILVER", "BD_E_S3_GOLD", "BD_E_S3_PERFECT", "BD_E_S3_GREAT"], texts.Select(t => t.Name));
        string[] bronze = texts[0].Markup.Split('\n');
        Assert.Equal("[x=38][b]< 140 pts[/b]. [b]for Bronze Badge >[/b]", bronze[0]);
        Assert.Equal(["", "", "  Test 1", "  Test 4", "  [b]Total[/b]", "", "Points to go:"], bronze[1..]);
        Assert.Contains("< 180 pts", texts[2].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Points to go", texts[4].Markup, StringComparison.Ordinal);

        // One mission: the limits of one mission (two digits), no total; four missions: "4 tests to go".
        Assert.StartsWith("[x=43][b]< 70 pts", ResultTables.GeneratedTexts([new(2, Vehicle.Birdman, 1)])[0].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Total", ResultTables.GeneratedTexts([new(2, Vehicle.Birdman, 1)])[0].Markup, StringComparison.Ordinal);
        IReadOnlyList<(string Name, string Markup)> four = ResultTables.GeneratedTexts([.. Enumerable.Range(0, 4).Select(t => new TestId(3, Vehicle.Birdman, t))]);
        Assert.Contains(four, t => t.Name == "BD_P_S3_PERFECT" && t.Markup.Contains("< 400 pts", StringComparison.Ordinal));
        Assert.Equal("[b]4 tests to go[/b]", four.Single(t => t.Name == "TRY4").Markup);
        Assert.True(ResultTables.IsGeneratedText("BD_B_S3_GOLD") && ResultTables.IsGeneratedText("TRY4"));
        Assert.False(ResultTables.IsGeneratedText("TRY3") || ResultTables.IsGeneratedText("A_S3_GOLD"));
    }

    [RealRomFact]
    public void BirdmanStage1Test1WithPoints_GetsTheHangGlidersPad_AndTheTotalScreen()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Birdman", false, rom, TestRomLocator.RomPath!);
        MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.HangGlider, 0));
        foreach (int test in new[] { 0, 3 })
        {
            MissionInfo bm = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.Birdman, test));
            Assert.Equal(0, bm.File.Comm.GetObjectCount(MissionObjectKind.LandingPad));
            var draft = new MissionDraft(bm, null);
            draft.SetBirdmanPoints(true);
            session.SaveMission(bm, draft.Comm, draft.SettingsToSave());
        }

        // Only test 1 gets a pad: the Hang Glider's of Holiday Island, with its target type.
        MissionInfo e1 = session.LoadMissions().Missions.Single(m => m.Id == EditorSession.BirdmanPadMission);
        Assert.Equal(1, e1.File.Comm.GetObjectCount(MissionObjectKind.LandingPad));
        Assert.Equal(hg.File.GetObjectData(MissionObjectKind.LandingPad)[..MissionFile.LandingPadSize].ToArray(),
            e1.File.GetObjectData(MissionObjectKind.LandingPad).ToArray());
        Assert.Equal(0, session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.Birdman, 3)).File.Comm.GetObjectCount(MissionObjectKind.LandingPad));

        // Saving again adds no second pad.
        var again = new MissionDraft(e1, null);
        session.SaveMission(e1, again.Comm, again.SettingsToSave());
        Assert.Equal(1, session.LoadMissions().Missions.Single(m => m.Id == e1.Id).File.Comm.GetObjectCount(MissionObjectKind.LandingPad));

        // The generated texts of the stage, and the code that leads "Next" to the total screen.
        GameTextLibrary texts = session.LoadTexts();
        Assert.Contains("< 140 pts", texts.Texts.Single(t => t.Name == "BD_E_S3_BLONDS").Markup, StringComparison.Ordinal);
        Assert.DoesNotContain(texts.Texts, t => t.Name == "BD_A_S3_BLONDS");
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(CodeArea.J(GameModuleImage.ResultNextAddress), Word(built, 0xB5D24));
        Assert.Equal(CodeArea.Jal(GameModuleImage.TotalMenuAddress), Word(built, 0xCEA50));
        Assert.Equal(CodeArea.Jal(GameModuleImage.HudBirdmanAddress), Word(built, 0x9FBC4));
        Assert.Equal(CodeArea.J(GameModuleImage.LevelPointsAddress), Word(built, 0xB3250)); // levelGetTotalPoints
    }

    [RealRomFact]
    public void BirdmanPad_IsAddedOnBuild_WhenThePointsCameWithoutIt()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Birdman", false, rom, TestRomLocator.RomPath!);
        MissionSettings settings = MissionSettings.For(EditorSession.BirdmanPadMission);
        settings.BirdmanPoints = true; // as saved by the first version of Step 36 (no pad yet)
        session.Project.Settings.Missions.Add(settings);
        session.Project.Save();
        Assert.Equal(0, session.LoadMissions().Missions.Single(m => m.Id == EditorSession.BirdmanPadMission).File.Comm.GetObjectCount(MissionObjectKind.LandingPad));

        session.Build(false);
        Assert.Equal(1, session.LoadMissions().Missions.Single(m => m.Id == EditorSession.BirdmanPadMission).File.Comm.GetObjectCount(MissionObjectKind.LandingPad));
        Assert.True(session.Project.Settings.Missions.Single(m => m.Id == EditorSession.BirdmanPadMission).BirdmanPoints);
    }

    [Fact]
    public void BirdmanLanding_HasNoGoodLandingSpeeds_AndTheOldOnesAreDropped()
    {
        Assert.Equal([LandingValue.BirdmanLandingVertical, LandingValue.BirdmanLandingForward], LandingValues.SpeedsOf(Vehicle.Birdman));
        Assert.DoesNotContain(LandingValue.BirdmanGoodForward, LandingValues.All);
        Assert.Equal(1f, LandingValues.ImpactShownPerStored(Vehicle.Birdman));
        Assert.Equal(LandingValues.KilometersPerHourPerUnit, LandingValues.ImpactShownPerStored(Vehicle.RocketBelt));
    }

    [Fact]
    public void SaveLayout_KeepsTheBirdmansStage4_WithThePatch()
    {
        SaveGameLayout retail = SaveGameLayout.Retail;
        SaveGameLayout patched = SaveGameLayout.RetailWithBirdmanStage4;
        Assert.Equal(57, retail.SavedTests.Count);
        Assert.Equal(61, patched.SavedTests.Count);
        Assert.False(retail.IsSaved(new TestId(3, Vehicle.Birdman, 0)));
        Assert.True(patched.IsSaved(new TestId(3, Vehicle.Birdman, 3)));
        Assert.Equal(retail.IndexOf(new TestId(2, Vehicle.Birdman, 3)), patched.IndexOf(new TestId(2, Vehicle.Birdman, 3)));
        Assert.Equal(retail.PhotosStartBit + (4 * SaveGameLayout.ResultBits), patched.PhotosStartBit);
        Assert.True(patched.BirdmanStage4);
        Assert.False(retail.BirdmanStage4);
    }
}
