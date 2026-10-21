using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Code;

public class GamePatchTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    [Fact]
    public void GamePatches_HaveUniqueStableIds()
    {
        Assert.Equal(["goal-objects-v1", "bonus-birdman-v1"], GamePatches.AllIds);
        Assert.Same(GamePatches.GoalObjects, GamePatches.Find("goal-objects-v1"));
        Assert.Same(GamePatches.BonusGames, GamePatches.Find("bonus-birdman-v1"));
        Assert.Null(GamePatches.Find("unknown"));
    }

    [RealRomFact]
    public void GoalObjects_FitsTheCleanRom()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.Equal(CodeFixState.NotApplied,
            GamePatches.GoalObjects.AsCodeFix.GetState(rom.Data, CodeFixes.CodeStart, CodeFixes.CodeEnd(PW64Editor.Core.FileSystem.RomLayout.PilotwingsUsa)));
    }

    [RealRomFact]
    public void NewProject_AppliesTheGoalPatch_AndUndoRestoresTheCode()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Goal", false, rom, TestRomLocator.RomPath!);
        Assert.True(session.Project.HasPatch(GamePatches.GoalObjects));

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        int module = built.Length - GameModule.RomReserve;
        Assert.Equal(0u, Word(built, 0xCCA78));                                         // ball target: no "veh = Rocket Belt"
        Assert.Equal(0u, Word(built, 0xCCAE0));                                         // goal ring: the same
        Assert.Equal(CodeArea.Jal(GameModuleImage.GoalJingleAddress), Word(built, 0xCCAF4)); // jingle per vehicle
        Assert.Equal(0u, Word(built, 0x82818));                                         // no hopd++
        Assert.Equal(64u, Word(built, module + 0x0C) & 64u);                            // module: goal objects count
        Assert.Equal(CodeArea.Jal(GameModuleImage.ConditionsHookAddress), Word(built, GameModule.ConditionModeCallRomOffset));

        // Undo: the original code comes back, the scoring hook stays (goal time, hard end).
        session.SetGamePatch(GamePatches.GoalObjects, false);
        Assert.False(HackProject.Load(temp.Combine("hack")).HasPatch(GamePatches.GoalObjects));
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0xA56A000Eu, Word(built, 0xCCA78));
        Assert.Equal(0xA519000Eu, Word(built, 0xCCAE0));
        Assert.Equal(0x0C0CFDD2u, Word(built, 0xCCAF4));
        Assert.Equal(0xAFAB0028u, Word(built, 0x82818));
        Assert.Equal(0u, Word(built, module + 0x0C) & 64u);
        Assert.Equal(CodeArea.Jal(GameModuleImage.ConditionsHookAddress), Word(built, GameModule.ConditionModeCallRomOffset));

        session.SetGamePatch(GamePatches.GoalObjects, true);
        Assert.True(HackProject.Load(temp.Combine("hack")).HasPatch(GamePatches.GoalObjects));
    }

    [RealRomFact]
    public void OlderProjects_HaveNoPatches_AndUnknownPatchesAreRefused()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        HackProject project = HackProject.Create(temp.Combine("p"), "Old", rom.ComputeSha1(), null);
        string path = Path.Combine(project.Folder, "project.json");
        string json = File.ReadAllText(path);
        Assert.Contains("goal-objects-v1", json);

        Assert.Contains("bonus-birdman-v1", json);
        File.WriteAllText(path, json.Replace("\"goal-objects-v1\",", string.Empty).Replace("\"bonus-birdman-v1\"", string.Empty));
        Assert.Empty(HackProject.Load(project.Folder).Settings.AppliedPatches);

        File.WriteAllText(path, json.Replace("\"goal-objects-v1\"", "\"from-the-future-v9\""));
        Assert.Throws<ProjectException>(() => HackProject.Load(project.Folder));
    }

    [RealRomFact]
    public void RetailMissions_DoNotNeedTheGoalPatch()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Retail", false, rom, TestRomLocator.RomPath!);
        Assert.Empty(GamePatches.MissionsUsing(GamePatches.GoalObjects, session.LoadMissions().Missions));
    }

    [RealRomFact]
    public void HardEnd_IsSetPerMission_WithFootnoteAndModuleEntry()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "HardEnd", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(0, Vehicle.HangGlider, 0);
        MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        Assert.Equal(0f, hg.File.Comm.HardEndTime); // unused in retail

        var draft = new MissionDraft(hg, null);
        Assert.True(draft.CanSetHardEnd);
        draft.SetHardEndTime(150);
        Assert.False(draft.Settings.HardEndZero); // default: scored
        Assert.Contains("The flight ends after 2'30\".", string.Join(" ", draft.Footnote(_ => null)));
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        int data = built.Length - GameModule.TotalRomReserve;
        Assert.Equal(1u, Word(built, data + 8));
        int entry = data + DataArea.HeaderSize;
        Assert.Equal((byte)Vehicle.HangGlider, built[entry]);
        Assert.Equal(16, built[entry + 3]); // scored
        Assert.Equal(0x05, built[entry + 0x14]); // default jingle when scored: Hang Glider "landing OK"
        Assert.Equal(CodeArea.Jal(GameModuleImage.HardEndAddress), Word(built, GameModule.HardEndRomOffset));
        Assert.Equal(CodeArea.Jal(GameModuleImage.HangGliderMusicAddress), Word(built, 0x760D4));
        Assert.Equal(CodeArea.Jal(GameModuleImage.HangGliderMusicStateAddress), Word(built, 0x76130));
        Assert.Equal(CodeArea.Jal(GameModuleImage.HudTextAddress), Word(built, GameModule.HudStartTextCallRomOffset));
        int module = built.Length - GameModule.RomReserve;
        Assert.Equal([0x65, 0xFFFF], new[] { (int)(Word(built, module + 0xF0) >> 16), (int)(Word(built, module + 0xF0) & 0xFFFF) }); // bold "5"
        Assert.Equal(0x7Du, Word(built, module + 0xF0 + (5 * 24)) >> 16); // bold "T" of "Time up!"
        Assert.Contains("Hard end scoring: scored", MissionReport.Create(session));

        hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        Assert.Equal(150f, hg.File.Comm.HardEndTime);
        draft = new MissionDraft(hg, null);
        draft.SetHardEndZero(true);
        Assert.Contains("0 points", string.Join(" ", draft.Footnote(_ => null)));
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(32, built[entry + 3]); // 0 points
        Assert.Equal(0x06, built[entry + 0x14]); // default jingle with 0 points: "landing missed"

        hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        draft = new MissionDraft(hg, null);
        Assert.Equal(HardEndJingle.LandingMissed, draft.HardEndJingle);
        draft.SetHardEndJingle(HardEndJingle.None);
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0, built[entry + 0x14]);
        Assert.Contains("jingle None", MissionReport.Create(session));

        // Off again: no entry, and the game code of the hard end stays original.
        hg = session.LoadMissions().Missions.Single(m => m.Id == id);
        draft = new MissionDraft(hg, null);
        draft.SetHardEndTime(0);
        session.SaveMission(hg, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0u, Word(built, data + 8));
        Assert.Equal(0x24190001u, Word(built, GameModule.HardEndRomOffset));
        Assert.Equal(0x0C0CFDD2u, Word(built, 0x760D4));
        Assert.Equal(0x0C0C7700u, Word(built, GameModule.HudStartTextCallRomOffset));

        // Cannonball has none.
        MissionInfo cb = session.LoadMissions().Missions.First(m => m.Id.Vehicle == Vehicle.Cannonball);
        var cannon = new MissionDraft(cb, null);
        Assert.False(cannon.CanSetHardEnd);
        cannon.SetHardEndTime(30);
        Assert.Equal(0f, cannon.Comm.HardEndTime);
    }

    [Fact]
    public void HardEndJingles_PerVehicle()
    {
        Assert.Equal(0x0A, HardEndJingles.Music(Vehicle.JumbleHopper, HardEndJingle.LandingMissed)); // the Rocket Belt's
        Assert.Equal(0x18, HardEndJingles.Music(Vehicle.JumbleHopper, HardEndJingle.LandingOk));     // goal
        Assert.Equal(0x1B, HardEndJingles.Music(Vehicle.Birdman, HardEndJingle.LandingMissed));      // crash
        Assert.Equal(0x15, HardEndJingles.Music(Vehicle.SkyDiving, HardEndJingle.LandingMissed));
        Assert.Equal(0, HardEndJingles.Music(Vehicle.Gyrocopter, HardEndJingle.None));
        Assert.Equal(HardEndJingle.LandingOk, HardEndJingles.Effective(null));
        Assert.Equal(HardEndJingle.LandingMissed, HardEndJingles.Effective(new MissionSettings { HardEndZero = true }));
        Assert.Equal(HardEndJingle.None, HardEndJingles.Effective(new MissionSettings { HardEndZero = true, HardEndJingle = HardEndJingle.None }));
    }
}
