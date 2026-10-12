using PW64Editor.Core.FileSystem;
using PW64Editor.Core.Missions;
using PW64Editor.Core.Rom;
using PW64Editor.Core.SaveGame;
using PW64Editor.Core.Tests.TestSupport;

namespace PW64Editor.Core.Tests.Missions;

public class MissionTests
{
    private static (byte[] Rom, GameFileSystem Files) Game()
    {
        N64Rom rom = N64Rom.Load(TestRomLocator.RomPath!);
        return (rom.Data, GameFileSystem.Read(rom, RomLayout.PilotwingsUsa));
    }

    private static MissionCatalogResult Catalog()
    {
        (byte[] rom, GameFileSystem files) = Game();
        IEnumerable<MissionSource> sources = files.Files
            .Where(f => f.FileType == MissionFile.FormType)
            .Select(f => new MissionSource(f.TableIndex, f.GroupIndex, f.Data, false));
        return MissionCatalog.Build(sources, new MissionTables(rom, MissionLayout.PilotwingsUsa));
    }

    private static MissionInfo Find(MissionCatalogResult catalog, Vehicle vehicle, int classIndex, int test) =>
        catalog.Missions.Single(m => m.Id == new TestId(classIndex, vehicle, test));

    [Fact]
    public void ScoreCurve_EvaluatesLikeTheGame()
    {
        // GC Beginner time curve: plateau, then two linear pieces.
        ScoreCurve curve = ScoreCurve.Create([new(0, 30), new(90.5f, 30), new(120, 15), new(210, 0)]);
        Assert.Equal(30f, curve.Evaluate(-5));
        Assert.Equal(30f, curve.Evaluate(90.5f));
        Assert.Equal(22.5f, curve.Evaluate(105.25f), 3);
        Assert.Equal(15f, curve.Evaluate(120));
        Assert.Equal(0f, curve.Evaluate(500));
        Assert.Equal(30f, curve.Maximum);

        // A step: two points with the same x.
        ScoreCurve step = ScoreCurve.Create([new(0, 0), new(150, 0), new(150, 40), new(177, 70)]);
        Assert.Equal(0f, step.Evaluate(149.9f));
        Assert.Equal(40f, step.Evaluate(150));

        byte[] data = new byte[ScoreCurve.Size];
        curve.Write(data);
        Assert.Equal(curve.Points, ScoreCurve.Read(data).Points);
        Assert.Throws<ArgumentException>(() => ScoreCurve.Create(Enumerable.Range(0, 11).Select(i => new CurvePoint(i, i))));
    }

    [RealRomFact]
    public void AllMissions_AreReadAndWrittenBackUnchanged()
    {
        (_, GameFileSystem files) = Game();
        List<GameFile> missions = files.Files.Where(f => f.FileType == MissionFile.FormType).ToList();
        Assert.Equal(61, missions.Count);
        foreach (GameFile file in missions)
        {
            MissionFile mission = MissionFile.Parse(file.Data);
            Assert.Equal(file.Data, mission.WithComm(mission.Comm));
            Assert.Equal(MissionComm.Size, mission.Comm.ToArray().Length);

            // Every curve written back gives the same bytes.
            MissionComm copy = mission.Comm.Clone();
            foreach (ScoreCurveKind kind in Enum.GetValues<ScoreCurveKind>())
            {
                copy.SetCurve(kind, mission.Comm.GetCurve(kind));
            }

            foreach (PhotoCurveKind kind in Enum.GetValues<PhotoCurveKind>())
            {
                copy.SetPhotoCurve(kind, mission.Comm.GetPhotoCurve(kind));
            }

            Assert.Equal(mission.Comm.ToArray(), copy.ToArray());
        }
    }

    [RealRomFact]
    public void Catalog_ReadsNamesTablesAndValues()
    {
        MissionCatalogResult catalog = Catalog();
        Assert.Empty(catalog.Problems);
        Assert.Equal(61, catalog.Missions.Count);

        // Menu order: Hang Glider first, Birdman last.
        Assert.Equal(Vehicle.HangGlider, catalog.Missions[0].Id.Vehicle);
        Assert.Equal(Vehicle.Birdman, catalog.Missions[^1].Id.Vehicle);

        MissionInfo gcBeginner = Find(catalog, Vehicle.Gyrocopter, 0, 0);
        Assert.Equal("E_GC_1", gcBeginner.File.TextBase);
        Assert.Equal("E_GC_1_N", gcBeginner.TitleTextName);
        Assert.Equal("GC_E_S1", gcBeginner.MissionScreenTextName);
        Assert.Equal("GC_E_S2", gcBeginner.ResultSheetTextName);
        Assert.Equal(MissionCondition.None, gcBeginner.File.Comm.Condition);
        Assert.Equal(-5, gcBeginner.File.Comm.HitDeduction);

        // Ring 20, Time 30, Landing Accuracy 30 (5.477² for the Gyrocopter), Landing Impact 20.
        Assert.Equal(
            [(PointSource.Rings, 20), (PointSource.Time, 30), (PointSource.LandingAccuracy, 30), (PointSource.LandingImpact, 20)],
            gcBeginner.Rows.Select(r => (r.Source, r.Value ?? -1)));
        Assert.Equal(100, gcBeginner.KnownRowSum);

        // Hang Glider B3: "as close as possible to 3'00"", and the "Land now" time.
        MissionInfo hgB3 = Find(catalog, Vehicle.HangGlider, 2, 2);
        Assert.Equal(180f, hgB3.File.Comm.LandNowTime);
        Assert.Equal(70, hgB3.Rows[0].Value);

        // Condition 4 (altitude) in HG B1, condition 3 in GC P2 (20 of 30 targets).
        Assert.Equal(MissionCondition.MinimumAltitude, Find(catalog, Vehicle.HangGlider, 2, 0).File.Comm.Condition);
        Assert.Equal(571f, Find(catalog, Vehicle.HangGlider, 2, 0).File.Comm.MinimumAltitude);
        Assert.Equal(20f, Find(catalog, Vehicle.Gyrocopter, 3, 1).File.Comm.RequiredObjects);

        // Splitting balloons: two balloons with five pieces of 3 points each (RB B1).
        Assert.Equal(30, Find(catalog, Vehicle.RocketBelt, 2, 0).Rows[0].Value);

        // Cannonball: four targets of 25 points, the shared texts of all levels.
        MissionInfo cannon = Find(catalog, Vehicle.Cannonball, 1, 0);
        Assert.Equal([25, 25, 25, 25], cannon.Rows.Select(r => r.Value ?? -1));
        Assert.Equal(PointSource.CannonballTarget1, cannon.Rows[0].Source);
        Assert.Equal("CB_L123_S1", cannon.MissionScreenTextName);

        // The game's table has no mission screen text for the fourth target of level 3.
        Assert.Equal(10, cannon.SharesMissionScreenTextWith.Count);
        Assert.Null(Find(catalog, Vehicle.Cannonball, 2, 3).MissionScreenTextName);
        Assert.Equal(3, cannon.SharesStatementWith.Count);

        // Jumble Hopper: own mission screen text per level, one shared result sheet.
        MissionInfo hopper = Find(catalog, Vehicle.JumbleHopper, 1, 0);
        Assert.Equal("HP_L2_S1", hopper.MissionScreenTextName);
        Assert.Equal("HP_L123_S2", hopper.ResultSheetTextName);
        Assert.Empty(hopper.SharesMissionScreenTextWith);
        Assert.Equal(2, hopper.SharesResultSheetTextWith.Count);

        // The three photo missions of the Hang Glider have photo curves (and Cannonball level 1,
        // target 1 a leftover of 25 points in every slot); the hopper's area deduction.
        Assert.Equal(3, catalog.Missions.Count(m => m.Id.Vehicle == Vehicle.HangGlider && !m.File.Comm.PhotoCurvesAreEmpty));
        Assert.False(Find(catalog, Vehicle.HangGlider, 1, 0).File.Comm.PhotoCurvesAreEmpty);
        Assert.Equal(2f, hopper.File.Comm.OutOfAreaDeduction);

        // Birdman has no score sheet in the tables.
        MissionInfo birdman = Find(catalog, Vehicle.Birdman, 0, 0);
        Assert.Empty(birdman.Rows);
        Assert.Null(birdman.MissionScreenTextName);
    }

    [RealRomFact]
    public void Islands_OfMissionsMatchTheClassTable()
    {
        foreach (MissionInfo mission in Catalog().Missions)
        {
            Assert.Equal(mission.ClassIsland, mission.Island);
        }

        Assert.Equal(2, Find(Catalog(), Vehicle.HangGlider, 3, 0).Island); // Pilot class on Little States
    }

    [RealRomFact]
    public void MedalLimits_AreReadFromTheCode()
    {
        (byte[] rom, _) = Game();
        IReadOnlyList<MedalLimits> limits = new MissionTables(rom, MissionLayout.PilotwingsUsa).MedalLimits();
        Assert.Equal(new MedalLimits(70, 80, 90), limits[0]);
        Assert.Equal(new MedalLimits(140, 160, 180), limits[1]);
        Assert.Equal(new MedalLimits(210, 240, 270), limits[3]);
        Assert.Equal(new MedalLimits(70, 80, 90), limits[6]);
        Assert.Equal(new MedalLimits(0, 0, 0), limits[7]);
    }

    [RealRomFact]
    public void CurveValues_MatchTheNumbersInTheMissionScreenTexts()
    {
        // Every curve row equals the number the original text prints for it.
        (byte[] rom, GameFileSystem files) = Game();
        var texts = PW64Editor.Core.Text.GameTextLibrary.Create(
            PW64Editor.Core.Text.TextFont.Load(files), PW64Editor.Core.Text.GameTextFile.FindFile(files).Data, false);
        Dictionary<string, string> markup = texts.Texts.GroupBy(t => t.Name).ToDictionary(g => g.Key, g => g.First().Markup);
        int checkedRows = 0;
        foreach (MissionInfo mission in Catalog().Missions.Where(m => m.MissionScreenTextName is not null))
        {
            List<int> numbers = System.Text.RegularExpressions.Regex.Matches(markup[mission.MissionScreenTextName!], @"\[b\](\d+)\[/b\] pts")
                .Select(m => int.Parse(m.Groups[1].Value)).ToList();
            for (int i = 0; i < mission.Rows.Count; i++)
            {
                if (mission.Rows[i].Kind is PointValueKind.Curve or PointValueKind.OtherMission)
                {
                    Assert.Equal(numbers[i], mission.Rows[i].Value);
                    checkedRows++;
                }
            }
        }

        Assert.True(checkedRows > 100, $"only {checkedRows} rows checked");
    }
}
