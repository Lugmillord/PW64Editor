using System.Buffers.Binary;
using PW64Editor.Core.Audio;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Project;

public class ProjectImporterTests
{
    private static readonly TestId HangGliderA = new(0, Vehicle.HangGlider, 0);
    private static readonly TestId RocketBeltA = new(0, Vehicle.RocketBelt, 0);
    private static readonly TestId GyrocopterA = new(0, Vehicle.Gyrocopter, 0);

    /// <summary>A project with something of every kind the import can recover, and its built ROM.</summary>
    private static (EditorSession Session, N64Rom Hack) CreateRichHack(TempDirectory temp, N64Rom clean)
    {
        EditorSession session = EditorSession.Create(temp.Combine("source"), "Rich Hack", false, clean, TestRomLocator.RomPath!);
        HackProject project = session.Project;
        project.Settings.Version = "2.3";
        project.Settings.Author = "Tester";
        project.Settings.Features.DebugOutput = true;
        project.Settings.Features.LiveValues = true;
        project.Settings.Features.LiveAltitude = false;
        project.Save();

        IReadOnlyList<MissionInfo> missions = session.LoadMissions().Missions;

        // Hang Glider A: combined conditions (module), hard end time with 0 points and no jingle, rows swapped.
        MissionInfo hg = missions.Single(m => m.Id == HangGliderA);
        var draft = new MissionDraft(hg, null);
        draft.SetConditions(new MissionConditions { CrashGivesZero = true, MinimumDistance = 150 });
        draft.SetHardEndTime(40);
        draft.SetHardEndZero(true);
        draft.SetHardEndJingle(HardEndJingle.None);
        Assert.Null(draft.MoveRow(0, 1));
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());

        // Rocket Belt A: unlimited fuel and a hard end time that is scored (default jingle).
        MissionInfo rb = missions.Single(m => m.Id == RocketBeltA);
        draft = new MissionDraft(rb, null);
        draft.SetUnlimitedFuel(true);
        draft.SetHardEndTime(30);
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());

        // Gyrocopter A: no "View Demo" button.
        MissionInfo gc = missions.Single(m => m.Id == GyrocopterA);
        draft = new MissionDraft(gc, null);
        draft.SetHideDemo(true);
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());

        // A song replaced by another one.
        SequenceBank bank = SequenceBank.Parse(clean.Data.AsSpan(RomLayout.PilotwingsUsa.Audio!.SequenceOffset, RomLayout.PilotwingsUsa.Audio.SequenceSize));
        project.WriteImportedSong(3, bank.Sequences[4]);

        N64Rom hack = session.Build(false).Result.RomBuild.Rom;
        return (session, hack);
    }

    [RealRomFact]
    public void HackRom_IsImportedWithItsSettings_AndRebuildsIdentically()
    {
        using var temp = new TempDirectory();
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        (EditorSession source, N64Rom hack) = CreateRichHack(temp, clean);

        RomImport import = ProjectImporter.Analyze(hack, clean, RomLayout.PilotwingsUsa, "file name");
        Assert.True(import.MadeWithEditor);
        ProjectSettings s = import.Settings;
        Assert.Equal("Rich Hack", s.Name);
        Assert.Equal("2.3", s.Version);
        Assert.Equal(source.Project.Settings.AppliedCodeFixes.Order(), s.AppliedCodeFixes.Order());
        Assert.Equal(source.Project.Settings.AppliedPatches, s.AppliedPatches);
        Assert.True(s.Features.DebugOutput);
        Assert.True(s.Features.LiveValues);
        Assert.False(s.Features.LiveAltitude);
        Assert.True(s.Features.LiveDistance);

        MissionSettings hg = s.Missions.Single(m => m.Id == HangGliderA);
        Assert.True(hg.Conditions!.CrashGivesZero);
        Assert.Equal(150f, hg.Conditions.MinimumDistance);
        Assert.True(hg.HardEndZero);
        Assert.Equal(HardEndJingle.None, hg.HardEndJingle);
        Assert.Equal(source.Project.Settings.Missions.Single(m => m.Id == HangGliderA).ResultRows, hg.ResultRows);

        MissionSettings rb = s.Missions.Single(m => m.Id == RocketBeltA);
        Assert.True(rb.UnlimitedFuel);
        Assert.False(rb.HardEndZero);
        Assert.Null(rb.HardEndJingle);   // the default
        Assert.Null(rb.Conditions);

        Assert.True(s.Missions.Single(m => m.Id == GyrocopterA).HideDemo);
        Assert.Equal([3], import.Songs.Keys);
        Assert.Equal(source.Project.GetOverrides().Select(o => o.TableIndex), import.ChangedFiles.Select(f => f.TableIndex));

        (EditorSession copy, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, hack, temp.Combine("imported"), "Rich Hack", true, clean, TestRomLocator.RomPath!);
        Assert.DoesNotContain(report, n => n.Kind == ImportNoteKind.Warning);
        Assert.Contains(report, n => n.Text.StartsWith("The project builds exactly the imported ROM", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(copy.Project.Folder, ".gitignore")));
        Assert.True(copy.Project.HasImportedSong(3));
        Assert.DoesNotContain(copy.Project.Settings.Missions, m => m.GenerateTexts);
    }

    [RealRomFact]
    public void Patch_IsAppliedToTheCleanRom_AndGivesTheAuthor()
    {
        using var temp = new TempDirectory();
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        (EditorSession source, N64Rom hack) = CreateRichHack(temp, clean);
        byte[] patch = ProjectBuilder.CreatePatch(source.Project, clean, hack);

        (N64Rom patched, string metadata) = ProjectImporter.ApplyPatch(clean, patch);
        Assert.Equal(hack.Data, patched.Data);
        RomImport import = ProjectImporter.Analyze(patched, clean, RomLayout.PilotwingsUsa, "patch", metadata);
        Assert.Equal("Tester", import.Settings.Author);

        // A patch for another ROM is refused.
        byte[] other = (byte[])clean.Data.Clone();
        other[0x2000] ^= 0xFF;
        byte[] foreignPatch = PW64Editor.Core.Patching.BpsWriter.Create(other, hack.Data, string.Empty);
        Assert.Throws<ProjectException>(() => ProjectImporter.ApplyPatch(clean, foreignPatch));
    }

    [RealRomFact]
    public void ForeignRom_GivesFilesAndSongs_AndReportsOtherCodeChanges()
    {
        using var temp = new TempDirectory();
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        RomLayout layout = RomLayout.PilotwingsUsa;
        byte[] data = (byte[])clean.Data.Clone();

        // A changed byte inside a mission file and one in the game code that no fix explains.
        GameFile mission = GameFileSystem.Read(clean, layout).Files.First(f => f.FileType == MissionFile.FormType);
        data[mission.RomOffset + mission.Size - 1] ^= 0x01;
        data[0x50000] ^= 0x01;
        N64Rom foreign = N64Rom.FromBytes(data);
        foreign.UpdateBootChecksum();

        RomImport import = ProjectImporter.Analyze(foreign, clean, layout, "Foreign");
        Assert.False(import.MadeWithEditor);
        Assert.Equal("Foreign", import.Settings.Name);
        Assert.Empty(import.Settings.AppliedCodeFixes);
        Assert.Equal([mission.TableIndex], import.ChangedFiles.Select(f => f.TableIndex));

        (EditorSession session, IReadOnlyList<ImportNote> report) = EditorSession.CreateFromImport(
            import, foreign, temp.Combine("foreign"), "Foreign", false, clean, TestRomLocator.RomPath!);
        Assert.Contains(report, n => n.Kind == ImportNoteKind.Warning && n.Text.Contains("Game code", StringComparison.Ordinal));
        Assert.Contains(report, n => n.Kind == ImportNoteKind.Info && n.Text.Contains("16 MiB", StringComparison.Ordinal));
        Assert.Single(session.Project.GetOverrides());
    }

    [RealRomFact]
    public void OtherGame_IsRefused()
    {
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        byte[] data = (byte[])clean.Data.Clone();
        data[0x3C] = (byte)'X'; // cartridge id
        Assert.Throws<ProjectException>(() => ProjectImporter.Analyze(N64Rom.FromBytes(data), clean, RomLayout.PilotwingsUsa, "x"));
    }

    [RealRomFact]
    public void Copy_TakesSettingsFilesAndSongs_ButNoBackupsOrRoms()
    {
        using var temp = new TempDirectory();
        N64Rom clean = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("orig"), "Original", true, clean, TestRomLocator.RomPath!);
        HackProject project = session.Project;
        project.Settings.Author = "Me";
        project.SetOutputRomPath(temp.Combine("elsewhere", "out.z64"));
        project.Save();
        project.WriteImportedSong(2, [1, 2, 3, 4]);
        File.WriteAllText(Path.Combine(project.FilesFolder, "notes.txt"), "keep me");
        session.Build(createRestorePoint: true);
        File.WriteAllBytes(Path.Combine(project.Folder, "Original.z64"), [0]);

        Assert.True(project.HasGitIgnore);
        EditorSession copy = session.CopyTo(temp.Combine("copy"), "Copy", true);
        HackProject c = copy.Project;
        Assert.Equal("Copy", c.Settings.Name);
        Assert.Equal("Me", c.Settings.Author);
        Assert.Equal("Original", HackProject.Load(project.Folder).Settings.Name);
        Assert.Null(c.Local.OutputRomPath);
        Assert.Equal(project.Local.CleanRomPath, c.Local.CleanRomPath);
        Assert.Equal([1, 2, 3, 4], c.ReadImportedSong(2));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(c.FilesFolder, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(c.Folder, ".gitignore")));
        Assert.False(Directory.Exists(Path.Combine(c.Folder, "backups")));
        Assert.Empty(Directory.EnumerateFiles(c.Folder, "*.z64"));

        // Changing the copy leaves the original alone.
        c.Settings.Missions.Add(MissionSettings.For(HangGliderA));
        Assert.Empty(project.Settings.Missions);

        Assert.False(session.CopyTo(temp.Combine("copy2"), "Copy 2", false).Project.HasGitIgnore);
        Assert.Throws<ProjectException>(() => session.CopyTo(Path.Combine(project.Folder, "inner"), "Inner", false));
        Assert.Throws<ProjectException>(() => session.CopyTo(temp.Combine("copy"), "Again", false));
    }
}
