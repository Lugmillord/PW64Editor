using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Workspace;

public class EditorStorageTests
{
    [Fact]
    public void LoadSettings_ReturnsDefaults_WhenNothingSaved()
    {
        using var temp = new TempDirectory();

        EditorSettings settings = new EditorStorage(temp.Path).LoadSettings();

        Assert.Empty(settings.RecentProjects);
    }

    [Fact]
    public void LoadSettings_ReturnsDefaults_ForBrokenFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("settings.json"), "{ broken");

        EditorSettings settings = new EditorStorage(temp.Path).LoadSettings();

        Assert.Empty(settings.RecentProjects);
    }

    [Fact]
    public void SaveSettings_ThenLoad_RoundTrips()
    {
        using var temp = new TempDirectory();
        var storage = new EditorStorage(temp.Combine("data"));

        storage.SaveSettings(new EditorSettings { RecentProjects = [temp.Combine("a"), temp.Combine("b")] });

        Assert.Equal([temp.Combine("a"), temp.Combine("b")], storage.LoadSettings().RecentProjects.ToArray());
    }
}

public class RecentProjectsTests
{
    [Fact]
    public void Add_PutsProjectOnTop_WithoutDuplicates()
    {
        using var temp = new TempDirectory();
        var settings = new EditorSettings();

        RecentProjects.Add(settings, temp.Combine("a"));
        RecentProjects.Add(settings, temp.Combine("b"));
        RecentProjects.Add(settings, temp.Combine("a"));

        Assert.Equal([temp.Combine("a"), temp.Combine("b")], settings.RecentProjects.ToArray());
    }

    [Fact]
    public void Add_KeepsAtMostMaxEntries()
    {
        using var temp = new TempDirectory();
        var settings = new EditorSettings();

        for (int i = 0; i < RecentProjects.MaxEntries + 5; i++)
        {
            RecentProjects.Add(settings, temp.Combine($"p{i}"));
        }

        Assert.Equal(RecentProjects.MaxEntries, settings.RecentProjects.Count);
        Assert.Equal(temp.Combine($"p{RecentProjects.MaxEntries + 4}"), settings.RecentProjects[0]);
    }

    [Fact]
    public void GetExisting_SkipsFoldersWithoutProject()
    {
        using var temp = new TempDirectory();
        HackProject.Create(temp.Combine("real"), "Real", "x", null);
        var settings = new EditorSettings();
        RecentProjects.Add(settings, temp.Combine("gone"));
        RecentProjects.Add(settings, temp.Combine("real"));

        Assert.Equal([temp.Combine("real")], RecentProjects.GetExisting(settings).ToArray());
        Assert.Equal(2, settings.RecentProjects.Count); // missing entries are kept, not deleted
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        using var temp = new TempDirectory();
        var settings = new EditorSettings();
        RecentProjects.Add(settings, temp.Combine("a"));

        RecentProjects.Remove(settings, temp.Combine("a"));

        Assert.Empty(settings.RecentProjects);
    }
}

public class CleanRomStoreTests
{
    [Fact]
    public void TryLoad_ReturnsNull_WhenNoCopyExists()
    {
        using var temp = new TempDirectory();
        var store = new CleanRomStore(new EditorStorage(temp.Path));

        Assert.Null(store.TryLoad(out string? problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void Import_RejectsNonRomFile()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(temp.Combine("notes.txt"), "not a rom");
        var store = new CleanRomStore(new EditorStorage(temp.Path));

        CleanRomImportResult result = store.Import(temp.Combine("notes.txt"));

        Assert.False(result.Success);
        Assert.False(File.Exists(store.RomPath));
    }

    [Fact]
    public void Import_RejectsOtherRom()
    {
        using var temp = new TempDirectory();
        File.WriteAllBytes(temp.Combine("other.z64"), SyntheticRom.Create(gameCode: "NSME"));
        var store = new CleanRomStore(new EditorStorage(temp.Path));

        CleanRomImportResult result = store.Import(temp.Combine("other.z64"));

        Assert.False(result.Success);
        Assert.Contains("not Pilotwings", result.Message);
    }

    [RealRomFact]
    public void Import_CopiesCleanRom_AndTryLoadVerifiesIt()
    {
        using var temp = new TempDirectory();
        var store = new CleanRomStore(new EditorStorage(temp.Path));

        CleanRomImportResult result = store.Import(TestRomLocator.RomPath!);
        N64Rom? loaded = store.TryLoad(out string? problem);

        Assert.True(result.Success, result.Message);
        Assert.NotNull(loaded);
        Assert.Null(problem);
    }

    [RealRomFact]
    public void Import_ConvertsByteSwappedRomToZ64()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        rom.Save(temp.Combine("clean.v64"), RomByteOrder.ByteSwapped);
        var store = new CleanRomStore(new EditorStorage(temp.Combine("data")));

        CleanRomImportResult result = store.Import(temp.Combine("clean.v64"));

        Assert.True(result.Success, result.Message);
        Assert.Equal(RomByteOrder.BigEndian, N64Rom.Load(store.RomPath).OriginalByteOrder);
    }

    [RealRomFact]
    public void TryLoad_DetectsDamagedCopy()
    {
        using var temp = new TempDirectory();
        var store = new CleanRomStore(new EditorStorage(temp.Path));
        store.Import(TestRomLocator.RomPath!);
        byte[] data = File.ReadAllBytes(store.RomPath);
        data[0x500000] ^= 0xFF;
        File.WriteAllBytes(store.RomPath, data);

        Assert.Null(store.TryLoad(out string? problem));
        Assert.Contains("damaged", problem);
    }
}

public class EditorSessionTests
{
    [RealRomFact]
    public void CreateBuildRestore_FullWorkflow()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Session Test", false, rom, TestRomLocator.RomPath!);

        string path = session.AddFileToProject(1063);
        Assert.Equal(OverrideState.Unchanged, Assert.Single(session.GetStatus()).State);

        var (_, romPath, restorePoint) = session.Build(createRestorePoint: true);
        Assert.True(File.Exists(romPath));
        Assert.NotNull(restorePoint);

        byte[] data = File.ReadAllBytes(path);
        data[0x36] = (byte)'X';
        File.WriteAllBytes(path, data);
        Assert.Equal(OverrideState.Modified, session.GetStatus()[0].State);

        session.RestoreTo(restorePoint.Name);
        Assert.Equal(OverrideState.Unchanged, session.GetStatus()[0].State);

        int patchSize = session.ExportPatch(temp.Combine("hack.bps"));
        Assert.True(patchSize > 0);
        Assert.True(File.Exists(temp.Combine("hack.bps")));
    }

    [RealRomFact]
    public void RestorePoint_KeepsEveryEditorSetting()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Restore Test", false, rom, TestRomLocator.RomPath!);

        // Settings of every tab that stores some: features, missions (conditions, demo button,
        // rows, footnote), texts, project settings.
        session.SaveFeatures(new GameFeatureSettings
        {
            DebugOutput = true, StartupInfo = true, GameMessages = true, OnScreen = false,
            LiveValues = true, LiveDistance = true, LiveAltitude = false, LiveHorizontalSpeed = true, LiveVerticalSpeed = false,
        });
        Core.Missions.MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == new Core.SaveGame.TestId(0, Core.SaveGame.Vehicle.HangGlider, 0));
        var draft = new Core.Missions.MissionDraft(hg, null);
        draft.SetConditions(new Core.Missions.MissionConditions
        {
            MinimumAltitude = 80, MinimumDistance = 900, Required = { [Core.Missions.ConditionObject.Rings] = 2 },
        });
        draft.SetHideDemo(true);
        Assert.Null(draft.SetRowSource(draft.Rows.Count, Core.Missions.PointSource.Distance, 10));
        draft.SetFootnote(["A footnote"]);
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());
        Core.Text.GameTextLibrary library = session.LoadTexts();
        session.SaveTexts(library, new Dictionary<int, string> { [library.Texts[5].Index] = "Changed text" });
        session.Project.Settings.Version = "2.5";
        session.Project.Save();

        string Snapshot() => System.Text.Json.JsonSerializer.Serialize(HackProject.Load(temp.Combine("hack")).Settings)
            + string.Join("|", Directory.EnumerateFiles(session.Project.FilesFolder, "*", SearchOption.AllDirectories).Order()
                .Select(f => Path.GetFileName(f) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(f)))));
        string before = Snapshot();
        BackupInfo point = session.CreateRestorePoint();

        // Change all of it again.
        session.SaveFeatures(new GameFeatureSettings());
        hg = session.LoadMissions().Missions.Single(m => m.Id == hg.Id);
        session.SaveMission(hg, session.OriginalMissionComm(hg), null);
        library = session.LoadTexts();
        session.SaveTexts(library, new Dictionary<int, string> { [library.Texts[5].Index] = "Something else" });
        session.Project.Settings.Version = "3.0";
        session.Project.Save();
        Assert.NotEqual(before, Snapshot());

        session.RestoreTo(point.Name);
        Assert.Equal(before, Snapshot());
        Assert.True(session.Project.Settings.Features.LiveValues);
        Assert.True(session.Project.Settings.Missions.Single().HideDemo);
        Assert.Equal(900f, session.Project.Settings.Missions.Single().Conditions!.MinimumDistance);
    }

    [RealRomFact]
    public void Open_RejectsProjectForDifferentRom()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject.Create(temp.Combine("hack"), "Other", "0000000000000000000000000000000000000000", null);

        Assert.Throws<ProjectException>(() => EditorSession.Open(temp.Combine("hack"), rom, TestRomLocator.RomPath!));
    }

    [RealRomFact]
    public void Open_UpdatesCleanRomPathOfProject()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject.Create(temp.Combine("hack"), "Hack", rom.ComputeSha1(), temp.Combine("old_location.z64"));

        EditorSession session = EditorSession.Open(temp.Combine("hack"), rom, TestRomLocator.RomPath!);

        Assert.Equal(TestRomLocator.RomPath, session.Project.Local.CleanRomPath);
        Assert.Equal(TestRomLocator.RomPath, HackProject.Load(temp.Combine("hack")).Local.CleanRomPath);
    }

    [RealRomFact]
    public void AddFileToProject_Throws_ForUnknownIndex()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Hack", false, rom, TestRomLocator.RomPath!);

        Assert.Throws<ProjectException>(() => session.AddFileToProject(99999));
    }

    [RealRomFact]
    public void RomReport_DescribesCleanRom()
    {
        string report = RomReport.Describe(N64Rom.Load(TestRomLocator.RomPath!));

        Assert.Contains("NPWE", report);
        Assert.Contains("CleanSupported", report);
        Assert.Contains("Checksum      : OK", report);
    }
}
