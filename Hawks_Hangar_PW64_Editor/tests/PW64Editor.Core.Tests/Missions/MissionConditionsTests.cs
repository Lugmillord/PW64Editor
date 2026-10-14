using System.Buffers.Binary;
using PW64Editor.Core.Code;
using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Rom;
using PW64Editor.Core.Project;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;
using PW64Editor.Core.Text;
using PW64Editor.Core.Workspace;

namespace PW64Editor.Core.Tests.Missions;

public class MissionConditionsTests
{
    private static MissionCatalogResult Catalog(N64Rom rom)
    {
        GameFileSystem files = GameFileSystem.Read(rom, RomLayout.PilotwingsUsa);
        return MissionCatalog.Build(
            files.Files.Where(f => f.FileType == MissionFile.FormType).Select(f => new MissionSource(f.TableIndex, f.GroupIndex, f.Data, false)),
            new MissionTables(rom.Data, MissionLayout.PilotwingsUsa));
    }

    [RealRomFact]
    public void RetailConditions_AreReadAndWrittenBackTheSame()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        foreach (MissionInfo mission in Catalog(rom).Missions)
        {
            MissionComm comm = mission.File.Comm.Clone();
            MissionConditions conditions = MissionConditions.FromComm(comm);
            Assert.Equal(comm.Condition, conditions.RetailMode(comm));
            conditions.ApplyTo(comm);
            Assert.Equal(mission.File.Comm.ToArray(), comm.ToArray());
        }

        MissionCatalogResult catalog = Catalog(rom);
        MissionComm rb = catalog.Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0)).File.Comm;
        Assert.Equal(MissionConditions.All, MissionConditions.FromComm(rb).Required[ConditionObject.Balloons]); // mode 2, one balloon
        MissionComm gc = catalog.Missions.Single(m => m.Id == new TestId(3, Vehicle.Gyrocopter, 1)).File.Comm;
        Assert.Equal(20, MissionConditions.FromComm(gc).Required[ConditionObject.Targets]);                   // mode 3, 20 of 30
        MissionComm hg = catalog.Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 0)).File.Comm;
        Assert.Equal(571f, MissionConditions.FromComm(hg).MinimumAltitude);                                    // mode 4
    }

    [RealRomFact]
    public void Combinations_NeedTheModule()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionComm rb = Catalog(rom).Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0)).File.Comm.Clone();

        // The user's case: altitude instead of the balloon – the retail game can do that alone.
        var altitude = new MissionConditions { MinimumAltitude = 120 };
        Assert.Equal(MissionCondition.MinimumAltitude, altitude.RetailMode(rb));
        altitude.ApplyTo(rb);
        Assert.Equal(MissionCondition.MinimumAltitude, rb.Condition);
        Assert.Equal(120f, rb.MinimumAltitude);
        Assert.Empty(MissionConditions.FromComm(rb).Required); // the balloon is no longer required

        // Balloon and altitude together, or with "crash gives 0": only the module.
        var both = new MissionConditions { MinimumAltitude = 120, Required = { [ConditionObject.Balloons] = MissionConditions.All } };
        Assert.True(both.NeedsModule(rb));
        both.ApplyTo(rb);
        Assert.Equal(MissionCondition.None, rb.Condition);
        var crash = new MissionConditions { CrashGivesZero = true, Required = { [ConditionObject.Balloons] = MissionConditions.All } };
        Assert.True(crash.NeedsModule(rb));
        Assert.Equal(MissionCondition.CrashGivesZero, new MissionConditions { CrashGivesZero = true }.RetailMode(rb));

        GameModuleCondition entry = both.ToModuleCondition(rb);
        Assert.Equal((byte)Vehicle.RocketBelt, entry.Vehicle);
        Assert.Equal([0, GameModule.RequiredAll, 0, 0, 0, 0, 0], entry.Required);
        Assert.Equal(120f, entry.MinimumAltitude);
    }

    [RealRomFact]
    public void AtLeast_UsesTheGamesLandNowOnlyWhenChosen()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionCatalogResult catalog = Catalog(rom);

        // Bull's Eye: 2 of 3 targets. Without "land when reached" the module counts, so the game
        // does not say "please land" and keeps the third target.
        MissionComm gc = catalog.Missions.Single(m => m.Id == new TestId(1, Vehicle.Gyrocopter, 1)).File.Comm.Clone();
        var two = new MissionConditions { Required = { [ConditionObject.Targets] = 2 } };
        Assert.True(two.CanLandWhenReached(gc));
        Assert.True(two.NeedsModule(gc));
        two.LandWhenReached = true;
        Assert.Equal(MissionCondition.SomeObjects, two.RetailMode(gc));

        // Retail mode 3 (Gyrocopter Pilot 2, 20 of 30 targets) keeps the game's behaviour.
        MissionComm p2 = catalog.Missions.Single(m => m.Id == new TestId(3, Vehicle.Gyrocopter, 1)).File.Comm;
        Assert.True(MissionConditions.FromComm(p2).LandWhenReached);

        // Not possible together with other conditions: the draft drops it.
        var draft = new MissionDraft(catalog.Missions.Single(m => m.Id == new TestId(1, Vehicle.Gyrocopter, 1)), null);
        draft.SetConditions(new MissionConditions { MinimumAltitude = 100, LandWhenReached = true, Required = { [ConditionObject.Targets] = 2 } });
        Assert.False(draft.Conditions.LandWhenReached);
        Assert.Equal(MissionCondition.None, draft.Comm.Condition);
    }

    [RealRomFact]
    public void StartAltitude_IsTheTakeoffHeight()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionInfo hg = Catalog(rom).Missions.Single(m => m.Id == new TestId(0, Vehicle.HangGlider, 0));
        float? start = MissionCatalog.StartAltitude(hg.File);
        Assert.NotNull(start);
        Assert.InRange(start!.Value, 50f, 100f); // the flight starts on the tower, above 50 m
    }

    [Fact]
    public void ModuleImage_ContainsTheConditionsTable()
    {
        var condition = new GameModuleCondition(2, (byte)Vehicle.Gyrocopter, 1, true, 50f, [3, 0, GameModule.RequiredAll, 0, 0, 0, 0]);
        byte[] image = GameModule.CreateImage(new GameModuleOptions(false) { Conditions = [condition] }, 0x7F0000, string.Empty);
        Span<byte> entry = image.AsSpan(GameModuleImage.ConditionsTableOffset, 16);
        Assert.Equal(new byte[] { (byte)Vehicle.Gyrocopter, 2, 1, 3, 3, 0, 0xFF, 0, 0, 0, 0, 0 }, entry[..12].ToArray());
        Assert.Equal(50f, BinaryPrimitives.ReadSingleBigEndian(entry[12..]));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(0x2C)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(0x0C))); // no debug output

        Assert.True(new GameModuleOptions(false) { Conditions = [condition] }.IsNeeded);
        IReadOnlyList<WordPatch> patches = GameModule.Patches(new GameModuleOptions(false) { Conditions = [condition] }, 0x7F0000, image.Length);
        Assert.Contains(patches, p => p.RomOffset == GameModule.ConditionModeCallRomOffset);
        Assert.DoesNotContain(patches, p => p.RomOffset == GameModule.DebugPrintfRomOffset);
    }

    [RealRomFact]
    public void Footnote_CombinesTheSettingsInThreeLines()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionInfo hg = Catalog(rom).Missions.Single(m => m.Id == new TestId(0, Vehicle.HangGlider, 0));
        var draft = new MissionDraft(hg, null);
        Assert.Null(draft.SetRowSource(2, PointSource.Time, 20));
        MissionComm? None(TestId _) => null;

        IReadOnlyList<string> simple = draft.Footnote(None);
        Assert.Equal(["Time points will be deducted when", "2'00\" have elapsed."], simple);

        draft.SetConditions(new MissionConditions { CrashGivesZero = true, MinimumAltitude = 50, Required = { [ConditionObject.Rings] = MissionConditions.All } });
        IReadOnlyList<string> combined = draft.Footnote(None);
        Assert.True(combined.Count <= MissionTextGenerator.FootnoteLines, string.Join(" | ", combined));
        Assert.All(combined, l => Assert.True(l.Length <= MissionTextGenerator.FootnoteWidth, l));
        string all = string.Join(" ", combined);
        Assert.Contains("all rings", all);
        Assert.Contains("35 m", all); // 50 game units
        Assert.Contains("Crash: 0 pts.", all);

        // Typed in: kept as it is.
        draft.SetFootnote(["My own note."]);
        Assert.Equal(["My own note."], draft.Footnote(None));
        draft.SetAutoFootnote(true, None);
        Assert.Equal(combined, draft.Footnote(None));
    }

    [RealRomFact]
    public void Footnote_ShowsTheAltitudeForFullPoints()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        MissionInfo hg = Catalog(rom).Missions.Single(m => m.Id == new TestId(3, Vehicle.HangGlider, 0)); // altitude points
        var draft = new MissionDraft(hg, null);
        draft.SetCurvePoints(ScoreCurveKind.Altitude, [new(0, 0), new(300, 35), new(500, 70)]);
        string all = string.Join(" ", draft.Footnote(_ => null));
        Assert.Contains("Full altitude points from 350 m on.", all); // 500 game units
        Assert.DoesNotContain("The higher the altitude", all);
    }

    [RealRomFact]
    public void Build_PutsCombinedConditionsIntoTheModule()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Conditions", false, rom, TestRomLocator.RomPath!);
        MissionInfo rb = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0));
        var draft = new MissionDraft(rb, null);

        // Retail-expressible: no module.
        draft.SetConditions(new MissionConditions { MinimumAltitude = 100 });
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());
        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        Assert.NotEqual(GameModule.Magic, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(rom.Size - GameModule.RomReserve)));

        // Combined: the module with one table entry, and the scoring function calls it.
        rb = session.LoadMissions().Missions.Single(m => m.Id == rb.Id);
        draft = new MissionDraft(rb, null);
        draft.SetConditions(new MissionConditions { MinimumAltitude = 100, Required = { [ConditionObject.Balloons] = MissionConditions.All } });
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());
        built = session.Build(false).Result.RomBuild.Rom.Data;
        int module = rom.Size - GameModule.RomReserve;
        Assert.Equal(GameModule.Magic, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(module)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(module + 0x2C)));
        Assert.Equal(CodeArea.Jal(GameModuleImage.ConditionsHookAddress),
            BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(GameModule.ConditionModeCallRomOffset)));
        Assert.Equal(CodeArea.Jal(GameModuleImage.RingsLeftHookAddress),
            BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(GameModule.RingsLeftCallRomOffset)));
        Assert.Equal(CodeArea.Jal(GameModuleImage.TargetsLeftHookAddress),
            BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(GameModule.TargetsLeftCallRomOffset)));

        // The settings survive saving and loading.
        MissionInfo reloaded = session.LoadMissions().Missions.Single(m => m.Id == rb.Id);
        Assert.True(new MissionDraft(reloaded, null).Conditions.SameAs(draft.Conditions));
    }

    [RealRomFact]
    public void CheckRings_AreNotCounted()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Rings", false, rom, TestRomLocator.RomPath!);

        // Dark Cavern: two invisible check rings unlock the goal ring.
        MissionInfo cavern = session.LoadMissions().Missions.Single(m => m.Id == new TestId(3, Vehicle.RocketBelt, 0));
        Assert.Equal(2, MissionCatalog.RingTypeCounts(cavern.File)[MissionCatalog.CheckRingType]);
        Assert.Equal(3, cavern.File.Comm.GetObjectCount(MissionObjectKind.Ring));
        Assert.Equal(1, MissionCatalog.ConditionTotal(cavern.File, ConditionObject.Rings));
        Assert.Equal(cavern.File.Comm.GetObjectCount(MissionObjectKind.Target), MissionCatalog.ConditionTotal(cavern.File, ConditionObject.Targets));
    }

    [RealRomFact]
    public void Report_ListsTheChangedMissions()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Report", false, rom, TestRomLocator.RomPath!);
        MissionInfo rb = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0));
        var draft = new MissionDraft(rb, null);
        draft.SetConditions(new MissionConditions { MinimumAltitude = 200, Required = { [ConditionObject.Balloons] = MissionConditions.All } });
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());

        string report = MissionReport.Create(session);
        Assert.Contains("changed 1", report);
        Assert.Contains("Balloons all of", report);
        Assert.Contains("highest altitude at least 200 units = 140 m", report);
        Assert.Contains("checked by: the game module", report);
        Assert.Contains("module entry: vehicle", report);
        Assert.Contains("COMM bytes changed", report);
        Assert.Contains("\"MinimumAltitude\": 200", report); // the settings as in project.json
    }

    [RealRomFact]
    public void Build_CorrectsAConditionByteSavedByAnEarlierVersion()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Stale", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(1, Vehicle.Gyrocopter, 1); // Bull's Eye, 3 targets
        MissionInfo gc = session.LoadMissions().Missions.Single(m => m.Id == id);
        var draft = new MissionDraft(gc, null);
        draft.SetConditions(new MissionConditions { Required = { [ConditionObject.Targets] = 2 } });

        // As an earlier version saved it: the game's "at least N" mode in the mission file.
        MissionComm stale = draft.Comm.Clone();
        stale.Condition = MissionCondition.SomeObjects;
        stale.RequiredObjects = 2;
        session.SaveMission(gc, stale, draft.SettingsToSave());
        Assert.Contains("the build writes None", MissionReport.Create(session));

        N64Rom built = session.Build(false).Result.RomBuild.Rom;
        MissionComm comm = GameFileSystem.Read(built, RomLayout.PilotwingsUsa).Files
            .Where(f => f.FileType == MissionFile.FormType)
            .Select(f => MissionFile.Parse(f.Data).Comm)
            .Single(c => c.Id == id);
        Assert.Equal(MissionCondition.None, comm.Condition);
    }

    [RealRomFact]
    public void GameUnits_MatchTheOriginalTexts()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Units", false, rom, TestRomLocator.RomPath!);

        // Hang Glider Class B 1: "ascend to 400m", condition value 571.
        MissionInfo hg = session.LoadMissions().Missions.Single(m => m.Id == new TestId(2, Vehicle.HangGlider, 0));
        Assert.Equal(400, MathF.Round(GameUnits.ToMeters(hg.File.Comm.MinimumAltitude)));
        Assert.Contains("400m", session.LoadTexts().Texts.Single(t => t.Name == "B_HG_1_M").Markup);
        Assert.Equal(571f, GameUnits.FromMeters(GameUnits.ToMeters(571f)), 3);

        // The default altitude curve reaches its full points at 500 m.
        ScoreCurve curve = MissionCurves.Default(ScoreCurveKind.Altitude, Vehicle.HangGlider, 10);
        Assert.Equal(500, MathF.Round(GameUnits.ToMeters(curve.Points[^1].X)));
    }

    [RealRomFact]
    public void Build_DistanceRowConditionDemoAndLiveValues()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.True(DemoButton.MatchesRom(rom.Data));
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Step31", false, rom, TestRomLocator.RomPath!);
        MissionInfo rb = session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.RocketBelt, 0));
        var draft = new MissionDraft(rb, null);
        Assert.Null(draft.SetRowSource(draft.Rows.Count - 1, PointSource.Distance, 10)); // 4 rows: the last one
        Assert.Equal(10, draft.EvaluateRows(_ => null).Single(r => r.Source == PointSource.Distance).Value);
        draft.SetConditions(new MissionConditions { MinimumDistance = 1000 });
        Assert.True(draft.Conditions.NeedsModule(draft.Comm)); // the game has no distance condition
        Assert.True(draft.CanHideDemo);
        draft.SetHideDemo(true);
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());
        session.SaveFeatures(new PW64Editor.Core.Project.GameFeatureSettings { DebugOutput = true, LiveValues = true });

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        uint Word(long offset) => BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan((int)offset));
        int module = rom.Size - GameModule.RomReserve;

        // Distance row: the result sheet's jump table entry of row type 10.
        Assert.Equal(GameModuleImage.DistanceRowAddress, Word(GameModule.DistanceRowTableRomOffset));

        // Distance condition in the table (flags 4, distance at +16) and the HUD text.
        int entry = module + GameModuleImage.ConditionsTableOffset;
        Assert.Equal(1, built[entry]); // vehicle
        Assert.Equal(4, built[entry + 3]);
        Assert.Equal(1000f, BinaryPrimitives.ReadSingleBigEndian(built.AsSpan(entry + 16)));
        Assert.NotEqual(0, BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(module + 0x94)));

        // Live values: flag 16, all four values, the flight frame calls go to the module.
        Assert.Equal(16u, Word(module + 0x0C) & 16u);
        Assert.Equal(15u, Word(module + 0x90));
        Assert.Equal(CodeArea.Jal(GameModuleImage.FlightFrameHookAddress), Word(0x802ED288 - 0x80278AD0));

        // Demo button of the Rocket Belt hidden: lui at, 0xA000 (HG and GC keep theirs).
        Assert.Equal(0x3C01A000u, Word(0x80349368 - 0x80278AD0));
        Assert.Equal(0x03210804u, Word(0x80349374 - 0x80278AD0)); // sllv at, at, t9
        Assert.Equal(0x04210005u, Word(0x80349378 - 0x80278AD0)); // bgez at, same target

        // Footnote and report know the distance.
        Assert.Contains("flown", string.Join(" ", draft.Footnote(_ => null)));
        string report = MissionReport.Create(session);
        Assert.Contains("distance at least 1000 units = 700 m", report);
        Assert.Contains("\"View Demo\" button: hidden", report);
    }

    [RealRomFact]
    public void Build_StartFuelAndUnlimitedFuel()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Fuel", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(1, Vehicle.RocketBelt, 1); // Touch & Go starts with a quarter tank
        MissionInfo rb = session.LoadMissions().Missions.Single(m => m.Id == id);
        Assert.Equal(0.25f, rb.File.StartFuel);
        var draft = new MissionDraft(rb, null);
        Assert.True(draft.CanSetFuel);
        draft.SetStartFuel(0.6f);
        draft.SetUnlimitedFuel(true);
        session.SaveMission(rb, draft.Comm, draft.SettingsToSave());
        Assert.False(new MissionDraft(session.LoadMissions().Missions.Single(m => m.Id == id), null).IsChanged);
        Assert.False(new MissionDraft(session.LoadMissions().Missions.Single(m => m.Id == new TestId(0, Vehicle.HangGlider, 0)), null).CanSetFuel);

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        MissionFile file = GameFileSystem.Read(N64Rom.FromBytes(built), RomLayout.PilotwingsUsa).Files
            .Where(f => f.FileType == MissionFile.FormType).Select(f => MissionFile.Parse(f.Data)).Single(f => f.Comm.Id == id);
        Assert.Equal(0.6f, file.StartFuel);
        Assert.Equal(MissionCondition.AllObjects, file.Comm.Condition); // the game's own condition stays

        int module = rom.Size - GameModule.RomReserve;
        int entry = module + GameModuleImage.ConditionsTableOffset;
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(module + 0x2C)));
        Assert.Equal(8, built[entry + 3]); // only the fuel flag
        Assert.Equal(CodeArea.Jal(GameModuleImage.FlightFrameHookAddress),
            BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan((int)(0x802ED45Cu - 0x80278AD0u))));
        Assert.Contains("unlimited yes", MissionReport.Create(session));
    }
    [RealRomFact]
    public void Build_PenaltyMessagesShowTheMissionsPoints()
    {
        using var temp = new TempDirectory();
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        Assert.False(ProjectBuilder.NeedsPenaltyMessages(GameFileSystem.Read(rom, RomLayout.PilotwingsUsa).Files)); // retail: the texts fit

        EditorSession session = EditorSession.Create(temp.Combine("hack"), "Penalty", false, rom, TestRomLocator.RomPath!);
        var id = new TestId(0, Vehicle.Gyrocopter, 0);
        MissionInfo gc = session.LoadMissions().Missions.Single(m => m.Id == id);
        Assert.Equal(-5, gc.File.Comm.HitDeduction);
        var draft = new MissionDraft(gc, null);
        draft.SetHitCost(12);
        session.SaveMission(gc, draft.Comm, draft.SettingsToSave());

        byte[] built = session.Build(false).Result.RomBuild.Rom.Data;
        int module = rom.Size - GameModule.RomReserve;
        Assert.Equal(GameModule.Magic, BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(module)));
        // The bold digits 0-9 for the module.
        Assert.Equal(Enumerable.Range(0x60, 10).Select(c => (ushort)c),
            Enumerable.Range(0, 10).Select(i => BinaryPrimitives.ReadUInt16BigEndian(built.AsSpan(module + 0xD4 + (i * 2)))));
        foreach (int call in new[] { 0x7E140, 0xAD0C4 })
        {
            Assert.Equal(CodeArea.Jal(GameModuleImage.PenaltyHitHookAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(call)));
        }

        Assert.Equal(CodeArea.Jal(GameModuleImage.PenaltyAreaHookAddress), BinaryPrimitives.ReadUInt32BigEndian(built.AsSpan(0x83460)));
    }
}
