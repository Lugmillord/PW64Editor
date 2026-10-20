using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Project;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

/// <summary>Stage 4: time points after a crash, the Gyrocopter's touchdown curve and the values of a good landing.</summary>
public class LandingTests
{
    private static uint Word(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static MissionFile BuiltMission(byte[] rom, TestId id) =>
        GameFileSystem.Read(N64Rom.FromBytes(rom), RomLayout.PilotwingsUsa).Files
            .Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data)).Single(f => f.Comm.Id == id);

    /// <summary>The data area entry of a mission in a built ROM.</summary>
    private static GameModuleCondition Entry(byte[] rom, TestId id)
    {
        GameModuleHeader header = GameModule.ReadHeader(rom)!;
        return GameModule.ReadConditions(rom, header)
            .Single(c => c.Vehicle == (byte)id.Vehicle && c.Class == id.Class && c.Test == id.Test);
    }

    [Fact]
    public void Entry_KeepsTheNewValues()
    {
        var condition = new GameModuleCondition(1, 2, 0, false, null, new byte[GameModule.ObjectTypes])
        {
            CrashTimePoints = true,
            TouchdownCurve = [new CurvePoint(0, 4), new CurvePoint(10, 0)],
            PadRadius = 40,
            VehicleLimits = [15, 8],
        };
        byte[] image = DataArea.CreateImage([condition]);
        GameModuleCondition read = DataArea.ReadImage(image).Single();
        Assert.True(read.CrashTimePoints);
        Assert.True(read.HasConditions);
        Assert.Equal(condition.TouchdownCurve, read.TouchdownCurve);
        Assert.Equal(40f, read.PadRadius);
        Assert.Equal([15f, 8f], read.VehicleLimits);
        Assert.Equal(64 | 128, image[DataArea.HeaderSize + 3]);
        Assert.Equal(3, image[DataArea.HeaderSize + 0x15]);
    }

    [Fact]
    public void LandingValues_ShowTheGamesUnits()
    {
        // The HUD shows units per second x 2.52 as km/h: the Gyrocopter's 7.937 is 20 km/h, the Hang Glider compares km/h.
        Assert.Equal(20.0f, LandingValues.Shown(LandingValue.GyrocopterCrashOffPad, LandingValues.Original(LandingValue.GyrocopterCrashOffPad)), 1);
        Assert.Equal(32.0f, LandingValues.Shown(LandingValue.GyrocopterCrash, LandingValues.Original(LandingValue.GyrocopterCrash)), 1);
        Assert.Equal(15f, LandingValues.Shown(LandingValue.HangGliderGoodVertical, 15));
        Assert.Equal(21f, LandingValues.Shown(LandingValue.PadRadius, 30), 3);
        Assert.Equal(4, LandingValues.SpeedsOf(Vehicle.HangGlider).Count);
        Assert.Empty(LandingValues.SpeedsOf(Vehicle.SkyDiving));
    }

    [RealRomFact]
    public void CrashTimePoints_GoToTheModule_WithAHintAndAFootnote()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Crash", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(0, Vehicle.Gyrocopter, 0);
        MissionInfo gc = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(gc, null);
        Assert.Equal(CrashScoring.Original, draft.CrashScoring);
        draft.SetCrashScoring(CrashScoring.TimePoints);
        Assert.Equal(CrashScoring.TimePoints, draft.CrashScoring);
        Assert.Equal(30, draft.EarlyCrashTimePoints); // GC Beginner: full time points from 0 s
        string footnote = string.Join(" ", draft.Footnote(_ => null));
        Assert.True(footnote.Contains("Time points also count after a crash.", StringComparison.Ordinal)
            || footnote.Contains("Crash: time pts. count.", StringComparison.Ordinal), footnote);
        Assert.Equal(MissionCondition.None, draft.Comm.Condition);

        draft.SetCrashScoring(CrashScoring.Zero);
        Assert.False(draft.Conditions.CrashTimePoints);
        Assert.Equal(MissionCondition.CrashGivesZero, draft.Comm.Condition); // the game can do that alone
        draft.SetCrashScoring(CrashScoring.TimePoints);
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        GameModuleCondition entry = Entry(built, id);
        Assert.True(entry.CrashTimePoints);
        Assert.False(entry.CrashGivesZero);
        Assert.Equal(MissionCondition.None, BuiltMission(built, id).Comm.Condition);
        Assert.Equal(CodeArea.Jal(GameModuleImage.FlightFrameHookAddress), Word(built, (int)(0x802ED45Cu - 0x80278AD0u)));
        Assert.Contains("time points also after a crash", MissionReport.Create(session));
    }

    [RealRomFact]
    public void Gyrocopter_TouchdownCurve_TwoPercentCurves()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Touchdown", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(0, Vehicle.Gyrocopter, 0);
        MissionInfo gc = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(gc, null);
        int landing = draft.Rows.IndexOf(PointSource.LandingAccuracy);
        Assert.True(landing >= 0);
        int shown = draft.EvaluateRows(_ => null)[landing].Value!.Value;

        draft.SetSeparateTouchdown(true);
        Assert.NotNull(draft.TouchdownCurve);
        Assert.False(draft.IsChanged && draft.Settings.GenerateTexts); // a copy changes no points
        Assert.Equal(shown, draft.EvaluateRows(_ => null)[landing].Value);

        // A curve without a point at 100 % is refused; one with it keeps the row's points.
        Assert.NotNull(draft.SetLandingPercentCurve(true, [new CurvePoint(0, 80), new CurvePoint(10, 0)]));
        Assert.Null(draft.SetLandingPercentCurve(true, [new CurvePoint(0, 100), new CurvePoint(GameUnits.FromMeters(20), 50), new CurvePoint(GameUnits.FromMeters(20), 0)]));
        Assert.Equal(shown, draft.EvaluateRows(_ => null)[landing].Value);

        // The row's points scale both curves.
        draft.SetRowPoints(landing, 40, _ => null);
        Assert.Equal(40, draft.EvaluateRows(_ => null)[landing].Value);
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        GameModuleCondition entry = Entry(built, id);
        Assert.Equal(3, entry.TouchdownCurve!.Count);
        Assert.Equal(CodeArea.Jal(GameModuleImage.TouchdownCurveAddress), Word(built, GameModule.TouchdownCurveCallRomOffset));
        Assert.Equal(40, session.LoadMissions().Missions.Single(m => m.Id == id).Rows[landing].Value);

        // Off again: the original call, no entry.
        draft = new MissionDraft(session.LoadMissions().Missions.Single(m => m.Id == id), null);
        draft.SetSeparateTouchdown(false);
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x0C0C4FC2u, Word(built, GameModule.TouchdownCurveCallRomOffset));
    }

    [RealRomFact]
    public void GoodLanding_ValuesRunwaysAndTheirPatches()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Landing", false, rom, TestRomLocator.RomPath!);
        IReadOnlyList<MissionInfo> missions = session.LoadMissions().Missions;

        // Hang Glider Beginner: a larger pad and softer limits.
        var hgId = new TestId(0, Vehicle.HangGlider, 0);
        var draft = new MissionDraft(missions.Single(m => m.Id == hgId), null);
        Assert.Contains(LandingValue.PadRadius, draft.LandingValuesShown);
        Assert.Contains(LandingValue.HangGliderGoodVertical, draft.LandingValuesShown);
        draft.SetLandingValue(LandingValue.PadRadius, GameUnits.FromMeters(35));
        draft.SetLandingValue(LandingValue.HangGliderGoodVertical, 25);
        draft.SetLandingValue(LandingValue.HangGliderGoodForward, 15); // the original: no setting
        Assert.Equal(2, draft.Settings.Landing.Count);
        session.SaveMission(draft.Mission, draft.Comm, draft.SettingsToSave());

        // A Gyrocopter mission with a runway: a wider runway and a harder crash limit.
        MissionInfo gc = missions.First(m => m.Id.Vehicle == Vehicle.Gyrocopter && m.File.RunwayWidths.Count > 0);
        draft = new MissionDraft(gc, null);
        float width = draft.RunwayWidths[0];
        draft.SetRunwayWidth(0, width * 2);
        draft.SetLandingValue(LandingValue.GyrocopterCrash, LandingValues.Stored(LandingValue.GyrocopterCrash, 40));
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        GameModuleCondition hg = Entry(built, hgId);
        Assert.Equal(GameUnits.FromMeters(35), hg.PadRadius!.Value, 3);
        Assert.Equal([50f, 50f, 25f, 15f], hg.VehicleLimits);
        GameModuleCondition gcEntry = Entry(built, gc.Id);
        Assert.Null(gcEntry.PadRadius);
        Assert.Equal(40f / 2.52f, gcEntry.VehicleLimits[0], 3);
        Assert.Equal(7.937f, gcEntry.VehicleLimits[1], 3);
        Assert.Equal(width * 2, BuiltMission(built, gc.Id).RunwayWidths[0]);

        int module = built.Length - GameModule.RomReserve;
        Assert.Equal(128u, Word(built, module + 0x0C) & 128u); // landing values are set in every flight frame
        Assert.Equal(0x3C01801Fu, Word(built, 0x7B1F8));        // lui at, 0x801F instead of 50.0
        Assert.Equal(0xC4285040u, Word(built, 0x7B208));        // lwc1 f8, 0x5040(at): landing vertical
        Assert.Equal(0xC428504Cu, Word(built, 0x7B2EC));        // lwc1 f8, 0x504C(at): good forward
        Assert.Equal(DataArea.LiveLandingAddress, 0x801F5040u);
        Assert.Equal(CodeArea.Jal(GameModuleImage.FlightFrameHookAddress), Word(built, (int)(0x802ED45Cu - 0x80278AD0u)));
        Assert.Equal(0x414B2B02u, Word(built, 0xDACF8)); // the constants themselves stay: the module sets their RAM copies

        string report = MissionReport.Create(session);
        Assert.Contains("Landing: HangGliderGoodVertical 25", report);
        Assert.Contains("Runway widths:", report);

        // Imported again, the values come back.
        RomImport import = ProjectImporter.Analyze(N64Rom.FromBytes(built), rom, RomLayout.PilotwingsUsa, "x");
        MissionSettings back = import.Settings.Missions.Single(m => m.Id == hgId);
        Assert.Equal(25f, back.Landing[LandingValue.HangGliderGoodVertical]);
        Assert.False(back.Landing.ContainsKey(LandingValue.HangGliderGoodForward));
        Assert.Equal(GameUnits.FromMeters(35), back.Landing[LandingValue.PadRadius], 3);
        Assert.Equal(40f / 2.52f, import.Settings.Missions.Single(m => m.Id == gc.Id).Landing[LandingValue.GyrocopterCrash], 3);
    }

    [RealRomFact]
    public void WithoutLandingValues_TheGameCodeStaysOriginal()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Plain", false, rom, TestRomLocator.RomPath!);
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.Equal(0x3C014248u, Word(built, 0x7B1F8));
        Assert.Equal(0x0C0C4FC2u, Word(built, GameModule.TouchdownCurveCallRomOffset));
        Assert.Equal(0u, Word(built, built.Length - GameModule.RomReserve + 0x0C) & 128u);
    }
}
